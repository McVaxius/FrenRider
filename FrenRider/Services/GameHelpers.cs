using System;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace FrenRider.Services;

/// <summary>
/// Static unsafe helpers for game state queries: inventory, status effects, item usage, companion.
/// </summary>
public static class GameHelpers
{
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
            return im->GetInventoryItemCount(itemId) + im->GetInventoryItemCount(itemId, true);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"GetInventoryItemCount({itemId}) failed: {ex.Message}");
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
    /// </summary>
    public static unsafe bool UseItem(uint itemId)
    {
        try
        {
            var am = ActionManager.Instance();
            if (am == null) return false;

            // Check if the action is ready
            var status = am->GetActionStatus(ActionType.Item, itemId);
            if (status != 0)
            {
                Plugin.Log.Warning($"UseItem({itemId}): ActionStatus={status}, not ready");
                return false;
            }

            var result = am->UseAction(ActionType.Item, itemId);
            Plugin.Log.Information($"UseItem({itemId}): result={result}");
            return result;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"UseItem({itemId}) failed: {ex.Message}");
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
    /// </summary>
    public static unsafe bool IsInSanctuary()
    {
        try
        {
            var am = ActionManager.Instance();
            if (am == null) return true; // Assume sanctuary if we can't check

            // If mount action is available (status 0), we're NOT in sanctuary
            var status = am->GetActionStatus(ActionType.GeneralAction, 9);
            return status != 0;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Look up a food item ID by name using Lumina game data.
    /// Returns 0 if not found.
    /// </summary>
    public static uint LookupFoodItemId(string foodName)
    {
        if (string.IsNullOrWhiteSpace(foodName)) return 0;

        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            if (sheet == null) return 0;

            var lowerName = foodName.ToLowerInvariant();
            foreach (var row in sheet)
            {
                var name = row.Name.ToString();
                if (!string.IsNullOrEmpty(name) && name.Equals(foodName, StringComparison.OrdinalIgnoreCase))
                    return row.RowId;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"LookupFoodItemId(\"{foodName}\") failed: {ex.Message}");
        }
        return 0;
    }

    /// <summary>
    /// Search inventory for the best available food from the food list.
    /// Returns (itemId, itemName) or (0, "") if none found.
    /// </summary>
    public static (uint Id, string Name) FindBestAvailableFood()
    {
        // Search from end (highest priority) to start
        for (var i = FoodList.Length - 1; i >= 0; i--)
        {
            if (GetInventoryItemCount(FoodList[i].Id) > 0)
                return FoodList[i];
        }
        return (0, "");
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
}
