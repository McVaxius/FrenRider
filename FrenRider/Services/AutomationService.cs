using System;
using Dalamud.Game.ClientState.Conditions;
using FrenRider.Models;

namespace FrenRider.Services;

public class AutomationService
{
    private readonly Plugin plugin;
    private readonly FrenTracker tracker;
    private readonly ZoneService zoneService;

    private int idleTickCounter;
    private long lastIdleActionMs;
    private long lastFoodCheckMs;
    private long lastCompanionCheckMs;
    private long companionStanceCooldownMs;
    private int idleListIndex;

    // Resolved food item ID (cached from name lookup or food search)
    private uint resolvedFoodItemId;
    private string resolvedFoodItemName = "";
    private bool foodIdResolved;

    private static readonly string[] DefaultIdleList = new[]
    {
        "/tomescroll",
        "/doze",
        "/sit",
        "/think",
        "/lookout",
        "/stretch",
        "/box",
        "/pushups",
    };

    public string LastIdleAction { get; private set; } = "";
    public bool IsIdle { get; private set; }
    public string FoodStatus { get; private set; } = "";
    public string CompanionStatus { get; private set; } = "";

    public AutomationService(Plugin plugin, FrenTracker tracker, ZoneService zoneService)
    {
        this.plugin = plugin;
        this.tracker = tracker;
        this.zoneService = zoneService;
    }

    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        if (!config.Enabled)
        {
            idleTickCounter = 0;
            IsIdle = false;
            return;
        }

        // Zone transition reset
        if (zoneService.ZoneChanged)
        {
            idleTickCounter = 0;
            IsIdle = false;
            LastIdleAction = "";
            foodIdResolved = false; // Re-resolve food on zone change
            return;
        }

        var inCombat = Plugin.Condition[ConditionFlag.InCombat];
        var mounted = Plugin.Condition[ConditionFlag.Mounted];
        var inDuty = Plugin.Condition[ConditionFlag.BoundByDuty];
        var now = Environment.TickCount64;

        // Don't idle if in combat or mounted
        if (inCombat || mounted)
        {
            idleTickCounter = 0;
            IsIdle = false;
            return;
        }

        // Check if following is idle (in range of fren, not moving)
        var follow = plugin.FollowService;
        if (follow.State == FollowState.InRange)
        {
            idleTickCounter++;

            if (idleTickCounter >= config.IdleTicksBeforeAction)
            {
                IsIdle = true;

                // Throttle idle actions to every 30 seconds minimum
                if (now - lastIdleActionMs > 30000)
                {
                    lastIdleActionMs = now;
                    PerformIdleAction(config);
                }
            }
        }
        else
        {
            idleTickCounter = 0;
            IsIdle = false;
        }

        // Food consumption check (every 10 seconds when not in combat)
        if (now - lastFoodCheckMs > 10000 && !inCombat)
        {
            lastFoodCheckMs = now;
            CheckFood(config);
        }

        // Companion chocobo summoning (every 15 seconds)
        if (now - lastCompanionCheckMs > 15000 && !inCombat && !mounted && !inDuty)
        {
            lastCompanionCheckMs = now;
            CheckCompanion(config);
        }

        // Deferred companion stance setting (after summoning, wait for spawn)
        if (companionStanceCooldownMs > 0 && now >= companionStanceCooldownMs)
        {
            companionStanceCooldownMs = 0;
            SetCompanionStance(config);
        }
    }

    private void PerformIdleAction(CharacterConfig config)
    {
        string action;

        switch (config.IdleActionMode)
        {
            case 0: // Specific action
                action = config.IdleAction;
                break;
            case 1: // Action from list
                var list = config.IdleListMode == 1 && config.CustomIdleList.Length > 0
                    ? config.CustomIdleList
                    : DefaultIdleList;

                if (list.Length == 0) return;
                action = list[idleListIndex % list.Length];
                idleListIndex++;
                break;
            default:
                return;
        }

        if (string.IsNullOrWhiteSpace(action)) return;

        LastIdleAction = action;
        SendCommand(action);
        Plugin.Log.Information($"Idle action: {action}");
    }

    /// <summary>
    /// Check if we need to eat food. Mirrors Lua food_deleter():
    /// - Check Well Fed status (ID 48) remaining time
    /// - If less than 90 seconds remaining, eat configured food
    /// - If configured food runs out and FeedMeSearch is true, search for alternatives
    /// </summary>
    private void CheckFood(CharacterConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.FeedMeItem)) return;
        if (!GameHelpers.IsPlayerAlive()) return;

        // Resolve food item ID from name if not yet done
        if (!foodIdResolved)
        {
            ResolveFoodItemId(config);
        }

        if (resolvedFoodItemId == 0)
        {
            FoodStatus = "No food item resolved";
            return;
        }

        // Check Well Fed buff remaining time
        var wellFedRemaining = GameHelpers.GetStatusTimeRemaining(GameHelpers.WellFedStatusId);

        if (wellFedRemaining > 90f)
        {
            FoodStatus = $"Well Fed: {wellFedRemaining:F0}s ({resolvedFoodItemName})";
            return;
        }

        // Need to eat — check if we have the food in inventory
        var count = GameHelpers.GetInventoryItemCount(resolvedFoodItemId);
        if (count > 0)
        {
            // Not in duty or not in combat — safe to eat (matches Lua: Condition[34]==false or Condition[26]==false)
            var inDuty = Plugin.Condition[ConditionFlag.BoundByDuty];
            var inCombat = Plugin.Condition[ConditionFlag.InCombat];

            if (!inDuty || !inCombat)
            {
                Plugin.Log.Information($"Eating food: {resolvedFoodItemName} (ID={resolvedFoodItemId}, count={count}, wellFed={wellFedRemaining:F1}s)");
                var result = GameHelpers.UseItem(resolvedFoodItemId);
                FoodStatus = result
                    ? $"Ate {resolvedFoodItemName} ({count - 1} left)"
                    : $"Failed to eat {resolvedFoodItemName}";
            }
            else
            {
                FoodStatus = $"Need food but in duty+combat";
            }
        }
        else
        {
            // Out of this food — try food search if enabled
            if (config.FeedMeSearch)
            {
                var (foundId, foundName) = GameHelpers.FindBestAvailableFood();
                if (foundId > 0)
                {
                    Plugin.Log.Information($"Food search: switched from {resolvedFoodItemName} to {foundName} (ID={foundId})");
                    resolvedFoodItemId = foundId;
                    resolvedFoodItemName = foundName;
                    FoodStatus = $"Switched to {foundName}";
                }
                else
                {
                    FoodStatus = "No food in inventory";
                    resolvedFoodItemId = 0;
                    foodIdResolved = false;
                }
            }
            else
            {
                FoodStatus = $"Out of {resolvedFoodItemName}";
            }
        }
    }

    /// <summary>
    /// Resolve the configured food name to an item ID.
    /// First checks the known food list, then falls back to Lumina lookup.
    /// </summary>
    private void ResolveFoodItemId(CharacterConfig config)
    {
        foodIdResolved = true;
        var foodName = config.FeedMeItem.Trim();

        // Check known food list first (fast path)
        foreach (var (id, name) in GameHelpers.FoodList)
        {
            if (name.Equals(foodName, StringComparison.OrdinalIgnoreCase))
            {
                resolvedFoodItemId = id;
                resolvedFoodItemName = name;
                Plugin.Log.Information($"Food resolved from known list: {name} -> ID {id}");
                return;
            }
        }

        // Lumina lookup
        var itemId = GameHelpers.LookupFoodItemId(foodName);
        if (itemId > 0)
        {
            resolvedFoodItemId = itemId;
            resolvedFoodItemName = foodName;
            Plugin.Log.Information($"Food resolved from Lumina: {foodName} -> ID {itemId}");
            return;
        }

        // If food search is enabled, try to find anything
        if (config.FeedMeSearch)
        {
            var (foundId, foundName) = GameHelpers.FindBestAvailableFood();
            if (foundId > 0)
            {
                resolvedFoodItemId = foundId;
                resolvedFoodItemName = foundName;
                Plugin.Log.Information($"Food search found: {foundName} -> ID {foundId}");
                return;
            }
        }

        Plugin.Log.Warning($"Could not resolve food item: {foodName}");
        resolvedFoodItemId = 0;
        resolvedFoodItemName = "";
    }

    /// <summary>
    /// Check if we need to summon chocobo companion. Mirrors Lua logic:
    /// - Not in sanctuary
    /// - Not in duty
    /// - Not mounted
    /// - BuddyTimeRemaining less than 900s (15 minutes)
    /// - Have Gysahl Greens (item ID 4868)
    /// - ForceGysahl config enabled
    /// </summary>
    private void CheckCompanion(CharacterConfig config)
    {
        if (!config.ForceGysahl)
        {
            CompanionStatus = "";
            return;
        }

        var mounted = Plugin.Condition[ConditionFlag.Mounted];
        var riding = Plugin.Condition[ConditionFlag.Mounting71];
        var inDuty = Plugin.Condition[ConditionFlag.BoundByDuty];
        var now = Environment.TickCount64;

        if (mounted || riding || inDuty)
        {
            CompanionStatus = "Can't summon (mounted/duty)";
            return;
        }

        // Check sanctuary — can't summon companion in sanctuary
        if (GameHelpers.IsInSanctuary())
        {
            CompanionStatus = "In sanctuary";
            return;
        }

        // Check companion timer
        var buddyTime = GameHelpers.GetBuddyTimeRemaining();
        if (buddyTime > 900f) // More than 15 minutes remaining — no need to re-summon
        {
            var mins = (int)(buddyTime / 60);
            var secs = (int)(buddyTime % 60);
            CompanionStatus = $"Companion: {mins}m{secs:D2}s";
            return;
        }

        // Check if we have Gysahl Greens
        var greensCount = GameHelpers.GetInventoryItemCount(GameHelpers.GysahlGreensItemId);
        if (greensCount <= 0)
        {
            CompanionStatus = "No Gysahl Greens";
            return;
        }

        // Summon companion!
        Plugin.Log.Information($"Summoning companion chocobo (buddyTime={buddyTime:F1}s, greens={greensCount})");
        var result = GameHelpers.UseItem(GameHelpers.GysahlGreensItemId);
        if (result)
        {
            CompanionStatus = $"Summoning chocobo ({greensCount - 1} greens left)";

            // Set stance after a short delay (companion needs to spawn)
            // The actual stance command fires from Update() when cooldown expires
            companionStanceCooldownMs = now + 3000; // 3 seconds
        }
        else
        {
            CompanionStatus = "Failed to summon chocobo";
        }
    }

    /// <summary>
    /// Set companion stance. Mirrors Lua: /cac "CompanionStrat"
    /// </summary>
    private void SetCompanionStance(CharacterConfig config)
    {
        var stanceCmd = config.CompanionStrat switch
        {
            "Defender Stance" => "/cac \"Defender Stance\"",
            "Attacker Stance" => "/cac \"Attacker Stance\"",
            "Healer Stance" => "/cac \"Healer Stance\"",
            "Follow" => "/cac \"Follow\"",
            _ => "/cac \"Free Stance\"",
        };

        Plugin.Log.Information($"Setting companion stance: {stanceCmd}");
        SendCommand(stanceCmd);
    }

    /// <summary>
    /// Trigger repair based on config (0=No, 1=Self, 2=Inn NPC).
    /// </summary>
    public void TriggerRepair(CharacterConfig config)
    {
        switch (config.Repair)
        {
            case 1: // Self repair
                SendCommand("/generalaction \"Repair\"");
                Plugin.Log.Information("Triggered self repair");
                break;
            case 2: // NPC repair (would need to interact with mender NPC)
                Plugin.Log.Information("NPC repair not yet implemented");
                break;
        }
    }

    /// <summary>
    /// Force re-resolution of food item ID (e.g., after config change).
    /// </summary>
    public void InvalidateFoodCache()
    {
        foodIdResolved = false;
        resolvedFoodItemId = 0;
        resolvedFoodItemName = "";
    }

    private static void SendCommand(string command)
    {
        try
        {
            if (!Plugin.CommandManager.ProcessCommand(command))
                Plugin.Log.Warning($"Automation command not handled: {command}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Automation command failed [{command}]: {ex.Message}");
        }
    }
}
