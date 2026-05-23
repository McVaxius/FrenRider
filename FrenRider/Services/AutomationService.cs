using System;
using Dalamud.Game.ClientState.Conditions;
using FrenRider.Models;

namespace FrenRider.Services;

public class AutomationService
{
    private const long RepairIdleCheckMs = 30000;
    private const long RepairActiveCheckMs = 1000;
    private const long RepairRetryMs = 30000;
    private const long RepairStartGraceMs = 5000;
    private const long RepairTimeoutMs = 180000;
    private const long RepairBlockedLogMs = 15000;

    private enum RepairFlowState
    {
        Idle,
        WaitingForAdsStart,
        WaitingForAdsCompletion,
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
    private long lastRepairBlockedLogMs;
    private string lastDiscardDeferReason = "";
    private string lastRepairBlockedReason = "";
    private RepairFlowState repairFlowState;
    private bool adsRepairUtilityObserved;
    private int repairRequestAttempts;
    private int idleListIndex;

    // Resolved food item ID (cached from name lookup or food search)
    private uint resolvedFoodItemId;
    private string resolvedFoodItemName = "";
    private bool resolvedFoodUseHighQuality;
    private bool foodIdResolved;
    private int cachedFeedMeItemId = int.MinValue;
    private string cachedFeedMeItem = "";
    private bool cachedFeedMeUseHighQuality;

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
    public bool IsRepairFlowActive => repairFlowState != RepairFlowState.Idle;

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
            InvalidateFoodCache(); // Re-resolve food on zone change
            CancelRepairFlow("Repair reset: zone transition.");
            return;
        }

        var inCombat = Plugin.Condition[ConditionFlag.InCombat];
        var mounted = Plugin.Condition[ConditionFlag.Mounted];
        var inDuty = Plugin.Condition[ConditionFlag.BoundByDuty];
        var now = Environment.TickCount64;

        CheckRepair(config, now);
        if (IsRepairFlowActive)
        {
            idleTickCounter = 0;
            IsIdle = false;
            LastIdleAction = "Repair active";
            FoodStatus = "Paused for repair";
            CompanionStatus = "Paused for repair";
            return;
        }

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

    public void UpdateRepairGate()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        if (!config.Enabled)
        {
            CancelRepairFlow("");
            return;
        }

        if (plugin.AdsIntegrationService.ShouldPauseDutySystems)
            return;

        if (zoneService.ZoneChanged)
        {
            CancelRepairFlow("Repair reset: zone transition.");
            return;
        }

        CheckRepair(config, Environment.TickCount64);
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
        if (config.FeedMeItemId <= 0 && string.IsNullOrWhiteSpace(config.FeedMeItem)) return;
        if (!GameHelpers.IsPlayerAlive()) return;

        if (BackfillLegacyFoodConfig(config))
            InvalidateFoodCache();

        InvalidateFoodCacheIfConfigChanged(config);

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
            FoodStatus = $"Well Fed: {wellFedRemaining:F0}s ({resolvedFoodItemName} [{QualityLabel(resolvedFoodUseHighQuality)}])";
            return;
        }

        // Need to eat — check if we have the food in inventory
        var count = GameHelpers.GetInventoryItemCount(resolvedFoodItemId, resolvedFoodUseHighQuality);
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
                var qualityLabel = QualityLabel(resolvedFoodUseHighQuality);
                Plugin.Log.Information($"Eating food: {resolvedFoodItemName} [{qualityLabel}] (ID={resolvedFoodItemId}, count={count}, wellFed={wellFedRemaining:F1}s)");
                var result = GameHelpers.UseItem(resolvedFoodItemId, resolvedFoodUseHighQuality);
                if (result)
                {
                    FoodStatus = $"Ate {resolvedFoodItemName} [{qualityLabel}] ({count - 1} left)";
                    // Success - don't check again for 30 seconds to let buff apply
                    lastFoodCheckMs = now + 20000; // Add 20s to the next check time
                }
                else
                {
                    FoodStatus = $"Failed to eat {resolvedFoodItemName} [{qualityLabel}]";
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
                var (foundId, foundName, foundHighQuality, foundCount) = GameHelpers.FindBestAvailableFood();
                if (foundId > 0)
                {
                    var qualityLabel = QualityLabel(foundHighQuality);
                    Plugin.Log.Information($"Food search: switched from {resolvedFoodItemName} [{QualityLabel(resolvedFoodUseHighQuality)}] to {foundName} [{qualityLabel}] (ID={foundId}, count={foundCount})");
                    resolvedFoodItemId = foundId;
                    resolvedFoodItemName = foundName;
                    resolvedFoodUseHighQuality = foundHighQuality;
                    FoodStatus = $"Switched to {foundName} [{qualityLabel}]";
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
                FoodStatus = $"Out of {resolvedFoodItemName} [{QualityLabel(resolvedFoodUseHighQuality)}]";
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
        resolvedFoodItemId = 0;
        resolvedFoodItemName = "";
        resolvedFoodUseHighQuality = config.FeedMeUseHighQuality;

        if (config.FeedMeItemId > 0)
        {
            resolvedFoodItemId = (uint)config.FeedMeItemId;
            resolvedFoodItemName = !string.IsNullOrWhiteSpace(config.FeedMeItem)
                ? config.FeedMeItem.Trim()
                : GameHelpers.LookupItemName((uint)config.FeedMeItemId);
            if (string.IsNullOrWhiteSpace(resolvedFoodItemName))
                resolvedFoodItemName = $"Item {config.FeedMeItemId}";

            if (string.IsNullOrWhiteSpace(config.FeedMeItem) && !resolvedFoodItemName.StartsWith("Item ", StringComparison.Ordinal))
            {
                config.FeedMeItem = resolvedFoodItemName;
                plugin.ConfigManager.SaveCurrentAccount();
            }

            Plugin.Log.Information($"Food resolved from config ID: {resolvedFoodItemName} [{QualityLabel(resolvedFoodUseHighQuality)}] -> ID {resolvedFoodItemId}");
            return;
        }

        var foodName = config.FeedMeItem.Trim();

        // Check known food list first (fast path)
        foreach (var (id, name) in GameHelpers.FoodList)
        {
            if (name.Equals(foodName, StringComparison.OrdinalIgnoreCase))
            {
                resolvedFoodItemId = id;
                resolvedFoodItemName = name;
                Plugin.Log.Information($"Food resolved from known list: {name} [{QualityLabel(resolvedFoodUseHighQuality)}] -> ID {id}");
                return;
            }
        }

        // Lumina lookup
        var (itemId, itemName) = GameHelpers.LookupFoodItem(foodName);
        if (itemId > 0)
        {
            resolvedFoodItemId = itemId;
            resolvedFoodItemName = itemName;
            Plugin.Log.Information($"Food resolved from Lumina: {itemName} [{QualityLabel(resolvedFoodUseHighQuality)}] -> ID {itemId}");
            return;
        }

        // If food search is enabled, try to find anything
        if (config.FeedMeSearch)
        {
            var (foundId, foundName, foundHighQuality, foundCount) = GameHelpers.FindBestAvailableFood();
            if (foundId > 0)
            {
                resolvedFoodItemId = foundId;
                resolvedFoodItemName = foundName;
                resolvedFoodUseHighQuality = foundHighQuality;
                Plugin.Log.Information($"Food search found: {foundName} [{QualityLabel(foundHighQuality)}] -> ID {foundId} (count={foundCount})");
                return;
            }
        }

        Plugin.Log.Warning($"Could not resolve food item: {foodName}");
        resolvedFoodItemId = 0;
        resolvedFoodItemName = "";
    }

    private bool BackfillLegacyFoodConfig(CharacterConfig config)
    {
        if (config.FeedMeItemId > 0) return false;
        if (string.IsNullOrWhiteSpace(config.FeedMeItem)) return false;

        var (itemId, itemName) = GameHelpers.LookupFoodItem(config.FeedMeItem);
        if (itemId == 0) return false;

        config.FeedMeItemId = (int)itemId;
        config.FeedMeItem = itemName;
        plugin.ConfigManager.SaveCurrentAccount();
        Plugin.Log.Information($"Backfilled legacy food config: {itemName} -> ID {itemId}");
        return true;
    }

    private void InvalidateFoodCacheIfConfigChanged(CharacterConfig config)
    {
        var foodName = config.FeedMeItem ?? "";
        if (cachedFeedMeItemId == config.FeedMeItemId
            && string.Equals(cachedFeedMeItem, foodName, StringComparison.Ordinal)
            && cachedFeedMeUseHighQuality == config.FeedMeUseHighQuality)
        {
            return;
        }

        cachedFeedMeItemId = config.FeedMeItemId;
        cachedFeedMeItem = foodName;
        cachedFeedMeUseHighQuality = config.FeedMeUseHighQuality;
        foodIdResolved = false;
        resolvedFoodItemId = 0;
        resolvedFoodItemName = "";
        resolvedFoodUseHighQuality = config.FeedMeUseHighQuality;
    }

    private static string QualityLabel(bool highQuality)
        => highQuality ? "HQ" : "NQ";

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
        var repairMode = ResolveAdsRepairMode(config);
        if (string.IsNullOrWhiteSpace(repairMode))
        {
            CancelRepairFlow("");
            return;
        }

        var threshold = Math.Clamp(config.TornClothes, 0, 100);
        if (threshold <= 0)
        {
            ResetRepairFlow();
            RepairStatus = $"{GetRepairModeLabel(repairMode)} repair enabled; threshold is 0%.";
            return;
        }

        var adsStatus = plugin.AdsRepairIpcService.Refresh();
        if (!adsStatus.IsAvailable)
        {
            ResetRepairFlow();
            RepairStatus = "Waiting: ADS not loaded.";
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

        if (repairFlowState == RepairFlowState.Idle)
        {
            if (!CanStartRepairNow(repairMode, out var deferReason))
            {
                RepairStatus = $"{GetRepairModeLabel(repairMode)} repair needed below {threshold}%; waiting while {deferReason}.";
                LogRepairBlocked(now, deferReason);
                return;
            }

            IssueRepairRequest(now, repairMode, threshold, $"equipped gear below {threshold}%");
            return;
        }

        TrackActiveRepair(now, repairMode, threshold);
    }

    private void TrackActiveRepair(long now, string repairMode, int threshold)
    {
        var elapsedSinceRequest = now - lastRepairAttemptMs;
        if (elapsedSinceRequest > RepairTimeoutMs)
        {
            var timeoutStatus = plugin.AdsRepairIpcService.Refresh(force: true);
            RepairStatus = $"Repair timed out after {RepairTimeoutMs / 1000}s; ADS: {GetAdsRepairStatusText(timeoutStatus)}";
            Plugin.Log.Warning($"[FrenRider][Repair] {RepairStatus}");
            ResetRepairFlow(preserveStatus: true);
            return;
        }

        var adsStatus = plugin.AdsRepairIpcService.Refresh(force: true);
        if (!adsStatus.StatusReadable)
        {
            RepairStatus = $"{GetRepairModeLabel(repairMode)} repair requested; waiting for ADS status.";
            return;
        }

        if (adsStatus.IsRepairRunning)
        {
            adsRepairUtilityObserved = true;
            repairFlowState = RepairFlowState.WaitingForAdsCompletion;
            var detail = string.IsNullOrWhiteSpace(adsStatus.UtilityStatus)
                ? "ADS repair running"
                : adsStatus.UtilityStatus;
            RepairStatus = $"{GetRepairModeLabel(repairMode)} repair running; FrenRider paused. ADS: {detail}";
            return;
        }

        if (!adsRepairUtilityObserved && elapsedSinceRequest < RepairStartGraceMs)
        {
            repairFlowState = RepairFlowState.WaitingForAdsStart;
            RepairStatus = $"{GetRepairModeLabel(repairMode)} repair requested; waiting for ADS to start.";
            return;
        }

        repairFlowState = RepairFlowState.WaitingForDurability;
        if (elapsedSinceRequest < RepairRetryMs)
        {
            RepairStatus = $"{GetRepairModeLabel(repairMode)} repair still needed below {threshold}%; retry in {(RepairRetryMs - elapsedSinceRequest + 999) / 1000}s. ADS: {GetAdsRepairStatusText(adsStatus)}";
            return;
        }

        if (!CanStartRepairNow(repairMode, out var deferReason))
        {
            RepairStatus = $"{GetRepairModeLabel(repairMode)} repair still needed below {threshold}%; waiting while {deferReason}. ADS: {GetAdsRepairStatusText(adsStatus)}";
            LogRepairBlocked(now, deferReason);
            return;
        }

        IssueRepairRequest(now, repairMode, threshold, $"still below {threshold}% after {elapsedSinceRequest / 1000}s");
    }

    private void IssueRepairRequest(long now, string repairMode, int threshold, string reason)
    {
        repairRequestAttempts++;
        lastRepairAttemptMs = now;
        repairFlowState = RepairFlowState.WaitingForAdsStart;
        adsRepairUtilityObserved = false;
        var modeLabel = GetRepairModeLabel(repairMode);

        if (repairRequestAttempts == 1)
        {
            Plugin.Log.Information($"[FrenRider][Repair] Requesting ADS {repairMode} repair via IPC ({reason}).");
        }
        else
        {
            Plugin.Log.Warning($"[FrenRider][Repair] Retrying ADS {repairMode} repair attempt {repairRequestAttempts} via IPC ({reason}).");
        }

        if (plugin.AdsRepairIpcService.StartRepair(repairMode, out var failure))
        {
            RepairStatus = $"{modeLabel} repair requested below {threshold}%; FrenRider paused.";
            Plugin.Log.Information($"[FrenRider][Repair] ADS {repairMode} repair requested (threshold {threshold}%).");
            return;
        }

        RepairStatus = $"ADS did not accept {modeLabel} repair: {failure}";
        Plugin.Log.Warning($"[FrenRider][Repair] ADS did not accept {repairMode} repair: {failure}");
        ResetRepairFlow(preserveStatus: true);
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

    private void ResetRepairFlow(bool preserveStatus = false)
    {
        repairFlowState = RepairFlowState.Idle;
        lastRepairAttemptMs = 0;
        lastRepairBlockedLogMs = 0;
        lastRepairBlockedReason = "";
        adsRepairUtilityObserved = false;
        repairRequestAttempts = 0;
        if (!preserveStatus)
            plugin.AdsRepairIpcService.Refresh();
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

    private static bool CanStartRepairNow(string repairMode, out string reason)
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

        if (GameHelpers.TryGetMountedOrRidingOrMountingBlocker(out var mountBlocker))
        {
            reason = mountBlocker;
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

        if (repairMode == "npc-no-inn" && !GameHelpers.IsInSanctuary())
        {
            reason = "outside sanctuary";
            return false;
        }

        reason = "ready";
        return true;
    }

    /// <summary>
    /// Trigger repair based on config (0=Disabled, 1=Self, 2=NPC no-inn). FrenRider always delegates repair to ADS.
    /// </summary>
    public void TriggerRepair(CharacterConfig config)
    {
        var repairMode = ResolveAdsRepairMode(config);
        if (string.IsNullOrWhiteSpace(repairMode))
            return;

        var now = Environment.TickCount64;
        var threshold = Math.Clamp(config.TornClothes, 0, 100);
        if (threshold <= 0)
            return;

        if (repairFlowState != RepairFlowState.Idle)
            return;

        var adsStatus = plugin.AdsRepairIpcService.Refresh();
        if (!adsStatus.IsAvailable)
            return;

        if (!CanStartRepairNow(repairMode, out _))
            return;

        if (GameHelpers.NeedsRepair(threshold))
        {
            IssueRepairRequest(now, repairMode, threshold, $"manual trigger below {threshold}%");
        }
    }

    private static string ResolveAdsRepairMode(CharacterConfig config)
        => config.Repair switch
        {
            1 => "self",
            2 => "npc-no-inn",
            _ => string.Empty,
        };

    private static string GetRepairModeLabel(string repairMode)
        => repairMode switch
        {
            "npc-no-inn" => "NPC no-inn",
            "self" => "Self",
            _ => "ADS",
        };

    private static string GetAdsRepairStatusText(AdsRepairStatusSnapshot status)
    {
        if (!string.IsNullOrWhiteSpace(status.UtilityLastFailure))
            return status.UtilityLastFailure;

        if (!string.IsNullOrWhiteSpace(status.UtilityLastSuccess))
            return status.UtilityLastSuccess;

        return string.IsNullOrWhiteSpace(status.UtilityStatus)
            ? "No ADS utility status."
            : status.UtilityStatus;
    }

    /// <summary>
    /// Force re-resolution of food item ID (e.g., after config change).
    /// </summary>
    public void InvalidateFoodCache()
    {
        foodIdResolved = false;
        resolvedFoodItemId = 0;
        resolvedFoodItemName = "";
        resolvedFoodUseHighQuality = false;
        cachedFeedMeItemId = int.MinValue;
        cachedFeedMeItem = "";
        cachedFeedMeUseHighQuality = false;
    }

    private static bool SendCommand(string command)
    {
        return GameHelpers.SendChatCommand(command, "Automation");
    }
}
