using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FrenRider.Models;

namespace FrenRider.Services;

/// <summary>
/// Handles automatic exit behaviour based on configurable rules:
/// 1. Exit if an exit object (Cairn of Return, etc.) exists in the zone - path to it + interact
/// 2. Exit N seconds after duty ends (via IDutyState.DutyCompleted + BoundByDuty transition)
/// 3. Leave duty when all other party members have left the zone
/// 4. Exit via CBT (Automaton) auto-leave after N seconds
/// </summary>
public class ExitBehaviourService : IDisposable
{
    private readonly Plugin plugin;
    private readonly FrenTracker tracker;
    private readonly ZoneService zoneService;

    // Duty completion tracking
    private bool dutyCompleted;
    private DateTime dutyCompletedTime;
    private bool dutyLeaveIssued;
    private bool wasBoundByDuty;
    private DateTime dutyEnteredTime = DateTime.MinValue;
    private DateTime boundByDutyLostTime = DateTime.MinValue; // Debounce for BoundByDuty flicker
    private const double DutyGracePeriodSeconds = 30.0;

    // Exit object navigation state
    private IGameObject? exitTarget;
    private bool isNavigatingToExit;
    private DateTime lastExitInteractTime = DateTime.MinValue;
    private DateTime lastExitScanTime = DateTime.MinValue;
    private DateTime lastPartyCheckTime = DateTime.MinValue;
    private DateTime lastLeaveAttemptTime = DateTime.MinValue;
    private int leaveAttemptCount;

    // Known exit object names (expanded from LootGoblin patterns)
    private static readonly string[] ExitObjectNames =
    {
        "Cairn of Return",
        "Exit",
        "Atomos",
        "Teleportation Portal",
        "Portal",
        "Gate",
        "Door",
    };

    private const float ExitScanRange = 50f;
    private const float ExitInteractRange = 3f;
    private const float ExitNavRange = 6f;

    public string StateDetail { get; private set; } = "";

    public ExitBehaviourService(Plugin plugin, FrenTracker tracker, ZoneService zoneService)
    {
        this.plugin = plugin;
        this.tracker = tracker;
        this.zoneService = zoneService;

        // Hook DutyCompleted event
        Plugin.DutyState.DutyCompleted += OnDutyCompleted;
    }

    public void Dispose()
    {
        Plugin.DutyState.DutyCompleted -= OnDutyCompleted;
    }

    private void OnDutyCompleted(Dalamud.Game.DutyState.IDutyStateEventArgs args)
        => OnDutyCompleted(args.TerritoryType.RowId);

    private void OnDutyCompleted(uint territoryId)
    {
        dutyCompleted = true;
        dutyCompletedTime = DateTime.Now;
        dutyLeaveIssued = false;
        Plugin.Log.Information($"[ExitBehaviour] Duty completed in territory {territoryId}");
    }

    /// <summary>
    /// Called every framework tick. Evaluates exit rules and takes action if needed.
    /// </summary>
    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        if (!config.Enabled) return;

        if (plugin.AdsIntegrationService.ShouldPauseDutySystems)
        {
            StateDetail = plugin.AdsIntegrationService.IsHandoffPending
                ? "ADS handoff pending"
                : "ADS active";
            return;
        }

        // Validate mutually exclusive exit options - if both are somehow checked, disable both
        if (config.ExitAfterDutyEnds && config.LeaveWhenAllLeft)
        {
            Plugin.Log.Warning("[ExitBehaviour] Both exit options were enabled simultaneously - disabling both to prevent conflicts");
            config.ExitAfterDutyEnds = false;
            config.LeaveWhenAllLeft = false;
            plugin.ConfigManager.SaveCurrentAccount();
        }

        var inDuty = Plugin.Condition[ConditionFlag.BoundByDuty] ||
                     Plugin.Condition[ConditionFlag.BoundByDuty56];

        // Track duty entry for grace period
        if (inDuty && !wasBoundByDuty)
        {
            dutyEnteredTime = DateTime.Now;
            Plugin.Log.Information($"[ExitBehaviour] Entered duty - {DutyGracePeriodSeconds}s grace period before exit checks");
        }

        // Detect BoundByDuty transition (true→false) as duty end signal
        // This catches all duty types including treasure dungeons where DutyCompleted may not fire
        // DEBOUNCE: BoundByDuty can flicker during loading screens within a duty,
        // so only detect transition if we've been unbound for >2 seconds
        if (wasBoundByDuty && !inDuty)
        {
            if (boundByDutyLostTime == DateTime.MinValue)
            {
                boundByDutyLostTime = DateTime.Now;
                Plugin.Log.Debug("[ExitBehaviour] BoundByDuty dropped - starting 2s debounce");
            }
            else if ((DateTime.Now - boundByDutyLostTime).TotalSeconds >= 2.0)
            {
                if (!dutyCompleted)
                {
                    dutyCompleted = true;
                    dutyCompletedTime = DateTime.Now;
                    dutyLeaveIssued = false;
                    Plugin.Log.Information("[ExitBehaviour] BoundByDuty transition confirmed (was bound, now free for 2s) - marking duty completed");
                }
            }
        }
        else
        {
            // Reset debounce if BoundByDuty came back
            if (boundByDutyLostTime != DateTime.MinValue && inDuty)
            {
                Plugin.Log.Debug("[ExitBehaviour] BoundByDuty restored during debounce - false alarm");
            }
            boundByDutyLostTime = DateTime.MinValue;
        }
        wasBoundByDuty = inDuty;

        // Reset state when no longer in duty
        if (!inDuty)
        {
            if (exitTarget != null || isNavigatingToExit)
            {
                exitTarget = null;
                isNavigatingToExit = false;
            }
            if (dutyCompleted && dutyLeaveIssued)
            {
                dutyCompleted = false;
                dutyLeaveIssued = false;
                leaveAttemptCount = 0;
                Plugin.Log.Debug("[ExitBehaviour] No longer in duty - reset completion state");
            }
            StateDetail = "";
            return;
        }

        // Don't try to leave during loading screens
        if (Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51])
            return;

        // Don't try to leave during combat
        if (Plugin.Condition[ConditionFlag.InCombat])
            return;

        // Grace period: don't check exit conditions for first 30s after entering duty
        // This prevents premature exits when party members haven't loaded in yet
        if (dutyEnteredTime != DateTime.MinValue)
        {
            var sinceDutyEntry = (DateTime.Now - dutyEnteredTime).TotalSeconds;
            if (sinceDutyEntry < DutyGracePeriodSeconds)
            {
                StateDetail = $"Grace period ({DutyGracePeriodSeconds - sinceDutyEntry:F0}s)...";
                return;
            }
        }

        // Exit object feature removed - no longer needed

        // Rule 2: Exit N seconds after duty ends
        if (config.ExitAfterDutyEnds && dutyCompleted && !dutyLeaveIssued)
        {
            var elapsed = (DateTime.Now - dutyCompletedTime).TotalSeconds;
            if (elapsed >= config.ExitAfterDutySeconds)
            {
                Plugin.Log.Information($"[ExitBehaviour] === LEAVE DUTY TRIGGERED ===");
                Plugin.Log.Information($"[ExitBehaviour] Reason: ExitAfterDutyEnds={config.ExitAfterDutyEnds}, elapsed={elapsed:F1}s >= configured={config.ExitAfterDutySeconds}s");
                Plugin.Log.Information($"[ExitBehaviour] DutyCompleted={dutyCompleted}, CompletedAt={dutyCompletedTime:HH:mm:ss}, InDuty={inDuty}");
                LeaveDuty();
                dutyLeaveIssued = true;
            }
            else
            {
                StateDetail = $"Duty completed, leaving in {(config.ExitAfterDutySeconds - elapsed):F0}s...";
                // Log every 10 seconds during countdown
                if ((int)elapsed % 10 == 0 && elapsed > 0)
                {
                    Plugin.Log.Debug($"[ExitBehaviour] Duty leave countdown: {elapsed:F0}s elapsed, {config.ExitAfterDutySeconds - elapsed:F0}s remaining");
                }
            }
        }
        else if (!config.ExitAfterDutyEnds && dutyCompleted)
        {
            // Clear duty completion state when feature is disabled
            Plugin.Log.Debug("[ExitBehaviour] Exit after duty ends feature disabled - clearing completion state");
            dutyCompleted = false;
            dutyLeaveIssued = false;
            leaveAttemptCount = 0;
        }

        // Rule 3: Leave when all others have left the zone
        if (config.LeaveWhenAllLeft)
        {
            CheckPartyInZone();
        }
        else
        {
            // Clear party check state when feature is disabled
            if (lastPartyCheckTime > DateTime.MinValue)
            {
                Plugin.Log.Debug("[ExitBehaviour] Leave when all others left feature disabled - clearing check state");
                lastPartyCheckTime = DateTime.MinValue;
            }
        }

        // CBT auto-leave feature removed
    }

    private void CheckExitObject()
    {
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null) return;

        var now = DateTime.UtcNow;

        // If we have a current exit target, check if it's still valid
        if (exitTarget != null)
        {
            // Check if target is still valid and in range
            bool targetValid = false;
            foreach (var obj in Plugin.ObjectTable)
            {
                if (obj != null && obj.GameObjectId == exitTarget.GameObjectId)
                {
                    targetValid = true;
                    exitTarget = obj; // Refresh reference
                    break;
                }
            }

            if (!targetValid)
            {
                Plugin.Log.Debug("[ExitBehaviour] Exit target no longer valid, resetting");
                exitTarget = null;
                isNavigatingToExit = false;
                return;
            }

            var dist = Vector3.Distance(localPlayer.Position, exitTarget.Position);

            if (dist <= ExitInteractRange)
            {
                // Close enough - stop nav and interact
                if (isNavigatingToExit)
                {
                    SendCommand("/vnav stop");
                    isNavigatingToExit = false;
                }

                // Interact every 2 seconds
                if ((now - lastExitInteractTime).TotalSeconds >= 2.0)
                {
                    lastExitInteractTime = now;
                    Plugin.Log.Information($"[ExitBehaviour] Interacting with exit '{exitTarget.Name.TextValue}' at {dist:F1}y");
                    GameHelpers.InteractWithObject(exitTarget);
                }
                StateDetail = $"Interacting with exit '{exitTarget.Name.TextValue}' ({dist:F1}y)";
            }
            else if (dist <= ExitNavRange)
            {
                // Very close but not interactable yet - stop nav, use lockon+automove
                if (isNavigatingToExit)
                {
                    SendCommand("/vnav stop");
                    isNavigatingToExit = false;
                }

                // Target and auto-move toward it
                Plugin.TargetManager.Target = exitTarget;
                SendCommand("/lockon on");
                SendCommand("/automove on");

                // Also try interacting
                if ((now - lastExitInteractTime).TotalSeconds >= 2.0)
                {
                    lastExitInteractTime = now;
                    GameHelpers.InteractWithObject(exitTarget);
                }
                StateDetail = $"Approaching exit '{exitTarget.Name.TextValue}' ({dist:F1}y)";
            }
            else
            {
                // Far away - navigate to it
                if (!isNavigatingToExit || (now - lastExitScanTime).TotalSeconds >= 5.0)
                {
                    isNavigatingToExit = true;
                    lastExitScanTime = now;
                    var coords = FormatVector(exitTarget.Position);
                    SendCommand($"/vnav moveto {coords}");
                    Plugin.Log.Information($"[ExitBehaviour] Navigating to exit '{exitTarget.Name.TextValue}' at {dist:F1}y");
                }
                StateDetail = $"Navigating to exit '{exitTarget.Name.TextValue}' ({dist:F1}y)";
            }
            return;
        }

        // Scan for exit objects (throttled to every 2s)
        if ((now - lastExitScanTime).TotalSeconds < 2.0) return;
        lastExitScanTime = now;

        Plugin.Log.Debug($"[ExitBehaviour] Scanning for exit objects in range {ExitScanRange}y...");

        IGameObject? nearest = null;
        float nearestDist = float.MaxValue;
        int scannedObjects = 0;
        int potentialExits = 0;

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj == null) continue;
            if (obj.ObjectKind != ObjectKind.EventObj) continue;
            if (!obj.IsTargetable) continue;

            scannedObjects++;
            var name = obj.Name.ToString();
            
            // Check for known exit names
            foreach (var exitName in ExitObjectNames)
            {
                if (name.Contains(exitName, StringComparison.OrdinalIgnoreCase))
                {
                    var dist = Vector3.Distance(localPlayer.Position, obj.Position);
                    if (dist < ExitScanRange && dist < nearestDist)
                    {
                        nearest = obj;
                        nearestDist = dist;
                    }
                    potentialExits++;
                    Plugin.Log.Debug($"[ExitBehaviour] Found potential exit '{name}' at {dist:F1}y");
                    break;
                }
            }
        }

        Plugin.Log.Debug($"[ExitBehaviour] Exit scan complete: {scannedObjects} EventObj scanned, {potentialExits} potential exits found");

        if (nearest != null)
        {
            exitTarget = nearest;
            isNavigatingToExit = false;
            Plugin.Log.Information($"[ExitBehaviour] Selected exit object '{nearest.Name.TextValue}' at {nearestDist:F1}y - will path to it");
        }
        else
        {
            Plugin.Log.Debug("[ExitBehaviour] No exit objects found in range");
        }
    }

    private void CheckPartyInZone()
    {
        var now = DateTime.UtcNow;
        // Throttle to every 3 seconds
        if ((now - lastPartyCheckTime).TotalSeconds < 3.0) return;
        lastPartyCheckTime = now;

        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null) return;

        // Count party members actually visible in the zone (present in ObjectTable)
        // PartyList.Length counts ALL party members including those who left the instance
        var partyList = Plugin.PartyList;
        
        Plugin.Log.Debug($"[ExitBehaviour] Party check: Party list has {partyList.Length} members");

        if (partyList.Length <= 1)
        {
            // Solo or empty party - leave
            Plugin.Log.Information($"[ExitBehaviour] Party list empty/solo (count={partyList.Length}) - leaving duty");
            LeaveDuty();
            return;
        }

        // Check how many party members are actually visible in the zone
        int membersInZone = 0;
        var memberNames = new System.Collections.Generic.List<string>();
        
        foreach (var member in partyList)
        {
            if (member == null) continue;

            // Skip self
            var memberName = member.Name.ToString();
            var localName = localPlayer.Name.ToString();
            if (memberName == localName) continue;

            memberNames.Add(memberName);

            // Check if this party member is visible in the ObjectTable (meaning they're in the same zone)
            foreach (var obj in Plugin.ObjectTable)
            {
                if (obj == null) continue;
                if (obj.ObjectKind != ObjectKind.Pc) continue;
                if (obj.Name.ToString() == memberName)
                {
                    membersInZone++;
                    break;
                }
            }
        }

        Plugin.Log.Debug($"[ExitBehaviour] Party members in zone: {membersInZone}/{partyList.Length - 1} (checking: {string.Join(", ", memberNames)})");

        if (membersInZone == 0)
        {
            Plugin.Log.Information($"[ExitBehaviour] No other party members in zone (party list={partyList.Length}, in zone=0) - leaving duty");
            LeaveDuty();
        }
        else
        {
            Plugin.Log.Debug($"[ExitBehaviour] Party check: {membersInZone} member(s) still in zone - not leaving");
        }
    }

    
    private void LeaveDuty()
    {
        var now = DateTime.UtcNow;
        // Throttle leave attempts to every 5 seconds
        if ((now - lastLeaveAttemptTime).TotalSeconds < 5.0) return;
        lastLeaveAttemptTime = now;
        leaveAttemptCount++;

        // Determine why we're leaving
        var config = plugin.ConfigManager.GetActiveConfig();
        string leaveReason = "Unknown";
        
        if (config.ExitAfterDutyEnds && dutyCompleted)
        {
            var elapsed = (DateTime.Now - dutyCompletedTime).TotalSeconds;
            leaveReason = $"Exit after duty ends - {elapsed:F0}s elapsed (configured: {config.ExitAfterDutySeconds}s)";
        }
        else if (config.LeaveWhenAllLeft)
        {
            leaveReason = "Leave when all party members left";
        }
        
        Plugin.Log.Information($"[ExitBehaviour] Leave duty attempt #{leaveAttemptCount} - REASON: {leaveReason}");
        Plugin.Log.Information($"[ExitBehaviour] Opening duty panel to leave");

        // Open duty panel to access Leave Duty button
        SendCommand("/dutyfinder");

        // Wait a moment for panel to open, then try to click Leave Duty
        System.Threading.Tasks.Task.Delay(500).ContinueWith(_ => {
            try
            {
                TryClickLeaveDutyButton();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[ExitBehaviour] ContinueWith exception in TryClickLeaveDutyButton: {ex.Message}");
            }
        }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnRanToCompletion);

        // Also try clicking Yes on any confirmation dialog that appears
        System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ => {
            try
            {
                if (GameHelpers.ClickYesIfVisible())
                {
                    Plugin.Log.Information("[ExitBehaviour] Successfully clicked Yes on leave duty confirmation");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[ExitBehaviour] ContinueWith exception in ClickYesIfVisible: {ex.Message}");
            }
        }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnRanToCompletion);

        StateDetail = $"Leaving duty (attempt #{leaveAttemptCount}) - {leaveReason}";
    }

    private unsafe void TryClickLeaveDutyButton()
    {
        try
        {
            // Use xa docs callback pattern: Open ContentsFinderMenu directly, then click Leave button (node 43)
            Plugin.Log.Information("[ExitBehaviour] Opening ContentsFinderMenu with callback");
            
            // Try direct callback to open ContentsFinderMenu (pattern from Character true 12)
            try
            {
                // Based on xa docs pattern - try different callback numbers to open ContentsFinderMenu
                GameHelpers.FireAddonCallback("ContentsFinderMenu", true, 0);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[ExitBehaviour] ContentsFinderMenu callback failed: {ex.Message}");
            }
            
            // Wait a moment for the menu to open, then click Leave button
            System.Threading.Tasks.Task.Delay(500).ContinueWith(_ => {
                try
                {
                    TryClickLeaveButton();
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"[ExitBehaviour] ContinueWith exception in TryClickLeaveButton: {ex.Message}");
                }
            }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnRanToCompletion);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[ExitBehaviour] Error trying to leave duty: {ex.Message}");
        }
    }

    private unsafe void TryClickLeaveButton()
    {
        try
        {
            // Click Leave button using xa docs pattern: ClickAddonButton("ContentsFinderMenu", 43)
            Plugin.Log.Information("[ExitBehaviour] Clicking Leave button on ContentsFinderMenu");
            GameHelpers.FireAddonCallback("ContentsFinderMenu", true, 43);
            
            // Handle the confirmation dialog
            System.Threading.Tasks.Task.Delay(500).ContinueWith(_ => {
                try
                {
                    HandleLeaveConfirmation();
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"[ExitBehaviour] ContinueWith exception in HandleLeaveConfirmation: {ex.Message}");
                }
            }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnRanToCompletion);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[ExitBehaviour] Error clicking Leave button: {ex.Message}");
        }
    }

    private void HandleLeaveConfirmation()
    {
        try
        {
            // Click Yes on SelectYesno confirmation dialog
            Plugin.Log.Information("[ExitBehaviour] Clicking Yes on leave confirmation dialog");
            GameHelpers.ClickYesIfVisible();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[ExitBehaviour] Error handling leave confirmation: {ex.Message}");
        }
    }

    private static string FormatVector(Vector3 value)
    {
        return string.Format(CultureInfo.InvariantCulture, "{0:F2} {1:F2} {2:F2}", value.X, value.Y, value.Z);
    }

    private static unsafe void SendCommand(string command)
    {
        try
        {
            if (Plugin.CommandManager.ProcessCommand(command))
                return;

            var uiModule = UIModule.Instance();
            if (uiModule == null) return;

            var bytes = Encoding.UTF8.GetBytes(command);
            var utf8String = Utf8String.FromSequence(bytes);
            uiModule->ProcessChatBoxEntry(utf8String, nint.Zero);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[ExitBehaviour] Command failed [{command}]: {ex.Message}");
        }
    }
}
