using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FrenRider.Models;
using Lumina.Excel.Sheets;

namespace FrenRider.Services;

internal readonly record struct BeastRosterEntry(uint Id, string Name, ushort Model);
internal readonly record struct BeastCaptureTarget(
    ulong Id, bool IsEnemy, bool IsTargetable, uint CurrentHp, uint MaxHp, int Level, IReadOnlyList<uint> CandidateBeastIds);
internal readonly record struct BeastCaptureFrame(
    string AccountId, string CharacterKey, CharacterConfig Config, bool IsBeastmaster,
    bool OwnershipLoaded, bool InCombat, bool AreaChanged, bool CanAct, int EffectiveLevel);
internal readonly record struct BeastCaptureAction(bool Available, bool RecastActive, float RecastSeconds);

// Keep the native boundary replaceable for offline lifecycle tests.
internal interface IBeastCaptureRuntime
{
    IReadOnlyList<BeastRosterEntry> Roster { get; }
    BeastCaptureFrame ReadFrame();
    BeastCaptureTarget? ReadTarget();
    IEnumerable<BeastCaptureTarget> ReadNearbyTargets();
    bool? IsOwned(uint beastId);
    IReadOnlySet<uint>? ReadUnlockedBeasts();
    void SaveUnlockedBeasts(IReadOnlySet<uint> beastIds);
    BeastCaptureAction ReadAction(ulong targetId);
    bool TargetBeast(ulong targetId);
    bool UseCapture(ulong targetId);
}

internal sealed class BeastCaptureService
{
    internal const uint CaptureActionId = 44880;
    private readonly IBeastCaptureRuntime runtime;
    private string accountId = "";
    private string characterKey = "";
    private bool wasInCombat;
    private bool wasOwnershipReady;
    private bool refreshPending = true;
    private long nextAttemptMs;

    internal BeastCaptureService(Plugin plugin) : this(new NativeBeastCaptureRuntime(plugin)) { }
    internal BeastCaptureService(IBeastCaptureRuntime runtime) => this.runtime = runtime;
    internal IReadOnlyList<BeastRosterEntry> Roster => runtime.Roster;

    // Loading may bypass the normal framework loop. Keep the next ownership refresh armed.
    internal void Suspend()
    {
        refreshPending = true;
        wasOwnershipReady = false;
    }

    internal void Update() => Update(Environment.TickCount64);

    internal void Update(long now)
    {
        try
        {
            var frame = runtime.ReadFrame();
            if (frame.AccountId != accountId || frame.CharacterKey != characterKey)
            {
                accountId = frame.AccountId;
                characterKey = frame.CharacterKey;
                wasInCombat = false;
                Suspend();
            }

            var ownershipReady = frame.IsBeastmaster && frame.OwnershipLoaded
                && !string.IsNullOrEmpty(accountId) && !string.IsNullOrEmpty(characterKey);
            refreshPending |= frame.AreaChanged || (wasInCombat && !frame.InCombat)
                || (ownershipReady && !wasOwnershipReady);
            wasInCombat = frame.InCombat;
            wasOwnershipReady = ownershipReady;

            // Native confirmation is independent of the automatic Capture checkbox and FR enable state.
            if (ownershipReady && refreshPending)
            {
                var unlocked = runtime.ReadUnlockedBeasts();
                if (unlocked == null)
                {
                    Suspend();
                    return;
                }

                runtime.SaveUnlockedBeasts(unlocked);
                refreshPending = false;
            }

            if (!ownershipReady || !frame.Config.Enabled || !frame.Config.TryToCatchBeasts
                || !frame.CanAct || now < nextAttemptMs)
                return;

            var target = runtime.ReadTarget();
            if (target is { } current && TryCapture(current, frame, now))
                return;

            foreach (var beast in runtime.ReadNearbyTargets())
            {
                if (beast.Id != target?.Id && TryCapture(beast, frame, now))
                    return;
            }
        }
        catch
        {
            // Unreadable native state must suspend automation quietly.
            Suspend();
        }
    }

    private bool TryCapture(BeastCaptureTarget beast, BeastCaptureFrame frame, long now)
    {
        if (!IsEligible(beast, frame.EffectiveLevel, frame.Config)
            || !beast.CandidateBeastIds.Any(id => runtime.IsOwned(id) == false))
            return false;

        var action = runtime.ReadAction(beast.Id);
        if (!action.Available || action.RecastActive
            || !float.IsFinite(action.RecastSeconds) || action.RecastSeconds <= 0
            || !runtime.TargetBeast(beast.Id))
            return false;

        // Reserve the interval before the call: even a rejected cast consumes this attempt.
        // Neither target changes nor enable/job/character transitions reset it.
        nextAttemptMs = now + (long)Math.Ceiling(action.RecastSeconds * 1000d);
        runtime.UseCapture(beast.Id);
        return true;
    }

    internal static bool IsEligible(BeastCaptureTarget target, int effectiveLevel, CharacterConfig config)
    {
        if (target.Id is 0 or 0xE0000000 || !target.IsEnemy || !target.IsTargetable
            || target.CurrentHp == 0 || target.MaxHp == 0 || target.CurrentHp > target.MaxHp
            || effectiveLevel <= 0 || target.Level <= 0 || target.Level > effectiveLevel || target.CandidateBeastIds.Count == 0)
            return false;

        var threshold = effectiveLevel - target.Level > 5 ? config.CaptureHpFarBelow : config.CaptureHpNearOrEqual;
        return (ulong)target.CurrentHp * 100 <= (ulong)target.MaxHp * (uint)threshold;
    }

    internal static Dictionary<ushort, IReadOnlyList<uint>> BuildModelLookup(IEnumerable<BeastRosterEntry> roster)
        => roster.Where(entry => entry.Id != 0 && entry.Model != 0)
            .GroupBy(entry => entry.Model)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<uint>)group.Select(entry => entry.Id).Distinct().ToArray());

    internal static IReadOnlyList<uint> ResolveBeasts(byte captureType, ushort model,
        IReadOnlyDictionary<ushort, IReadOnlyList<uint>> lookup)
        => captureType == 5 && lookup.TryGetValue(model, out var ids) ? ids : Array.Empty<uint>();
}

internal sealed class NativeBeastCaptureRuntime : IBeastCaptureRuntime
{
    private const uint BeastmasterJobId = 43;
    private readonly Plugin plugin;
    private List<BeastRosterEntry>? roster;
    private Dictionary<ushort, IReadOnlyList<uint>> modelLookup = new();

    internal NativeBeastCaptureRuntime(Plugin plugin) => this.plugin = plugin;
    public IReadOnlyList<BeastRosterEntry> Roster
    {
        get
        {
            EnsureRoster();
            return roster ?? (IReadOnlyList<BeastRosterEntry>)Array.Empty<BeastRosterEntry>();
        }
    }

    private void EnsureRoster()
    {
        if (roster != null)
            return;

        var beasts = Plugin.DataManager.GetExcelSheet<XBMPet>();
        var pets = Plugin.DataManager.GetExcelSheet<Pet>();
        var mirages = Plugin.DataManager.GetExcelSheet<PetMirage>();
        var models = Plugin.DataManager.GetExcelSheet<ModelChara>();
        if (beasts == null || pets == null || mirages == null || models == null)
            return;

        var entries = new List<BeastRosterEntry>();
        foreach (var beast in beasts)
        {
            if (beast.RowId == 0 || beast.Unknown4 <= 0 || !pets.TryGetRow((uint)beast.Unknown4, out var pet))
                continue;

            // API15 names these links Unknown4 (Pet) and Unknown8 (primary PetMirage).
            var model = pet.Unknown8 != 0 && mirages.TryGetRow(pet.Unknown8, out var mirage)
                ? models.GetRowOrDefault(mirage.ModelChara.RowId) : null;
            entries.Add(new BeastRosterEntry(beast.RowId, pet.Name.ExtractText(), model?.Model ?? 0));
        }

        modelLookup = BeastCaptureService.BuildModelLookup(entries);
        roster = entries.OrderBy(entry => entry.Name, StringComparer.CurrentCulture).ThenBy(entry => entry.Id).ToList();
    }

    public BeastCaptureFrame ReadFrame()
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        var hasCharacter = Plugin.ClientState.IsLoggedIn && player != null && plugin.ConfigManager.TryGetLocalActiveConfig(out _);
        var beastmaster = hasCharacter && player!.ClassJob.RowId == BeastmasterJobId;
        var condition = Plugin.Condition;
        var loading = condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51];
        var canAct = hasCharacter && player!.CurrentHp > 0 && !player.IsDead && !player.IsCasting
            && !loading && !condition[ConditionFlag.Unconscious] && !condition[ConditionFlag.Casting]
            && !condition[ConditionFlag.WatchingCutscene] && !condition[ConditionFlag.WatchingCutscene78]
            && !condition[ConditionFlag.OccupiedInCutSceneEvent] && !condition[ConditionFlag.OccupiedInQuestEvent]
            && !GameHelpers.IsMountedOrRidingOrMounting()
            && !plugin.AutomationService.IsUtilityGateActive
            && !plugin.CombatService.IsQuestionableSoloAuthorityActive
            && !plugin.AdsIntegrationService.IsSoloCombatHeld
            && !plugin.CoppeliaPowerlevelLeaseService.IsLeaseActive
            && !plugin.AdsHyperFocusLeaseService.IsLeaseActive;
        return new BeastCaptureFrame(
            hasCharacter ? plugin.ConfigManager.CurrentAccountId : "",
            hasCharacter ? plugin.ConfigManager.ActiveCharacterKey : "",
            plugin.ConfigManager.GetActiveConfig(), beastmaster,
            beastmaster && !loading && Plugin.UnlockState.IsXBMPetListLoaded,
            condition[ConditionFlag.InCombat], plugin.ZoneService.ZoneChanged, canAct, Plugin.PlayerState.EffectiveLevel);
    }

    public BeastCaptureTarget? ReadTarget()
        => Plugin.TargetManager.Target is IBattleNpc target ? ReadTarget(target) : null;

    public IEnumerable<BeastCaptureTarget> ReadNearbyTargets()
    {
        var currentTargetId = Plugin.TargetManager.Target?.GameObjectId;
        foreach (var target in Plugin.ObjectTable.OfType<IBattleNpc>()
            .Where(target => target.GameObjectId != currentTargetId && target.CurrentDistance < 10
                && target.BattleNpcKind == BattleNpcSubKind.Combatant
                && target.IsTargetable && target.CurrentHp > 0)
            .OrderBy(target => target.CurrentDistance))
        {
            if (ReadTarget(target) is { } beast)
                yield return beast;
        }
    }

    private BeastCaptureTarget? ReadTarget(IBattleNpc target)
    {
        if (target.CurrentDistance >= 10)
            return null;

        EnsureRoster();
        var npc = Plugin.DataManager.GetExcelSheet<BNpcBase>()?.GetRowOrDefault(target.BaseId);
        var model = npc is { Unknown10: 5 }
            ? Plugin.DataManager.GetExcelSheet<ModelChara>()?.GetRowOrDefault(npc.Value.ModelChara.RowId) : null;
        var beastIds = model is { } value
            ? BeastCaptureService.ResolveBeasts(npc!.Value.Unknown10, value.Model, modelLookup) : Array.Empty<uint>();
        return new BeastCaptureTarget(target.GameObjectId,
            target.BattleNpcKind == BattleNpcSubKind.Combatant,
            target.IsTargetable, target.CurrentHp, target.MaxHp, target.Level, beastIds);
    }

    public bool? IsOwned(uint beastId)
    {
        if (Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId != BeastmasterJobId || !Plugin.UnlockState.IsXBMPetListLoaded)
            return null;

        var row = Plugin.DataManager.GetExcelSheet<XBMPet>()?.GetRowOrDefault(beastId);
        return row is { } beast ? Plugin.UnlockState.IsXBMPetUnlocked(beast) : null;
    }

    public IReadOnlySet<uint>? ReadUnlockedBeasts()
    {
        if (Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId != BeastmasterJobId || !Plugin.UnlockState.IsXBMPetListLoaded)
            return null;

        var beasts = Plugin.DataManager.GetExcelSheet<XBMPet>();
        if (beasts == null)
            return null;

        // Save the full confirmed list, independently of whether a beast's model can be resolved for Capture.
        var unlocked = new HashSet<uint>();
        foreach (var beast in beasts)
        {
            if (beast.RowId != 0 && Plugin.UnlockState.IsXBMPetUnlocked(beast))
                unlocked.Add(beast.RowId);
        }

        return unlocked;
    }

    public void SaveUnlockedBeasts(IReadOnlySet<uint> beastIds) => plugin.ConfigManager.SaveUnlockedBeasts(beastIds);

    public unsafe BeastCaptureAction ReadAction(ulong targetId)
    {
        var am = ActionManager.Instance();
        return am == null ? default : new BeastCaptureAction(
            am->GetActionStatus(ActionType.Action, BeastCaptureService.CaptureActionId, targetId) == 0,
            am->IsRecastTimerActive(ActionType.Action, BeastCaptureService.CaptureActionId),
            ActionManager.GetAdjustedRecastTime(ActionType.Action, BeastCaptureService.CaptureActionId) / 1000f);
    }

    public bool TargetBeast(ulong targetId)
    {
        var current = Plugin.TargetManager.Target;
        var target = current?.GameObjectId == targetId ? current : Plugin.ObjectTable.SearchById(targetId);
        if (target is not IBattleNpc npc || npc.CurrentDistance >= 10
            || npc.BattleNpcKind != BattleNpcSubKind.Combatant || !npc.IsTargetable || npc.CurrentHp == 0)
            return false;

        if (current?.GameObjectId != targetId)
            Plugin.TargetManager.Target = target;
        return Plugin.TargetManager.Target?.GameObjectId == targetId;
    }

    public bool UseCapture(ulong targetId)
    {
        var returned = GameHelpers.TryUseActionLocation(ActionType.Action, BeastCaptureService.CaptureActionId,
            targetId, itemLocation: 0, quiet: true);
        Plugin.Log.Information("[FrenRider][Capture] Attempted Capture (44880); returned={Returned}", returned);
        return returned;
    }
}
