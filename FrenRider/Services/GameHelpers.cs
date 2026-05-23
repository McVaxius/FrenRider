using System;
using System.Numerics;
using System.Text;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using DalamudObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace FrenRider.Services;

/// <summary>
/// Static unsafe helpers for game state queries: inventory, status effects, item usage, companion.
/// </summary>
public static class GameHelpers
{
    private const float AetheryteSanctuaryFallbackDistance = 50.0f;
    private const float AetheryteSanctuaryFallbackDistanceSquared = AetheryteSanctuaryFallbackDistance * AetheryteSanctuaryFallbackDistance;

    // Well Fed status ID
    public const uint WellFedStatusId = 48;

    // Gysahl Greens item ID
    public const uint GysahlGreensItemId = 4868;

    // Known food items in order of priority (least to most preferred) — matches Lua food_list
    public static readonly (uint Id, string Name)[] FoodList =
    {
        (4745,  "Orange Juice"),
        (12855, "Grilled Sweetfish"),
        (19816, "Popoto Soba"),
        (19822, "Grilled Turban"),
        (39872, "Baked Eggplant"),
        (44182, "Pineapple Orange Jelly"),
        (44178, "Moqecka"),
        (46003, "Mate Cookie"),
    };

    /// <summary>
    /// Get the count of an item in the player's inventory (NQ + HQ).
    /// </summary>
    public static unsafe int GetInventoryItemCount(uint itemId)
    {
        try
        {
            var im = InventoryManager.Instance();
            if (im == null) return 0;
            // NQ count + HQ count (isHq = true adds 1000000 offset internally)
            return GetInventoryItemCount(itemId, false) + GetInventoryItemCount(itemId, true);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"GetInventoryItemCount({itemId}) failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Get the exact NQ or HQ count of an item in the player's inventory.
    /// </summary>
    public static unsafe int GetInventoryItemCount(uint itemId, bool highQuality)
    {
        try
        {
            var im = InventoryManager.Instance();
            if (im == null) return 0;
            return im->GetInventoryItemCount(itemId, highQuality);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"GetInventoryItemCount({itemId}, HQ={highQuality}) failed: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Check if the player has a specific status effect. Returns remaining time in seconds (0 if not found).
    /// </summary>
    public static unsafe float GetStatusTimeRemaining(uint statusId)
    {
        try
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null) return 0f;

            var chara = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)player.Address;
            if (chara == null) return 0f;

            var sm = chara->GetStatusManager();
            if (sm == null) return 0f;

            for (var i = 0; i < sm->NumValidStatuses; i++)
            {
                var status = sm->Status[i];
                if (status.StatusId == statusId)
                    return status.RemainingTime;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"GetStatusTimeRemaining({statusId}) failed: {ex.Message}");
        }
        return 0f;
    }

    /// <summary>
    /// Use an item from inventory by item ID.
    /// Mirrors AutoDuty's approach: uses extraParam 65535 and checks for casting/occupied state.
    /// </summary>
    public static unsafe bool UseItem(uint itemId)
        => UseItem(itemId, highQuality: false);

    /// <summary>
    /// Use an item from inventory by item ID, optionally as HQ.
    /// HQ item actions use the base item row with the HQ action ID offset.
    /// </summary>
    public static unsafe bool UseItem(uint itemId, bool highQuality)
    {
        try
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null)
            {
                Plugin.Log.Warning($"UseItem({itemId}): LocalPlayer is null");
                return false;
            }

            // Check if player is casting
            if (player.IsCasting)
            {
                Plugin.Log.Debug($"UseItem({itemId}): Player is casting, skipping");
                return false;
            }

            // Check if player is occupied (in cutscene, etc)
            if (Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInQuestEvent] ||
                Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInCutSceneEvent] ||
                Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Occupied33] ||
                Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Occupied39])
            {
                Plugin.Log.Debug($"UseItem({itemId}): Player is occupied, skipping");
                return false;
            }

            var am = ActionManager.Instance();
            if (am == null)
            {
                Plugin.Log.Warning($"UseItem({itemId}): ActionManager is null");
                return false;
            }

            var actionItemId = highQuality ? itemId + 1_000_000u : itemId;

            // Check if the action is ready
            var status = am->GetActionStatus(ActionType.Item, actionItemId);
            if (status != 0)
            {
                Plugin.Log.Debug($"UseItem({itemId}, HQ={highQuality}): ActionStatus={status}, not ready");
                return false;
            }

            // Use item with extraParam 65535 (required for item usage)
            var result = am->UseAction(ActionType.Item, actionItemId, extraParam: 65535);
            Plugin.Log.Information($"UseItem({itemId}, HQ={highQuality}): UseAction result={result}");
            return result;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"UseItem({itemId}, HQ={highQuality}) failed: {ex.Message}");
            return false;
        }
    }

    public static bool CanAutoDiscardNow(out string reason)
    {
        if (!IsMountedOrRiding())
        {
            reason = "not mounted or riding";
            return false;
        }

        if (Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat])
        {
            reason = "in combat";
            return false;
        }

        if (Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas]
            || Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas51])
        {
            reason = "between areas";
            return false;
        }

        if (Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInQuestEvent]
            || Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInCutSceneEvent]
            || Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Occupied33]
            || Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Occupied39]
            || Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.WatchingCutscene])
        {
            reason = "occupied or in cutscene";
            return false;
        }

        reason = "ready";
        return true;
    }

    public static bool IsMountedOrRiding()
    {
        return Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Mounted]
            || Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.RidingPillion];
    }

    public static bool IsMountedOrRidingOrMounting()
    {
        return IsMountedOrRiding()
            || Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Mounting71];
    }

    public static bool TryGetMountedOrRidingOrMountingBlocker(out string reason)
    {
        if (Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Mounting71])
        {
            reason = "mounting";
            return true;
        }

        if (Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.RidingPillion])
        {
            reason = "riding pillion";
            return true;
        }

        if (Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Mounted])
        {
            reason = "mounted";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    public static unsafe bool CanUseMountActionNow(out string reason)
    {
        if (Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Mounted]
            || Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.RidingPillion])
        {
            reason = "already mounted";
            return false;
        }

        if (Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Mounting71])
        {
            reason = "already mounting";
            return false;
        }

        if (Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat])
        {
            reason = "in combat";
            return false;
        }

        if (Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas]
            || Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas51])
        {
            reason = "between areas";
            return false;
        }

        try
        {
            var am = ActionManager.Instance();
            if (am == null)
            {
                reason = "ActionManager unavailable";
                return false;
            }

            var status = am->GetActionStatus(ActionType.GeneralAction, 9);
            if (status != 0)
            {
                reason = $"mount action unavailable (status={status})";
                return false;
            }
        }
        catch (Exception ex)
        {
            reason = $"mount action check failed: {ex.Message}";
            return false;
        }

        reason = "ready";
        return true;
    }

    /// <summary>
    /// Uses the API15 ClientStructs UseActionLocation wrapper. FrenRider does not detour it today;
    /// this is just the low-level call surface.
    /// </summary>
    public static unsafe bool TryUseActionLocation(
        ActionType actionType,
        uint actionId,
        ulong targetId = 0xE0000000,
        Vector3? targetPosition = null,
        uint itemLocation = 0xFFFF)
    {
        try
        {
            var actionManager = ActionManager.Instance();
            if (actionManager == null)
            {
                Plugin.Log.Warning($"TryUseActionLocation({actionType}, {actionId}): ActionManager is null");
                return false;
            }

            if (targetPosition.HasValue)
            {
                var position = targetPosition.Value;
                return actionManager->UseActionLocation(actionType, actionId, targetId, &position, itemLocation);
            }

            return actionManager->UseActionLocation(actionType, actionId, targetId, null, itemLocation);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"TryUseActionLocation({actionType}, {actionId}) failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Get companion (chocobo buddy) time remaining in seconds.
    /// Returns 0 if no companion is active or if in sanctuary.
    /// </summary>
    public static unsafe float GetBuddyTimeRemaining()
    {
        try
        {
            var uiState = UIState.Instance();
            if (uiState == null) return 0f;
            return uiState->Buddy.CompanionInfo.TimeLeft;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"GetBuddyTimeRemaining() failed: {ex.Message}");
            return 0f;
        }
    }

    /// <summary>
    /// Check if the player is in a sanctuary (rest area where you can't summon companion).
    /// Uses ActionManager to check if Mount general action is available — if not, we're in sanctuary.
    /// General Action ID 9 = Mount.
    /// Also allows ADS NPC no-inn repair near Aetheryte/Aethernet objects.
    /// </summary>
    public static unsafe bool IsInSanctuary()
    {
        try
        {
            var am = ActionManager.Instance();
            if (am == null) return true; // Assume sanctuary if we can't check

            // If mount action is available (status 0), we're NOT in sanctuary
            var status = am->GetActionStatus(ActionType.GeneralAction, 9);
            return status != 0 || IsNearAetheryteOrAethernet();
        }
        catch
        {
            return true;
        }
    }

    private static bool IsNearAetheryteOrAethernet()
    {
        try
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null) return false;

            var playerPosition = player.Position;
            foreach (var obj in Plugin.ObjectTable)
            {
                if (obj == null || !IsAetheryteOrAethernet(obj))
                    continue;

                if (Vector3.DistanceSquared(playerPosition, obj.Position) <= AetheryteSanctuaryFallbackDistanceSquared)
                    return true;
            }
        }
        catch
        {
            // Keep old sanctuary heuristic behavior if object-table proximity cannot be read.
        }

        return false;
    }

    private static bool IsAetheryteOrAethernet(IGameObject obj)
    {
        if (obj.ObjectKind == DalamudObjectKind.Aetheryte)
            return true;

        var name = obj.Name.TextValue;
        return !string.IsNullOrEmpty(name) &&
               (name.Contains("Aetheryte", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Aethernet", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Look up a food item ID by name using Lumina game data.
    /// Returns 0 if not found.
    /// </summary>
    public static uint LookupFoodItemId(string foodName)
        => LookupFoodItem(foodName).Id;

    /// <summary>
    /// Look up a food item by exact name using Lumina meal rows.
    /// </summary>
    public static (uint Id, string Name) LookupFoodItem(string foodName)
    {
        if (string.IsNullOrWhiteSpace(foodName)) return (0, "");

        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            if (sheet == null) return (0, "");

            var trimmedName = foodName.Trim();
            foreach (var row in sheet)
            {
                if (row.ItemUICategory.RowId != 46) continue;

                var name = row.Name.ToString();
                if (!string.IsNullOrEmpty(name) && name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase))
                    return (row.RowId, name);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"LookupFoodItemId(\"{foodName}\") failed: {ex.Message}");
        }
        return (0, "");
    }

    /// <summary>
    /// Look up any item display name by item ID.
    /// </summary>
    public static string LookupItemName(uint itemId)
    {
        if (itemId == 0) return "";

        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            if (sheet == null) return "";

            if (sheet.TryGetRow(itemId, out var item))
                return item.Name.ToString();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"LookupItemName({itemId}) failed: {ex.Message}");
        }

        return "";
    }

    /// <summary>
    /// Search inventory for the best available food from the food list.
    /// Returns item ID, name, exact quality, and count, or zero values if none found.
    /// </summary>
    public static (uint Id, string Name, bool HighQuality, int Count) FindBestAvailableFood()
    {
        // Search from end (highest priority) to start
        for (var i = FoodList.Length - 1; i >= 0; i--)
        {
            var nqCount = GetInventoryItemCount(FoodList[i].Id, highQuality: false);
            if (nqCount > 0)
                return (FoodList[i].Id, FoodList[i].Name, false, nqCount);

            var hqCount = GetInventoryItemCount(FoodList[i].Id, highQuality: true);
            if (hqCount > 0)
                return (FoodList[i].Id, FoodList[i].Name, true, hqCount);
        }
        return (0, "", false, 0);
    }

    /// <summary>
    /// Check if the player is alive (HP > 0).
    /// </summary>
    public static bool IsPlayerAlive()
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null) return false;
        return player.CurrentHp > 0;
    }

    /// <summary>
    /// Check if any equipped gear needs repair (below specified condition percentage).
    /// </summary>
    public static unsafe bool NeedsRepair(int conditionPercent = 0)
    {
        try
        {
            var thresholdPercent = Math.Clamp(conditionPercent, 0, 100);
            var thresholdRaw = (uint)(thresholdPercent * 300);

            var im = InventoryManager.Instance();
            if (im == null) return false;

            // Check equipped gear slots
            var equippedContainer = im->GetInventoryContainer(InventoryType.EquippedItems);
            if (equippedContainer == null) return false;

            for (var i = 0; i < equippedContainer->Size; i++)
            {
                var item = equippedContainer->GetInventorySlot(i);
                if (item == null || item->ItemId == 0) continue;

                // InventoryItem.Condition is scaled 0..30000, where 30000 is 100%.
                if (item->Condition < thresholdRaw)
                    return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"NeedsRepair({conditionPercent}) failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Click Yes on SelectYesno dialog if visible.
    /// Uses AtkUnitBase.FireCallback with proper AtkValue array.
    /// </summary>
    public static unsafe bool ClickYesIfVisible()
    {
        try
        {
            nint addonPtr = Plugin.GameGui.GetAddonByName("SelectYesno", 1);
            if (addonPtr == 0)
                return false;

            var addon = (AtkUnitBase*)addonPtr;
            if (!addon->IsVisible)
                return false;

            // Create AtkValue array for Yes button (index 0)
            var atkValues = stackalloc AtkValue[2];
            atkValues[0] = default;
            atkValues[1] = default;
            atkValues[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int;
            atkValues[0].Int = 0; // Yes button index
            atkValues[1].Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int;
            atkValues[1].Int = 0;

            addon->FireCallback(2, atkValues);
            Plugin.Log.Information("[YES/NO] Clicked Yes on SelectYesno dialog");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[YES/NO] ClickYesIfVisible failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Interact with a targeted game object via TargetSystem.
    /// Sets the Dalamud target first, then calls TargetSystem.InteractWithObject.
    /// Ported from LootGoblin's proven interaction pattern.
    /// </summary>
    public static unsafe bool InteractWithObject(IGameObject obj)
    {
        try
        {
            Plugin.Log.Information($"[INTERACT] Starting interaction with {obj.Name.TextValue} (Address: {obj.Address:X})");

            Plugin.TargetManager.Target = obj;

            var ts = FFXIVClientStructs.FFXIV.Client.Game.Control.TargetSystem.Instance();
            if (ts == null)
            {
                Plugin.Log.Error("[INTERACT] TargetSystem.Instance() returned null");
                return false;
            }

            var gameObjPtr = (GameObject*)obj.Address;
            if (gameObjPtr == null)
            {
                Plugin.Log.Error($"[INTERACT] Failed to cast GameObject* from address {obj.Address:X}");
                return false;
            }

            Plugin.Log.Information($"[INTERACT] Calling TargetSystem.InteractWithObject for {obj.Name.TextValue}");
            ts->InteractWithObject(gameObjPtr, true);
            Plugin.Log.Information($"[INTERACT] InteractWithObject called successfully for {obj.Name.TextValue} at {obj.Position}");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[INTERACT] InteractWithObject failed: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// Use the Repair general action (self-repair with dark matter).
    /// General Action 6 = Repair.
    /// </summary>
    public static unsafe bool UseRepairAction()
    {
        try
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null)
            {
                Plugin.Log.Warning("UseRepairAction: LocalPlayer is null");
                return false;
            }

            // Check if player is casting
            if (player.IsCasting)
            {
                Plugin.Log.Debug("UseRepairAction: Player is casting, skipping");
                return false;
            }

            // Check if player is occupied
            if (Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInQuestEvent] ||
                Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInCutSceneEvent] ||
                Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Occupied33] ||
                Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Occupied39])
            {
                Plugin.Log.Debug("UseRepairAction: Player is occupied, skipping");
                return false;
            }

            var am = ActionManager.Instance();
            if (am == null)
            {
                Plugin.Log.Warning("UseRepairAction: ActionManager is null");
                return false;
            }

            // Use General Action 6 (Repair)
            var result = am->UseAction(ActionType.GeneralAction, 6);
            Plugin.Log.Information($"UseRepairAction: UseAction result={result}");
            return result;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"UseRepairAction failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check if a UI addon is currently visible.
    /// Pattern from LootGoblin GameHelpers.
    /// </summary>
    public static unsafe bool IsAddonVisible(string addonName)
    {
        try
        {
            var addon = RaptureAtkUnitManager.Instance()->GetAddonByName(addonName);
            return addon != null && addon->IsVisible;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Send a slash command through the game chat path.
    /// </summary>
    public static unsafe bool SendChatCommand(string command, string logPrefix)
    {
        try
        {
            var uiModule = UIModule.Instance();
            if (uiModule == null)
            {
                Plugin.Log.Error($"{logPrefix} command failed [{command}]: UIModule null");
                return false;
            }

            var bytes = Encoding.UTF8.GetBytes(command);
            var utf8String = Utf8String.FromSequence(bytes);
            uiModule->ProcessChatBoxEntry(utf8String, nint.Zero);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"{logPrefix} command failed [{command}]: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Click a verified button node on a visible addon.
    /// </summary>
    public static bool ClickAddonButton(string addonName, int nodeListIndex)
    {
        return ClickAddonButton(addonName, nodeListIndex, out _);
    }

    /// <summary>
    /// Click a verified button node on a visible addon, returning a short reason when no click fires.
    /// </summary>
    public static bool ClickAddonButton(string addonName, int nodeListIndex, out string failureReason)
    {
        var diagnostic = TryClickAddonButtonDetailed(addonName, nodeListIndex);
        failureReason = diagnostic.FailureReason;
        return diagnostic.Result;
    }

    public sealed class AddonButtonClickDiagnostic
    {
        public int NodeListIndex { get; set; }
        public bool AddonVisible { get; set; }
        public int NodeCount { get; set; } = -1;
        public bool NodePresent { get; set; }
        public bool EventPresent { get; set; }
        public string EventParam { get; set; } = "n/a";
        public bool Result { get; set; }
        public string FailureReason { get; set; } = "";

        public string ToLogString()
        {
            var failure = string.IsNullOrEmpty(FailureReason) ? "none" : FailureReason;
            return $"addonVisible={BoolText(AddonVisible)}; nodeCount={NodeCount}; node{NodeListIndex}Present={BoolText(NodePresent)}; eventPresent={BoolText(EventPresent)}; eventParam={EventParam}; result={BoolText(Result)}; failureReason={failure}";
        }

        private static string BoolText(bool value) => value ? "true" : "false";
    }

    public static unsafe AddonButtonClickDiagnostic TryClickAddonButtonDetailed(string addonName, int nodeListIndex)
    {
        var diagnostic = new AddonButtonClickDiagnostic
        {
            NodeListIndex = nodeListIndex,
        };

        try
        {
            if (nodeListIndex < 0)
            {
                diagnostic.FailureReason = "node index out of range";
                return diagnostic;
            }

            var unitManager = RaptureAtkUnitManager.Instance();
            if (unitManager == null)
            {
                diagnostic.FailureReason = "unit manager not found";
                return diagnostic;
            }

            var addon = unitManager->GetAddonByName(addonName);
            if (addon == null)
            {
                diagnostic.FailureReason = "addon not found";
                return diagnostic;
            }

            diagnostic.AddonVisible = addon->IsVisible;
            diagnostic.NodeCount = (int)addon->UldManager.NodeListCount;

            if (!addon->IsVisible)
            {
                diagnostic.FailureReason = "addon hidden";
                return diagnostic;
            }

            if (nodeListIndex >= diagnostic.NodeCount)
            {
                diagnostic.FailureReason = "node index out of range";
                return diagnostic;
            }

            var node = addon->UldManager.NodeList[nodeListIndex];
            diagnostic.NodePresent = node != null;
            if (node == null)
            {
                diagnostic.FailureReason = "node null";
                return diagnostic;
            }

            var evt = node->AtkEventManager.Event;
            diagnostic.EventPresent = evt != null;
            if (evt == null)
            {
                diagnostic.FailureReason = "event null";
                return diagnostic;
            }

            diagnostic.EventParam = evt->Param.ToString();
            addon->ReceiveEvent((AtkEventType)25, (int)evt->Param, evt);
            diagnostic.Result = true;
            return diagnostic;
        }
        catch (Exception ex)
        {
            diagnostic.FailureReason = $"exception: {ex.Message}";
            Plugin.Log.Error($"ClickAddonButton({addonName}, {nodeListIndex}) failed: {ex.Message}");
            return diagnostic;
        }
    }

    /// <summary>
    /// Fire a callback on a named addon with variable arguments.
    /// Pattern from LootGoblin GameHelpers.
    /// SND equivalent: /callback AddonName true/false arg1 arg2 ...
    /// </summary>
    public static unsafe bool TryFireAddonCallback(string addonName, bool updateState, out string failureReason, params object[] args)
    {
        failureReason = "";

        try
        {
            var unitManager = RaptureAtkUnitManager.Instance();
            if (unitManager == null)
            {
                failureReason = "addon not found";
                return false;
            }

            var addon = unitManager->GetAddonByName(addonName);
            if (addon == null)
            {
                failureReason = "addon not found";
                return false;
            }

            if (!addon->IsVisible)
            {
                failureReason = "addon hidden";
                return false;
            }

            var atkValues = new AtkValue[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                if (!TryCreateAtkValue(args[i], out atkValues[i]))
                {
                    failureReason = "unsupported arg type";
                    return false;
                }
            }

            fixed (AtkValue* ptr = atkValues)
            {
                addon->FireCallback((uint)atkValues.Length, ptr, updateState);
            }

            return true;
        }
        catch (Exception ex)
        {
            failureReason = $"exception: {ex.Message}";
            Plugin.Log.Error($"[Callback] Failed for '{addonName}': {ex.Message}");
            return false;
        }
    }

    public static unsafe void FireAddonCallback(string addonName, bool updateState, params object[] args)
    {
        if (TryFireAddonCallback(addonName, updateState, out var failureReason, args))
        {
            Plugin.Log.Information($"[Callback] Fired on '{addonName}' with {args.Length} args, updateState={updateState}");
            return;
        }

        if (failureReason is "addon not found" or "addon hidden")
        {
            Plugin.Log.Warning($"[Callback] Addon '{addonName}' not found or not visible");
            return;
        }

        Plugin.Log.Error($"[Callback] Failed for '{addonName}': {failureReason}");
    }

    private static bool TryCreateAtkValue(object arg, out AtkValue atkValue)
    {
        atkValue = default;
        switch (arg)
        {
            case int intVal:
                atkValue.Type = AtkValueType.Int;
                atkValue.Int = intVal;
                return true;
            case uint uintVal:
                atkValue.Type = AtkValueType.UInt;
                atkValue.UInt = uintVal;
                return true;
            case bool boolVal:
                atkValue.Type = AtkValueType.Bool;
                atkValue.Byte = (byte)(boolVal ? 1 : 0);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Get remaining time for the current duty in seconds, or 0 if unavailable.
    /// Mirrors the EventFramework lookup already used elsewhere in this workspace.
    /// </summary>
    public static unsafe float GetDutyRemainingTime()
    {
        try
        {
            var eventFramework = EventFramework.Instance();
            if (eventFramework == null)
                return 0f;

            var instanceContentDirector = eventFramework->GetInstanceContentDirector();
            if (instanceContentDirector == null || !instanceContentDirector->HasTimer())
                return 0f;

            return instanceContentDirector->ContentTimeLeft;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"GetDutyRemainingTime failed: {ex.Message}");
            return 0f;
        }
    }

}
