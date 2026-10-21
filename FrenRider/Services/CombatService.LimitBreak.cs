using System;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FrenRider.Models;
using ActionRow = Lumina.Excel.Sheets.Action;
using BNpcBase = Lumina.Excel.Sheets.BNpcBase;

namespace FrenRider.Services;

internal readonly record struct LimitBreakObservation(bool Active, bool InCombat, bool CanAct,
    bool Pending, LimitBreakRole Role, int AvailableBars, int FullBars,
    float PartyHpPercent, int KoMembers, bool HasTarget, bool TargetIsBoss,
    float TargetHpPercent, int EnemiesHit);

internal static class LimitBreakPolicy
{
    internal static LimitBreakRole Role(uint job) => job switch
    {
        1 or 3 or 19 or 21 or 32 or 37 => LimitBreakRole.Tank,
        6 or 24 or 28 or 33 or 40 => LimitBreakRole.Healer,
        2 or 4 or 20 or 22 or 29 or 30 or 34 or 39 or 41 or 43 => LimitBreakRole.Melee,
        5 or 23 or 31 or 38 => LimitBreakRole.PhysicalRanged,
        7 or 25 or 26 or 27 or 35 or 36 or 42 => LimitBreakRole.MagicalRanged,
        _ => LimitBreakRole.None,
    };

    internal static int Tier(LimitBreakGauge requirement, int available, int full)
    {
        if (available is < 1 or > 3 || full < 1 || full > available || !Enum.IsDefined(requirement)) return 0;
        var required = requirement == LimitBreakGauge.FullAvailable ? available : (int)requirement;
        return full >= required ? required : 0;
    }

    internal static int Decide(LimitBreakSettings settings, LimitBreakObservation s)
    {
        if (!settings.IsValid || !s.Active || !s.InCombat || !s.CanAct || s.Pending) return 0;
        if (s.Role == LimitBreakRole.Tank)
            return settings.Tank.Enabled && s.PartyHpPercent <= settings.Tank.PartyHpPercent
                ? Tier(settings.Tank.Gauge, s.AvailableBars, s.FullBars) : 0;
        if (s.Role == LimitBreakRole.Healer)
        {
            var p = settings.Healer;
            if (!p.Enabled) return 0;
            if (p.Revive && s.KoMembers >= p.MinimumKoMembers && s.AvailableBars == 3 && s.FullBars == 3) return 3;
            // A healing policy cannot consume LB3 or substitute healing for revival.
            return p.Heal && s.FullBars is 1 or 2 && s.FullBars <= s.AvailableBars
                && s.PartyHpPercent <= p.PartyHpPercent ? s.FullBars : 0;
        }
        var damage = settings.Damage(s.Role);
        return damage?.Enabled == true && s.HasTarget
            && (damage.Target != LimitBreakTarget.BossOnly || s.TargetIsBoss)
            && s.TargetHpPercent <= damage.TargetHpPercent && s.EnemiesHit >= damage.MinimumEnemies
                ? Tier(damage.Gauge, s.AvailableBars, s.FullBars) : 0;
    }
}

public partial class CombatService
{
    private long lastLimitBreakRequest;
    private int limitBreakRequestedUnits;
    private uint limitBreakRequestedAction;
    private bool limitBreakCastObserved;
    private ulong limitBreakCharacter;
    private uint limitBreakTerritory;

    private void ResetLimitBreakRequest()
    {
        lastLimitBreakRequest = 0;
        limitBreakRequestedUnits = 0;
        limitBreakRequestedAction = 0;
        limitBreakCastObserved = false;
    }

    internal void RecordLimitBreakRequest(uint action, int units, long now)
    {
        lastLimitBreakRequest = now;
        limitBreakRequestedUnits = units;
        limitBreakRequestedAction = action;
        limitBreakCastObserved = false;
    }

    internal bool IsLimitBreakRequestPending(int units, bool casting, uint castAction, bool animationLock, long now)
    {
        if (limitBreakRequestedAction == 0) return false;
        limitBreakCastObserved |= casting && castAction == limitBreakRequestedAction;
        if (casting || animationLock) return true;
        if (units < limitBreakRequestedUnits || limitBreakCastObserved || now - lastLimitBreakRequest >= 5000)
        {
            ResetLimitBreakRequest();
            return false;
        }
        return true;
    }

    private unsafe void CheckLimitBreak(CharacterConfig config)
    {
        var settings = config.LimitBreak;
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null || !plugin.AreCombatRotationsEnabled || ShouldSuppressFrenRiderCombatCommands
            || !Plugin.Condition[ConditionFlag.InCombat] || !settings.IsValid) return;
        var role = LimitBreakPolicy.Role(player.ClassJob.RowId);
        if (!(role == LimitBreakRole.Tank ? settings.Tank.Enabled
            : role == LimitBreakRole.Healer ? settings.Healer.Enabled : settings.Damage(role)?.Enabled == true)) return;
        if (limitBreakCharacter != player.GameObjectId || limitBreakTerritory != Plugin.ClientState.TerritoryType)
        {
            ResetLimitBreakRequest();
            limitBreakCharacter = player.GameObjectId;
            limitBreakTerritory = Plugin.ClientState.TerritoryType;
        }
        if (player.IsDead || player.CurrentHp == 0 || Plugin.Condition[ConditionFlag.BetweenAreas]
            || Plugin.Condition[ConditionFlag.BetweenAreas51] || Plugin.Condition[ConditionFlag.Mounted]
            || Plugin.Condition[ConditionFlag.Mounting71] || Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]
            || Plugin.Condition[ConditionFlag.LoggingOut]) return;
        var gauge = LimitBreakController.Instance();
        var actions = ActionManager.Instance();
        if (gauge == null || actions == null || gauge->IsPvP || gauge->BarUnits == 0
            || gauge->BarCount is < 1 or > 3) return;
        var now = Environment.TickCount64;
        if (IsLimitBreakRequestPending(gauge->CurrentUnits, player.IsCasting, player.CastActionId,
            actions->AnimationLock > 0, now)) return;
        if (player.IsCasting || actions->AnimationLock > 0 || actions->QueuedActionId != 0) return;

        // Observe only this party. Missing/offline members cannot supply HP or KO proof.
        var members = Plugin.PartyList.Select(p => p.GameObject).OfType<IBattleChara>()
            .Where(p => p.MaxHp > 0 && Vector3.Distance(player.Position, p.Position) <= 50).ToArray();
        if (members.Length == 0) return;
        foreach (var member in members.Where(p => p.IsCasting))
            for (byte tier = 0; tier < 3; tier++)
                if (member.CastActionId == gauge->GetActionId((Character*)member.Address, tier)) return;

        var observation = new LimitBreakObservation(true, true, true, false, role, gauge->BarCount,
            gauge->CurrentUnits / gauge->BarUnits,
            members.Average(p => 100f * p.CurrentHp / p.MaxHp), members.Count(p => p.CurrentHp == 0),
            false, false, 100, 0);
        var damage = settings.Damage(role);
        // Damage thresholds are evaluated against the actual chosen native action's footprint.
        var tierToUse = damage == null ? LimitBreakPolicy.Decide(settings, observation)
            : LimitBreakPolicy.Tier(damage.Gauge, observation.AvailableBars, observation.FullBars);
        if (tierToUse == 0) return;
        var action = gauge->GetActionId((Character*)player.Address, (byte)(tierToUse - 1));
        if (action == 0 || !Plugin.DataManager.GetExcelSheet<ActionRow>().TryGetRow(action, out var row)) return;
        IBattleChara? target = player;
        if (damage != null)
        {
            var enemies = Plugin.ObjectTable.OfType<IBattleNpc>()
                .Where(p => p.IsTargetable && !p.IsDead && p.CurrentHp > 0 && p.MaxHp > 0
                    && ((Character*)p.Address)->CharacterData.IsHostile
                    && ((Character*)p.Address)->CharacterData.InCombat).ToArray();
            var candidates = damage.Target == LimitBreakTarget.CurrentTarget
                ? enemies.Where(p => p.GameObjectId == Plugin.TargetManager.Target?.GameObjectId)
                : enemies.AsEnumerable();
            target = candidates.Select(p => new
                {
                    Target = p,
                    Boss = Plugin.DataManager.GetExcelSheet<BNpcBase>().TryGetRow(p.BaseId, out var npc) && npc.Rank is 2 or 6,
                    Hits = CountLimitBreakHits(role, player.Position, p, enemies, row.EffectRange),
                })
                .Where(p => LimitBreakPolicy.Decide(settings, observation with
                {
                    HasTarget = true, TargetIsBoss = p.Boss,
                    TargetHpPercent = 100f * p.Target.CurrentHp / p.Target.MaxHp, EnemiesHit = p.Hits,
                }) == tierToUse
                    && actions->GetActionStatus(ActionType.Action, action, p.Target.GameObjectId) == 0
                    && ActionManager.GetActionInRangeOrLoS(action,
                        (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)player.Address,
                        (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)p.Target.Address) == 0)
                .OrderByDescending(p => p.Hits).ThenBy(p => p.Target.GameObjectId).Select(p => p.Target).FirstOrDefault();
            if (target == null) return;
        }
        if (actions->GetActionStatus(ActionType.Action, action, target.GameObjectId) != 0) return;
        var position = target.Position;
        var submitted = row.TargetArea
            ? actions->UseActionLocation(ActionType.Action, action, target.GameObjectId, &position)
            : actions->UseAction(ActionType.Action, action, target.GameObjectId);
        if (!submitted) return;
        RecordLimitBreakRequest(action, gauge->CurrentUnits, now);
    }

    internal static int CountLimitBreakHits(LimitBreakRole role, Vector3 origin, IBattleChara target,
        IBattleNpc[] enemies, float radius)
    {
        if (role == LimitBreakRole.Melee) return 1;
        if (role == LimitBreakRole.MagicalRanged)
            return enemies.Count(p => Vector3.Distance(p.Position, target.Position) <= radius + p.HitboxRadius);
        var direction = new Vector2(target.Position.X - origin.X, target.Position.Z - origin.Z);
        if (direction.LengthSquared() < .001f) return 0;
        direction = Vector2.Normalize(direction);
        return enemies.Count(p =>
        {
            var delta = new Vector2(p.Position.X - origin.X, p.Position.Z - origin.Z);
            var forward = Vector2.Dot(delta, direction);
            // Physical ranged LB is a 4-yalm-wide line, extending to the action's native range.
            return forward >= 0 && forward <= radius + p.HitboxRadius
                && Math.Abs(p.Position.Y - origin.Y) <= 2 + p.HitboxRadius
                && Math.Abs(delta.X * direction.Y - delta.Y * direction.X) <= 2 + p.HitboxRadius;
        });
    }
}
