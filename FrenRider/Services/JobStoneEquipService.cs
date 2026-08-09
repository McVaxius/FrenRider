using System;
using System.Collections.Generic;
using System.Linq;
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
    private const int MinimumLevel = 30;
    private const ushort EquippedSoulCrystalSlot = 13;
    private const long CheckIntervalMs = 5000;
    private const long ConfirmDelayMs = 250;
    private const long ConfirmTimeoutMs = 5000;

    private long lastCheckMs;
    private PendingEquip? pendingEquip;

    public void Update(CharacterConfig config)
    {
        var now = Environment.TickCount64;
        if (pendingEquip != null)
        {
            if (!config.Enabled || !config.EquipJobStoneForCurrentClass)
            {
                pendingEquip = null;
                return;
            }

            ConfirmEquip(now);
            return;
        }

        if (!config.Enabled || !config.EquipJobStoneForCurrentClass || now - lastCheckMs < CheckIntervalMs)
            return;

        lastCheckMs = now;
        TryBeginEquip(config, now);
    }

    internal static bool ShouldAttempt(JobStonePolicyInput input)
        => input.FrenRiderEnabled
           && input.FeatureEnabled
           && input.IsBaseClass
           && input.Level >= MinimumLevel
           && input.CanChangeEquipment
           && input.HasValidCurrentGearset
           && input.SoulCrystalSlotEmpty;

    internal static JobStoneSlot? SelectFirstMatchingStone(
        IReadOnlySet<uint> eligibleItemIds,
        IEnumerable<JobStoneSlot> slots)
    {
        foreach (var slot in slots)
        {
            if (slot.ItemId != 0 && eligibleItemIds.Contains(slot.ItemId))
                return slot;
        }

        return null;
    }

    private static unsafe bool TryGetCurrentGearset(out RaptureGearsetModule* gearsets, out int gearsetId)
    {
        gearsets = RaptureGearsetModule.Instance();
        gearsetId = gearsets == null ? -1 : gearsets->CurrentGearsetIndex;
        return gearsets != null && gearsetId >= 0 && gearsets->IsValidGearset(gearsetId);
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
               && !condition[ConditionFlag.BoundByDuty]
               && !condition[ConditionFlag.BoundByDuty56]
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

    private static HashSet<uint> GetEligibleStoneIds(ClassJob currentClass)
    {
        var sheet = Plugin.DataManager.GetExcelSheet<ClassJob>();
        if (sheet == null)
            return new HashSet<uint>();

        return sheet
            .Where(job => job.ClassJobParent.RowId == currentClass.RowId && job.ItemSoulCrystal.RowId != 0)
            .Select(job => job.ItemSoulCrystal.RowId)
            .ToHashSet();
    }

    private unsafe void TryBeginEquip(CharacterConfig config, long now)
    {
        try
        {
            var currentClass = Plugin.PlayerState.ClassJob.Value;
            var isBaseClass = currentClass.RowId != 0 && currentClass.ItemSoulCrystal.RowId == 0;
            var hasValidGearset = TryGetCurrentGearset(out _, out var gearsetId);

            var inventory = InventoryManager.Instance();
            var equipped = inventory == null
                ? null
                : inventory->GetInventoryContainer(InventoryType.EquippedItems);
            var equippedStone = equipped != null && equipped->IsLoaded && equipped->Size > EquippedSoulCrystalSlot
                ? equipped->GetInventorySlot(EquippedSoulCrystalSlot)
                : null;
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

            var eligibleItemIds = GetEligibleStoneIds(currentClass);
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

            var match = SelectFirstMatchingStone(eligibleItemIds, slots);
            if (match == null)
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

            pendingEquip = new PendingEquip(match.Value.ItemId, gearsetId, now, now + ConfirmDelayMs);
            Plugin.Log.Information($"[FrenRider][JobStone] Requested armoury slot {match.Value.Slot} item {match.Value.ItemId} for base class {currentClass.Abbreviation}.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[FrenRider][JobStone] Failed to evaluate or request job-stone equip.");
        }
    }

    private unsafe void ConfirmEquip(long now)
    {
        var pending = pendingEquip;
        if (pending == null || now < pending.NextConfirmMs)
            return;

        if (now - pending.StartedMs > ConfirmTimeoutMs)
        {
            Plugin.Log.Warning($"[FrenRider][JobStone] Timed out confirming item {pending.ItemId}; gearset was not updated.");
            pendingEquip = null;
            return;
        }

        if (!CanChangeEquipmentNow())
            return;

        try
        {
            var inventory = InventoryManager.Instance();
            var equipped = inventory == null
                ? null
                : inventory->GetInventoryContainer(InventoryType.EquippedItems);
            var item = equipped != null && equipped->IsLoaded && equipped->Size > EquippedSoulCrystalSlot
                ? equipped->GetInventorySlot(EquippedSoulCrystalSlot)
                : null;
            if (item == null || item->GetBaseItemId() != pending.ItemId)
                return;

            var gearsets = RaptureGearsetModule.Instance();
            if (gearsets == null
                || gearsets->CurrentGearsetIndex != pending.GearsetId
                || !gearsets->IsValidGearset(pending.GearsetId))
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

    private sealed record PendingEquip(uint ItemId, int GearsetId, long StartedMs, long NextConfirmMs);
}
