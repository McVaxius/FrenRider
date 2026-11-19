using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FrenRider.Models;
using Lumina.Excel.Sheets;

namespace FrenRider.Services;

internal readonly record struct JobStonePolicyInput(
    bool FrenRiderEnabled,
    bool FeatureEnabled,
    bool IsBaseClass,
    int Level,
    bool CanChangeEquipment,
    bool HasValidCurrentGearset,
    bool SoulCrystalSlotEmpty);

internal readonly record struct JobStoneSlot(int Slot, uint ItemId);

internal sealed class JobStoneEquipService
{
    internal const uint ArcanistClassId = 26;
    private const uint SummonerJobId = 27;
    private const uint ScholarJobId = 28;
    private const int MinimumLevel = 30;
    private const ushort EquippedSoulCrystalSlot = 13;
    private const long CheckIntervalMs = 5000;
    private const long ConfirmDelayMs = 250;
    private const long ConfirmTimeoutMs = 5000;

    private readonly Plugin plugin;
    private long lastCheckMs;
    private PendingEquip? pendingEquip;

    public JobStoneEquipService(Plugin plugin) => this.plugin = plugin;

    public void Update(bool movementHeld)
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        if (!CanRun(config)) { SuspendAutomatic(); return; }
        if (movementHeld) return;

        var now = Environment.TickCount64;
        if (pendingEquip != null)
        {
            ConfirmEquip(config, now);
            return;
        }

        if (now - lastCheckMs < CheckIntervalMs)
            return;

        lastCheckMs = now;
        TryBeginEquip(config, now);
    }

    internal void SuspendAutomatic() => pendingEquip = null;

    private static bool CanRun(CharacterConfig config)
        => config.Enabled && config.EquipJobStoneForCurrentClass
           && !Plugin.IsBoundByDuty();

    private bool CanWrite(CharacterConfig config, ulong characterId, string provider)
        => plugin.AutorotIpcService.ReadJobStoneProvider() == provider
           && characterId != 0 && Plugin.PlayerState.ContentId == characterId
           && ReferenceEquals(config, plugin.ConfigManager.GetActiveConfig()) && CanRun(config);

    internal static bool ShouldAttempt(JobStonePolicyInput input)
        => input.FrenRiderEnabled
           && input.FeatureEnabled
           && input.IsBaseClass
           && input.Level >= MinimumLevel
           && input.CanChangeEquipment
           && input.HasValidCurrentGearset
           && input.SoulCrystalSlotEmpty;

    internal static JobStoneSlot? SelectMatchingStone(
        IReadOnlySet<uint> eligibleItemIds,
        IEnumerable<JobStoneSlot> slots,
        uint preferredItemId = 0)
    {
        JobStoneSlot? firstMatch = null;
        foreach (var slot in slots)
        {
            if (slot.ItemId == 0 || !eligibleItemIds.Contains(slot.ItemId)) continue;
            if (preferredItemId == 0 || slot.ItemId == preferredItemId)
                return slot;
            firstMatch ??= slot;
        }

        return firstMatch;
    }

    private static unsafe bool TryGetCurrentGearset(out RaptureGearsetModule* gearsets, out int gearsetId)
    {
        gearsets = RaptureGearsetModule.Instance();
        gearsetId = gearsets == null ? -1 : gearsets->CurrentGearsetIndex;
        return gearsets != null && gearsetId >= 0 && gearsets->IsValidGearset(gearsetId);
    }

    private static unsafe InventoryItem* ReadInventorySlot(InventoryManager* inventory, InventoryType type, int slot)
    {
        var container = inventory == null ? null : inventory->GetInventoryContainer(type);
        return container != null && container->IsLoaded && slot >= 0 && slot < container->Size
            ? container->GetInventorySlot(slot)
            : null;
    }

    private static bool CanChangeEquipmentNow()
    {
        if (!Plugin.ClientState.IsLoggedIn || Plugin.ObjectTable.LocalPlayer == null || !GameHelpers.IsPlayerAlive())
            return false;

        if (GameHelpers.TryGetMountedOrRidingOrMountingBlocker(out _))
            return false;

        var condition = Plugin.Condition;
        return !condition[ConditionFlag.InCombat]
               && !condition[ConditionFlag.Casting]
               && !Plugin.IsBoundByDuty()
               && !condition[ConditionFlag.WatchingCutscene]
               && !condition[ConditionFlag.OccupiedInCutSceneEvent]
               && !condition[ConditionFlag.Occupied]
               && !condition[ConditionFlag.Occupied30]
               && !condition[ConditionFlag.Occupied33]
               && !condition[ConditionFlag.Occupied38]
               && !condition[ConditionFlag.Occupied39]
               && !condition[ConditionFlag.OccupiedInEvent]
               && !condition[ConditionFlag.OccupiedInQuestEvent]
               && !condition[ConditionFlag.OccupiedSummoningBell]
               && !condition[ConditionFlag.BetweenAreas]
               && !condition[ConditionFlag.BetweenAreas51]
               && !condition[ConditionFlag.Unconscious]
               && !GameHelpers.IsAddonVisible("ContentsFinderConfirm")
               && !GameHelpers.IsAddonVisible("SelectYesno");
    }

    private static HashSet<uint> GetEligibleStoneIds(ClassJob currentClass, ArcanistJobPreference preference,
        out uint preferredItemId)
    {
        preferredItemId = 0;
        var sheet = Plugin.DataManager.GetExcelSheet<ClassJob>();
        if (sheet == null)
            return new HashSet<uint>();

        var preferredJobId = preference == ArcanistJobPreference.Summoner ? SummonerJobId : ScholarJobId;
        var eligibleItemIds = new HashSet<uint>();
        foreach (var job in sheet)
        {
            var itemId = job.ItemSoulCrystal.RowId;
            if (job.ClassJobParent.RowId != currentClass.RowId || itemId == 0) continue;
            eligibleItemIds.Add(itemId);
            if (currentClass.RowId == ArcanistClassId && job.RowId == preferredJobId)
                preferredItemId = itemId;
        }

        return eligibleItemIds;
    }

    private unsafe void TryBeginEquip(CharacterConfig config, long now)
    {
        try
        {
            var provider = plugin.AutorotIpcService.ReadJobStoneProvider();
            if (provider == null)
                return;

            var characterId = Plugin.PlayerState.ContentId;
            if (characterId == 0) return;
            var currentClass = Plugin.PlayerState.ClassJob.Value;
            var isBaseClass = currentClass.RowId != 0 && currentClass.ItemSoulCrystal.RowId == 0;
            var hasValidGearset = TryGetCurrentGearset(out _, out var gearsetId);

            var inventory = InventoryManager.Instance();
            var equippedStone = ReadInventorySlot(inventory, InventoryType.EquippedItems, EquippedSoulCrystalSlot);
            var soulCrystalSlotEmpty = equippedStone != null && equippedStone->GetBaseItemId() == 0;

            var policy = new JobStonePolicyInput(
                config.Enabled,
                config.EquipJobStoneForCurrentClass,
                isBaseClass,
                Plugin.PlayerState.Level,
                CanChangeEquipmentNow(),
                hasValidGearset,
                soulCrystalSlotEmpty);
            if (!ShouldAttempt(policy))
                return;

            var eligibleItemIds = GetEligibleStoneIds(currentClass, config.PreferredArcanistJob, out var preferredItemId);
            if (eligibleItemIds.Count == 0 || inventory == null)
                return;

            var armoury = inventory->GetInventoryContainer(InventoryType.ArmorySoulCrystal);
            if (armoury == null || !armoury->IsLoaded)
                return;

            var slots = new List<JobStoneSlot>(armoury->Size);
            for (var slot = 0; slot < armoury->Size; slot++)
            {
                var item = armoury->GetInventorySlot(slot);
                slots.Add(new JobStoneSlot(slot, item == null ? 0 : item->GetBaseItemId()));
            }

            var match = SelectMatchingStone(eligibleItemIds, slots, preferredItemId);
            if (match == null)
                return;

            if (!CanWrite(config, characterId, provider) || !CanChangeEquipmentNow()
                || pendingEquip != null
                || Plugin.PlayerState.ClassJob.RowId != currentClass.RowId || Plugin.PlayerState.Level < MinimumLevel
                || !TryGetCurrentGearset(out _, out var currentGearsetId) || currentGearsetId != gearsetId
                || InventoryManager.Instance() != inventory)
                return;
            var source = ReadInventorySlot(inventory, InventoryType.ArmorySoulCrystal, match.Value.Slot);
            equippedStone = ReadInventorySlot(inventory, InventoryType.EquippedItems, EquippedSoulCrystalSlot);
            if (source == null || source->GetBaseItemId() != match.Value.ItemId
                || equippedStone == null || equippedStone->GetBaseItemId() != 0)
                return;
            var moveResult = inventory->MoveItemSlot(
                InventoryType.ArmorySoulCrystal,
                (ushort)match.Value.Slot,
                InventoryType.EquippedItems,
                EquippedSoulCrystalSlot);
            if (moveResult != 0)
            {
                Plugin.Log.Debug($"[FrenRider][JobStone] Equip request for item {match.Value.ItemId} returned {moveResult}.");
                return;
            }

            pendingEquip = new PendingEquip(match.Value.ItemId, gearsetId, characterId, provider, now, now + ConfirmDelayMs);
            Plugin.Log.Information($"[FrenRider][JobStone] Requested armoury slot {match.Value.Slot} item {match.Value.ItemId} for base class {currentClass.Abbreviation}.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[FrenRider][JobStone] Failed to evaluate or request job-stone equip.");
        }
    }

    private unsafe void ConfirmEquip(CharacterConfig config, long now)
    {
        var pending = pendingEquip;
        if (pending == null)
            return;
        if (pending.CharacterId != Plugin.PlayerState.ContentId)
        {
            SuspendAutomatic();
            return;
        }
        if (now < pending.NextConfirmMs)
            return;

        if (now - pending.StartedMs > ConfirmTimeoutMs)
        {
            Plugin.Log.Warning($"[FrenRider][JobStone] Timed out confirming item {pending.ItemId}; gearset was not updated.");
            pendingEquip = null;
            return;
        }

        try
        {
            var canWrite = CanWrite(config, pending.CharacterId, pending.Provider);
            if (!ReferenceEquals(pending, pendingEquip))
                return;
            if (!canWrite) { SuspendAutomatic(); return; }
            if (!CanChangeEquipmentNow())
                return;

            var item = ReadInventorySlot(InventoryManager.Instance(), InventoryType.EquippedItems, EquippedSoulCrystalSlot);
            if (item == null || item->GetBaseItemId() != pending.ItemId)
                return;

            if (!TryGetCurrentGearset(out var gearsets, out var gearsetId) || gearsetId != pending.GearsetId)
            {
                Plugin.Log.Warning("[FrenRider][JobStone] Job stone equipped, but the active gearset changed before confirmation; no gearset was updated.");
                pendingEquip = null;
                return;
            }

            pendingEquip = null;
            var updateResult = gearsets->UpdateGearset(pending.GearsetId);
            if (updateResult == 0)
                Plugin.Log.Information($"[FrenRider][JobStone] Confirmed item {pending.ItemId} and updated gearset {pending.GearsetId + 1} once.");
            else
                Plugin.Log.Warning($"[FrenRider][JobStone] Confirmed item {pending.ItemId}, but gearset update returned {updateResult}.");
        }
        catch (Exception ex)
        {
            pendingEquip = null;
            Plugin.Log.Warning(ex, "[FrenRider][JobStone] Failed while confirming the equipped job stone; gearset update stopped.");
        }
    }

    private sealed record PendingEquip(uint ItemId, int GearsetId, ulong CharacterId, string Provider, long StartedMs, long NextConfirmMs);
}
