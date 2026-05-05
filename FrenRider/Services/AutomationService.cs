using System;
using Dalamud.Game.ClientState.Conditions;
using FrenRider.Models;

namespace FrenRider.Services;

public class AutomationService
{
    private const long RepairIdleCheckMs = 30000;
    private const long RepairActiveCheckMs = 5000;
    private const long RepairRetryMs = 10000;
    private const long RepairStopSettleMs = 1000;
    private const long RepairBlockedLogMs = 15000;
    private const string AdsStopCommand = "/ads stop";
    private const string AdsSelfRepairCommand = "/ads selfrepair";

    private enum RepairFlowState
    {
        Idle,
        WaitingForAdsStopSettle,
        WaitingForDurability,
    }

    private readonly Plugin plugin;
    private readonly FrenTracker tracker;
    private readonly ZoneService zoneService;

    private int idleTickCounter;
    private long lastIdleActionMs;
    private long lastFoodCheckMs;
    private long lastFoodAttemptMs;
    private long lastCompanionCheckMs;
    private long lastCompanionAttemptMs;
    private long companionStanceCooldownMs;
    private long lastDiscardMs;
    private long lastDiscardDeferLogMs;
    private long lastRepairCheckMs;
    private long lastRepairAttemptMs;
    private long repairNextActionMs;
    private long lastRepairBlockedLogMs;
    private string lastDiscardDeferReason = "";
    private string lastRepairBlockedReason = "";
    private RepairFlowState repairFlowState;
    private int repairRequestAttempts;
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
    public string RepairStatus { get; private set; } = "";

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
            CancelRepairFlow("");
            return;
        }

        if (plugin.AdsIntegrationService.ShouldPauseDutySystems)
        {
            idleTickCounter = 0;
            IsIdle = false;
            LastIdleAction = plugin.AdsIntegrationService.IsHandoffPending
                ? "ADS handoff pending"
                : "ADS active";
            return;
        }

        // Zone transition reset
        if (zoneService.ZoneChanged)
        {
            idleTickCounter = 0;
            IsIdle = false;
            LastIdleAction = "";
            foodIdResolved = false; // Re-resolve food on zone change
            CancelRepairFlow("Repair reset: zone transition.");
            return;
        }

        var inCombat = Plugin.Condition[ConditionFlag.InCombat];
        var mounted = Plugin.Condition[ConditionFlag.Mounted];
        var inDuty = Plugin.Condition[ConditionFlag.BoundByDuty];
        var now = Environment.TickCount64;

        // Auto-discard (every 10 seconds, but only during mounted-safe idle windows)
        if (config.EnableAutoDiscard && now - lastDiscardMs > 10000)
        {
            if (GameHelpers.CanAutoDiscardNow(out var discardReason))
            {
                lastDiscardMs = now;
                lastDiscardDeferReason = "";
                if (SendCommand("/ays discard"))
                    Plugin.Log.Debug("Auto-discard: sent /ays discard");
            }
            else if (discardReason != lastDiscardDeferReason || now - lastDiscardDeferLogMs > 10000)
            {
                lastDiscardDeferReason = discardReason;
                lastDiscardDeferLogMs = now;
                Plugin.Log.Debug($"Auto-discard deferred: {discardReason}");
            }
        }

        CheckRepair(config, now);

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
                var list = DefaultIdleList;
                if (config.IdleListMode == 1)
                {
                    if (config.EnsureCustomIdleListSeeded())
                        plugin.ConfigManager.SaveCurrentAccount();

                    list = CharacterConfig.GetExecutableCustomIdleCommands(config.CustomIdleList);
                }

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
            var now = Environment.TickCount64;

            if (!inDuty || !inCombat)
            {
                // Throttle: only attempt once per 5 seconds to avoid spam
                if (now - lastFoodAttemptMs < 5000)
                {
                    FoodStatus = $"Need food (cooldown {(5000 - (now - lastFoodAttemptMs)) / 1000.0:F1}s)";
                    return;
                }

                lastFoodAttemptMs = now;
                Plugin.Log.Information($"Eating food: {resolvedFoodItemName} (ID={resolvedFoodItemId}, count={count}, wellFed={wellFedRemaining:F1}s)");
                var result = GameHelpers.UseItem(resolvedFoodItemId);
                if (result)
                {
                    FoodStatus = $"Ate {resolvedFoodItemName} ({count - 1} left)";
                    // Success - don't check again for 30 seconds to let buff apply
                    lastFoodCheckMs = now + 20000; // Add 20s to the next check time
                }
                else
                {
                    FoodStatus = $"Failed to eat {resolvedFoodItemName}";
                }
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

        // Throttle: only attempt once per 5 seconds to avoid spam
        if (now - lastCompanionAttemptMs < 5000)
        {
            CompanionStatus = $"Need companion (cooldown {(5000 - (now - lastCompanionAttemptMs)) / 1000.0:F1}s)";
            return;
        }

        // Summon companion!
        lastCompanionAttemptMs = now;
        Plugin.Log.Information($"Summoning companion chocobo (buddyTime={buddyTime:F1}s, greens={greensCount})");
        var result = GameHelpers.UseItem(GameHelpers.GysahlGreensItemId);
        if (result)
        {
            CompanionStatus = $"Summoning chocobo ({greensCount - 1} greens left)";

            // Set stance after a short delay (companion needs to spawn)
            // The actual stance command fires from Update() when cooldown expires
            companionStanceCooldownMs = now + 3000; // 3 seconds
            
            // Success - don't check again for 30 seconds to let companion spawn
            lastCompanionCheckMs = now + 20000; // Add 20s to the next check time
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

    private void CheckRepair(CharacterConfig config, long now)
    {
        if (config.Repair == 2)
        {
            config.Repair = 0;
            plugin.ConfigManager.SaveCurrentAccount();
            ResetRepairFlow();
            RepairStatus = "Legacy NPC repair disabled.";
            return;
        }

        if (config.Repair != 1)
        {
            CancelRepairFlow("");
            return;
        }

        var threshold = Math.Clamp(config.TornClothes, 0, 100);
        if (threshold <= 0)
        {
            ResetRepairFlow();
            RepairStatus = "Self repair enabled; threshold is 0%.";
            return;
        }

        if (!plugin.AdsIntegrationService.AdsLoaded)
        {
            ResetRepairFlow();
            RepairStatus = "Waiting: ADS not loaded.";
            return;
        }

        if (repairFlowState == RepairFlowState.WaitingForAdsStopSettle)
        {
            ContinueRepairAfterAdsStop(now, threshold);
            return;
        }

        var checkInterval = repairFlowState == RepairFlowState.Idle
            ? RepairIdleCheckMs
            : RepairActiveCheckMs;
        if (now - lastRepairCheckMs < checkInterval)
            return;

        lastRepairCheckMs = now;

        var needsRepair = GameHelpers.NeedsRepair(threshold);
        if (!needsRepair)
        {
            if (repairFlowState != RepairFlowState.Idle)
            {
                CompleteRepairFlow(threshold);
            }
            else
            {
                RepairStatus = $"No equipped gear below {threshold}%.";
            }
            return;
        }

        if (!CanSelfRepairNow(out var deferReason))
        {
            RepairStatus = $"Deferred: {deferReason}.";
            LogRepairBlocked(now, deferReason);
            return;
        }

        if (repairFlowState == RepairFlowState.Idle)
        {
            IssueRepairRequest(now, threshold, $"equipped gear below {threshold}%");
            return;
        }

        var elapsedSinceRequest = now - lastRepairAttemptMs;
        if (elapsedSinceRequest < RepairRetryMs)
        {
            RepairStatus = $"Repair in progress below {threshold}%; retry in {(RepairRetryMs - elapsedSinceRequest + 999) / 1000}s.";
            return;
        }

        IssueRepairRequest(now, threshold, $"still below {threshold}% after {elapsedSinceRequest / 1000}s");
    }

    private void IssueRepairRequest(long now, int threshold, string reason)
    {
        repairRequestAttempts++;
        lastRepairAttemptMs = now;
        repairNextActionMs = now + RepairStopSettleMs;
        repairFlowState = RepairFlowState.WaitingForAdsStopSettle;

        if (repairRequestAttempts == 1)
        {
            Plugin.Log.Information($"[FrenRider][Repair] Sending ADS self-repair request ({reason}).");
        }
        else
        {
            Plugin.Log.Warning($"[FrenRider][Repair] Retrying ADS self-repair request attempt {repairRequestAttempts} ({reason}).");
        }

        if (SendCommand(AdsStopCommand))
        {
            RepairStatus = $"Stopping ADS before self repair below {threshold}%.";
            return;
        }

        repairFlowState = RepairFlowState.WaitingForDurability;
        RepairStatus = "Failed to send /ads stop before self repair.";
        Plugin.Log.Warning("[FrenRider][Repair] ADS stop command failed before self repair.");
    }

    private void ContinueRepairAfterAdsStop(long now, int threshold)
    {
        if (now < repairNextActionMs)
        {
            RepairStatus = $"Stopping ADS before self repair below {threshold}%.";
            return;
        }

        if (!GameHelpers.NeedsRepair(threshold))
        {
            CompleteRepairFlow(threshold);
            return;
        }

        if (!CanSelfRepairNow(out var deferReason))
        {
            RepairStatus = $"Deferred: {deferReason}.";
            LogRepairBlocked(now, deferReason);
            return;
        }

        repairFlowState = RepairFlowState.WaitingForDurability;
        if (SendCommand(AdsSelfRepairCommand))
        {
            RepairStatus = $"Sent /ads selfrepair below {threshold}%.";
            Plugin.Log.Information($"[FrenRider][Repair] ADS self repair requested (threshold {threshold}%).");
            return;
        }

        RepairStatus = "Failed to send /ads selfrepair.";
        Plugin.Log.Warning("[FrenRider][Repair] Self repair command failed: /ads selfrepair");
    }

    private void CompleteRepairFlow(int threshold)
    {
        ResetRepairFlow();
        RepairStatus = $"Repair complete; no equipped gear below {threshold}%.";
        Plugin.Log.Information($"[FrenRider][Repair] Repair complete; equipped gear is no longer below {threshold}%.");
    }

    private void CancelRepairFlow(string status)
    {
        if (repairFlowState != RepairFlowState.Idle)
            Plugin.Log.Information("[FrenRider][Repair] Clearing active repair flow.");

        ResetRepairFlow();
        RepairStatus = status;
    }

    private void ResetRepairFlow()
    {
        repairFlowState = RepairFlowState.Idle;
        repairNextActionMs = 0;
        lastRepairAttemptMs = 0;
        lastRepairBlockedLogMs = 0;
        lastRepairBlockedReason = "";
        repairRequestAttempts = 0;
    }

    private void LogRepairBlocked(long now, string reason)
    {
        if (string.Equals(lastRepairBlockedReason, reason, StringComparison.OrdinalIgnoreCase) &&
            now - lastRepairBlockedLogMs < RepairBlockedLogMs)
        {
            return;
        }

        lastRepairBlockedReason = reason;
        lastRepairBlockedLogMs = now;
        Plugin.Log.Information($"[FrenRider][Repair] Repair request held while {reason}; rechecking durability instead of sending another repair command.");
    }

    private static bool CanSelfRepairNow(out string reason)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
        {
            reason = "local player unavailable";
            return false;
        }

        if (!GameHelpers.IsPlayerAlive())
        {
            reason = "player dead";
            return false;
        }

        if (player.IsCasting)
        {
            reason = "casting";
            return false;
        }

        if (Plugin.Condition[ConditionFlag.InCombat])
        {
            reason = "in combat";
            return false;
        }

        if (Plugin.Condition[ConditionFlag.BoundByDuty] ||
            Plugin.Condition[ConditionFlag.BoundByDuty56])
        {
            reason = "in duty";
            return false;
        }

        if (Plugin.Condition[ConditionFlag.BetweenAreas] ||
            Plugin.Condition[ConditionFlag.BetweenAreas51])
        {
            reason = "between areas";
            return false;
        }

        if (Plugin.Condition[ConditionFlag.Mounted] ||
            Plugin.Condition[ConditionFlag.RidingPillion])
        {
            reason = "mounted or riding";
            return false;
        }

        if (Plugin.Condition[ConditionFlag.OccupiedInQuestEvent] ||
            Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
            Plugin.Condition[ConditionFlag.Occupied33] ||
            Plugin.Condition[ConditionFlag.Occupied39] ||
            Plugin.Condition[ConditionFlag.WatchingCutscene])
        {
            reason = "occupied or in cutscene";
            return false;
        }

        if (GameHelpers.IsAddonVisible("ContentsFinderConfirm"))
        {
            reason = "duty confirm popup";
            return false;
        }

        if (GameHelpers.IsAddonVisible("SelectYesno"))
        {
            reason = "SelectYesno dialog";
            return false;
        }

        reason = "ready";
        return true;
    }

    /// <summary>
    /// Trigger repair based on config (0=No, 1=Self). FrenRider always delegates repair to ADS.
    /// </summary>
    public void TriggerRepair(CharacterConfig config)
    {
        if (config.Repair != 1)
            return;

        var now = Environment.TickCount64;
        var threshold = Math.Clamp(config.TornClothes, 0, 100);
        if (threshold <= 0)
            return;

        if (repairFlowState != RepairFlowState.Idle)
            return;

        if (!plugin.AdsIntegrationService.AdsLoaded)
            return;

        if (CanSelfRepairNow(out _) && GameHelpers.NeedsRepair(threshold))
        {
            IssueRepairRequest(now, threshold, $"manual trigger below {threshold}%");
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

    private static bool SendCommand(string command)
    {
        return GameHelpers.SendChatCommand(command, "Automation");
    }
}
