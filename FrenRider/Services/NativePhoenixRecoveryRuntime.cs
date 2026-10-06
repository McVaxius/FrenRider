using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Hooking;
using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using Lumina.Excel.Sheets;

namespace FrenRider.Services;

internal sealed unsafe class NativePhoenixRecoveryRuntime : IPhoenixRecoveryRuntime, IDisposable
{
    private readonly Plugin plugin;
    private readonly ICallGateProvider<bool> holdProvider;
    private readonly Hook<ActionManager.Delegates.UseAction> useActionHook;
    private readonly Hook<ActionManager.Delegates.UseActionLocation> useLocationHook;
    private readonly List<(object Node, FieldInfo Field, bool Original)> movementChanges = new();
    private bool movementHeld;
    private bool actionsHeld;
    private bool preventPulls;
    private bool issuingItem;
    private ulong itemTarget;
    private bool navigating;
    private ulong navigationTarget;
    private Vector3 progressPosition;
    private long progressMs;
    private bool unreachable;
    private long nextMovementSettingsReadMs;

    internal NativePhoenixRecoveryRuntime(Plugin plugin)
    {
        this.plugin = plugin;
        holdProvider = Plugin.PluginInterface.GetIpcProvider<bool>("FrenRider.PhoenixDown.ShouldPauseDutyProgression");
        holdProvider.RegisterFunc(() => plugin.PhoenixDownRecoveryService.ShouldPauseDutyProgression());
        useActionHook = Plugin.GameInteropProvider.HookFromAddress<ActionManager.Delegates.UseAction>(
            ActionManager.Addresses.UseAction.Value, UseActionDetour);
        useLocationHook = Plugin.GameInteropProvider.HookFromAddress<ActionManager.Delegates.UseActionLocation>(
            ActionManager.Addresses.UseActionLocation.Value, UseLocationDetour);
        useActionHook.Enable();
        useLocationHook.Enable();
    }

    public PhoenixRecoveryFrame ReadFrame()
    {
        var local = Plugin.ObjectTable.LocalPlayer;
        var config = plugin.ConfigManager.GetActiveConfig();
        if (!Plugin.ClientState.IsLoggedIn || local == null || !config.Enabled || !config.UsePhoenixDownsForRecovery
            || !plugin.ConfigManager.TryGetLocalActiveConfig(out _))
            return new PhoenixRecoveryFrame { Config = config };
        var condition = Plugin.Condition;
        var gameMain = GameMain.Instance();
        var dutyId = gameMain == null ? 0u : gameMain->CurrentContentFinderConditionId;
        var inInstance = condition[ConditionFlag.BoundByDuty] || condition[ConditionFlag.BoundByDuty56]
            || condition[ConditionFlag.BoundByDuty95] || dutyId != 0;
        var duty = Plugin.DataManager.GetExcelSheet<ContentFinderCondition>()?.GetRowOrDefault(dutyId);
        var dutyMatchesTerritory = gameMain != null && gameMain->CurrentTerritoryTypeId == Plugin.ClientState.TerritoryType
            && duty?.TerritoryType.RowId == Plugin.ClientState.TerritoryType;
        var memberType = duty?.ContentMemberType.ValueNullable;
        var partySize = memberType is { } members
            ? members.TanksPerParty + members.HealersPerParty + members.MeleesPerParty + members.RangedPerParty : 0;
        var territory = Plugin.DataManager.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(Plugin.ClientState.TerritoryType);
        var outdoors = territory?.TerritoryIntendedUse.RowId == 1;
        var scope = PhoenixDownRecoveryService.ResolveScope(inInstance, dutyMatchesTerritory ? duty?.ContentType.RowId ?? 0 : 0, partySize, outdoors);
        if (scope == PhoenixRecoveryScope.None)
            return new PhoenixRecoveryFrame { Config = config };
        var actors = new List<PhoenixActor>();
        var partyMembers = Plugin.PartyList.Where(member => member.ContentId != 0).ToArray();
        var partyIds = partyMembers.Select(member => member.ContentId).ToArray();
        if (local != null)
        {
            foreach (var actor in Plugin.ObjectTable.OfType<IPlayerCharacter>())
            {
                if (actor.GameObjectId is 0 or 0xE0000000 || actor.Address == 0)
                    continue;
                // Zero HP alone can be a loading snapshot. Require both native death
                // and HP truth for a corpse; ambiguous actors cannot act as rescuers.
                if (actor.MaxHp == 0 || actor.IsDead != (actor.CurrentHp == 0))
                    continue;
                var native = (BattleChara*)actor.Address;
                var cast = native->GetCastInfo();
                var casting = cast != null && cast->IsCasting;
                var contentId = native->ContentId;
                var member = partyMembers.FirstOrDefault(member => member.EntityId == actor.EntityId
                    || contentId != 0 && member.ContentId == contentId);
                var party = member != null;
                if (party)
                    contentId = member!.ContentId;
                actors.Add(new PhoenixActor(actor.GameObjectId, contentId, party,
                    actor.IsDead && actor.CurrentHp == 0, actor.ClassJob.ValueNullable?.Role == 4,
                    actor.Position, actor.StatusList.Any(status => status.StatusId == 148),
                    casting ? cast->TargetId : 0,
                    casting && cast->ActionType == (byte)ActionType.Item && cast->ActionId == PhoenixDownRecoveryService.ItemId));
            }
        }
        var ready = Plugin.ClientState.IsLoggedIn && local != null && ((Character*)local.Address)->ContentId == Plugin.PlayerState.ContentId
            && plugin.ConfigManager.TryGetLocalActiveConfig(out _)
            && !condition[ConditionFlag.BetweenAreas] && !condition[ConditionFlag.BetweenAreas51]
            && !condition[ConditionFlag.OccupiedInCutSceneEvent] && !condition[ConditionFlag.WatchingCutscene]
            && !condition[ConditionFlag.OccupiedInQuestEvent] && !condition[ConditionFlag.Occupied33]
            && !condition[ConditionFlag.Occupied39] && !plugin.AutomationService.IsUtilityGateActive;
        return new PhoenixRecoveryFrame
        {
            Config = config, Ready = ready, CharacterId = Plugin.PlayerState.ContentId,
            TerritoryId = Plugin.ClientState.TerritoryType, DutyId = dutyId,
            Scope = scope,
            LocalId = local?.GameObjectId ?? 0, Actors = actors, PartyContentIds = partyIds,
            PartyHasDeadMember = partyMembers.Any(member => member.MaxHP > 0 && member.CurrentHP == 0),
            PartyHasLivingMember = partyMembers.Any(member => member.CurrentHP > 0),
            InCombat = condition[ConditionFlag.InCombat],
            Flying = condition[ConditionFlag.InFlight] || local != null && ((Character*)local.Address)->MoveController.MovementState == MovementStateOptions.Flying,
            Mounted = GameHelpers.IsMountedOrRidingOrMounting(), Casting = local?.IsCasting == true || condition[ConditionFlag.Casting],
        };
    }

    public PhoenixItemReadiness ReadItem(ulong targetId)
    {
        var manager = ActionManager.Instance();
        var local = Plugin.ObjectTable.LocalPlayer;
        var target = Plugin.ObjectTable.OfType<IPlayerCharacter>().FirstOrDefault(actor => actor.GameObjectId == targetId);
        if (manager == null || local == null || target == null)
            return default;
        var source = local.Position + new Vector3(0, 2, 0);
        var offset = target.Position + new Vector3(0, 2, 0) - source;
        var length = offset.Length();
        var lineOfSight = length < 0.01f || !BGCollisionModule.RaycastMaterialFilter(source, offset / length, out _, length);
        // The shared medicine group is also used by other potions. Check it
        // explicitly even when the Phoenix Down's own timer is not initialized.
        var medicine = manager->GetRecastGroupDetail(58);
        var medicineCooldown = medicine != null && medicine->IsActive && medicine->Elapsed < medicine->Total;
        return new PhoenixItemReadiness(GameHelpers.GetInventoryItemCount(PhoenixDownRecoveryService.ItemId, false) > 0,
            medicineCooldown || manager->IsRecastTimerActive(ActionType.Item, PhoenixDownRecoveryService.ItemId),
            manager->GetActionStatus(ActionType.Item, PhoenixDownRecoveryService.ItemId, targetId) == 0,
            lineOfSight);
    }

    public bool AdsOwnsDuty
    {
        get
        {
            try { return Plugin.PluginInterface.GetIpcSubscriber<bool>("ADS.IsDutyOwned").InvokeFunc(); }
            catch { return plugin.AdsIntegrationService.IsControllingDuty || plugin.AdsDutyIpcService.Current.IsOwned; }
        }
    }
    public bool TryGetAdsAcknowledgement(out bool acknowledged)
    {
        try
        {
            acknowledged = Plugin.PluginInterface.GetIpcSubscriber<bool>("ADS.IsPhoenixDownRecoveryHoldActive").InvokeFunc();
            return true;
        }
        catch
        {
            acknowledged = false;
            return false;
        }
    }

    public void SetHolds(bool movement, bool actions, bool blockPulls)
    {
        actionsHeld = actions;
        if (!actions)
            itemTarget = 0;
        preventPulls = blockPulls;
        if (movement && !movementHeld)
        {
            plugin.FollowService.SuspendForRecovery();
            plugin.MountService.PreemptFarChase("Phoenix Down recovery");
            nextMovementSettingsReadMs = 0;
        }
        movementHeld = movement;
        // Survivors may use combat AI movement to finish the current fight. Once
        // this client approaches or casts, that movement competes with recovery.
        if (movement && (!Plugin.Condition[ConditionFlag.InCombat] || actions || navigating))
        {
            var now = Environment.TickCount64;
            if (now >= nextMovementSettingsReadMs)
            {
                HoldBossModMovement();
                nextMovementSettingsReadMs = now + 1000;
            }
        }
        else
            RestoreBossModMovement();
    }

    private void HoldBossModMovement()
    {
        const BindingFlags staticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags instanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var exposed in Plugin.PluginInterface.InstalledPlugins.Where(p => p.IsLoaded
            && p.InternalName is "BossMod" or "BossModReborn"))
        {
            try
            {
                var instance = BossModExternalAutomationSnapshotProvider.FindLivePluginInstance(exposed, out var assembly, out _);
                var service = assembly?.GetType("BossMod.Service");
                var root = service?.GetProperty("Config", staticMembers)?.GetValue(null)
                    ?? service?.GetField("Config", staticMembers)?.GetValue(null);
                var nodes = root?.GetType().GetProperty("Nodes", instanceMembers)?.GetValue(root) as IEnumerable;
                if (instance == null || nodes == null)
                    continue;
                foreach (var node in nodes)
                {
                    if (node?.GetType().FullName != "BossMod.AI.AIConfig")
                        continue;
                    var field = node.GetType().GetField("ForbidMovement", instanceMembers);
                    if (field?.GetValue(node) is not bool original || original)
                        continue;
                    if (!movementChanges.Any(change => ReferenceEquals(change.Node, node)))
                        movementChanges.Add((node, field, original));
                    // In-memory only: do not notify the plugin's persistence event.
                    field.SetValue(node, true);
                }
            }
            catch (Exception ex) { Plugin.Log.Debug($"[Phoenix Down] Movement hold unavailable: {ex.Message}"); }
        }
    }

    private void RestoreBossModMovement()
    {
        foreach (var change in movementChanges)
        {
            try
            {
                if (change.Field.GetValue(change.Node) is true)
                    change.Field.SetValue(change.Node, change.Original);
            }
            catch (Exception ex) { Plugin.Log.Debug($"[Phoenix Down] Movement restore unavailable: {ex.Message}"); }
        }
        movementChanges.Clear();
    }

    public PhoenixApproachResult Approach(PhoenixActor target, float stopRange, long now)
    {
        try
        {
            var local = Plugin.ObjectTable.LocalPlayer;
            if (local == null || !Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady").InvokeFunc())
                return PhoenixApproachResult.Blocked;
            if (navigationTarget != target.Id)
            {
                StopApproach();
                navigationTarget = target.Id;
                progressPosition = local.Position;
                progressMs = now;
            }
            if (unreachable)
                return PhoenixApproachResult.Blocked;
            if (Vector3.DistanceSquared(local.Position, progressPosition) >= 1f)
            {
                progressPosition = local.Position;
                progressMs = now;
            }
            if (now - progressMs >= 10000)
            {
                StopNavigation();
                unreachable = true;
                return PhoenixApproachResult.Blocked;
            }
            if (!navigating)
            {
                HoldBossModMovement();
                var direction = Vector3.Normalize(local.Position - target.Position);
                var destination = target.Position + direction * (stopRange - 0.5f);
                navigating = Plugin.PluginInterface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo")
                    .InvokeFunc(destination, false);
                if (!navigating)
                {
                    unreachable = true;
                    return PhoenixApproachResult.Blocked;
                }
            }
            return PhoenixApproachResult.Moving;
        }
        catch { return PhoenixApproachResult.Blocked; }
    }

    private void StopNavigation()
    {
        if (!navigating)
            return;
        try { Plugin.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop").InvokeAction(); }
        catch { }
        navigating = false;
    }
    public void StopApproach()
    {
        StopNavigation();
        navigationTarget = 0;
        unreachable = false;
    }
    public void Dismount() => GameHelpers.SendChatCommand("/mount", "[Phoenix Down]");
    public bool UsePhoenixDown(ulong targetId)
    {
        itemTarget = targetId;
        issuingItem = true;
        try { return GameHelpers.UseItem(PhoenixDownRecoveryService.ItemId, false, targetId); }
        finally { issuingItem = false; }
    }

    private bool ShouldBlock(ActionType type, uint id, ulong targetId)
    {
        if ((issuingItem || actionsHeld) && type == ActionType.Item && id == PhoenixDownRecoveryService.ItemId)
            return targetId != itemTarget;
        if (actionsHeld)
            return true;
        if (!preventPulls || Plugin.Condition[ConditionFlag.InCombat])
            return false;
        if (type == ActionType.Action && Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()?.GetRowOrDefault(id)?.CanTargetHostile == true)
            return true;
        return Plugin.ObjectTable.FirstOrDefault(actor => actor.GameObjectId == targetId)
            is Dalamud.Game.ClientState.Objects.Types.IBattleNpc npc
            && npc.StatusFlags.HasFlag(Dalamud.Game.ClientState.Objects.Enums.StatusFlags.Hostile);
    }
    private bool UseActionDetour(ActionManager* self, ActionType type, uint id, ulong targetId, uint extra,
        ActionManager.UseActionMode mode, uint combo, bool* areaTargeted)
    {
        if (!ShouldBlock(type, id, targetId))
            return useActionHook.Original(self, type, id, targetId, extra, mode, combo, areaTargeted);
        if (areaTargeted != null)
            *areaTargeted = false;
        return false;
    }
    private bool UseLocationDetour(ActionManager* self, ActionType type, uint id, ulong targetId, Vector3* location, uint extra, byte a7)
        => !ShouldBlock(type, id, targetId) && useLocationHook.Original(self, type, id, targetId, location, extra, a7);

    public void Dispose()
    {
        SetHolds(false, false, false);
        StopApproach();
        holdProvider.UnregisterFunc();
        useActionHook.Dispose();
        useLocationHook.Dispose();
    }
}
