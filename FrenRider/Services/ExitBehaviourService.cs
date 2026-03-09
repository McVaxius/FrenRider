using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
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

    // Exit object navigation state
    private IGameObject? exitTarget;
    private bool isNavigatingToExit;
    private DateTime lastExitInteractTime = DateTime.MinValue;
    private DateTime lastExitScanTime = DateTime.MinValue;
    private DateTime lastPartyCheckTime = DateTime.MinValue;
    private DateTime lastLeaveAttemptTime = DateTime.MinValue;
    private int leaveAttemptCount;

    // CBT auto-leave tracking
    private bool cbtConfigured;

    // Known exit object names (from LootGoblin treasure dungeon patterns)
    private static readonly string[] ExitObjectNames =
    {
        "Cairn of Return",
        "Exit",
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

    private void OnDutyCompleted(object? sender, ushort territoryId)
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

        var inDuty = Plugin.Condition[ConditionFlag.BoundByDuty] ||
                     Plugin.Condition[ConditionFlag.BoundByDuty56];

        // Detect BoundByDuty transition (true→false) as duty end signal
        // This catches all duty types including treasure dungeons where DutyCompleted may not fire
        if (wasBoundByDuty && !inDuty)
        {
            if (!dutyCompleted)
            {
                dutyCompleted = true;
                dutyCompletedTime = DateTime.Now;
                dutyLeaveIssued = false;
                Plugin.Log.Information("[ExitBehaviour] BoundByDuty transition detected (was bound, now free) - marking duty completed");
            }
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
            if (cbtConfigured)
            {
                cbtConfigured = false;
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

        // Rule 1: Exit if exit object exists - path to it + interact (LootGoblin pattern)
        if (config.ExitIfExitExists)
        {
            CheckExitObject();
        }

        // Rule 2: Exit N seconds after duty ends
        if (config.ExitAfterDutyEnds && dutyCompleted && !dutyLeaveIssued)
        {
            var elapsed = (DateTime.Now - dutyCompletedTime).TotalSeconds;
            if (elapsed >= config.ExitAfterDutySeconds)
            {
                Plugin.Log.Information($"[ExitBehaviour] Leaving duty - {config.ExitAfterDutySeconds}s elapsed since duty completed");
                LeaveDuty();
                dutyLeaveIssued = true;
            }
            else
            {
                StateDetail = $"Duty completed, leaving in {(config.ExitAfterDutySeconds - elapsed):F0}s...";
            }
        }

        // Rule 3: Leave when all others have left the zone
        if (config.LeaveWhenAllLeft)
        {
            CheckPartyInZone();
        }

        // Rule 4: Exit via CBT (Automaton) auto-leave
        if (config.ExitViaCBT && !cbtConfigured)
        {
            ConfigureCBTAutoLeave(config);
        }
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

        IGameObject? nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj == null) continue;
            if (obj.ObjectKind != ObjectKind.EventObj) continue;

            var name = obj.Name.ToString();
            if (string.IsNullOrEmpty(name)) continue;

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
                    break;
                }
            }
        }

        if (nearest != null)
        {
            exitTarget = nearest;
            isNavigatingToExit = false;
            Plugin.Log.Information($"[ExitBehaviour] Found exit object '{nearest.Name.TextValue}' at {nearestDist:F1}y - will path to it");
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
        if (partyList.Length <= 1)
        {
            // Solo or empty party - leave
            Plugin.Log.Information($"[ExitBehaviour] Party list empty/solo (count={partyList.Length}) - leaving duty");
            LeaveDuty();
            return;
        }

        // Check how many party members are actually visible in the zone
        int membersInZone = 0;
        foreach (var member in partyList)
        {
            if (member == null) continue;

            // Skip self
            var memberName = member.Name.ToString();
            var localName = localPlayer.Name.ToString();
            if (memberName == localName) continue;

            // Check if this party member is visible in the ObjectTable (meaning they're in the same zone)
            bool isVisible = false;
            foreach (var obj in Plugin.ObjectTable)
            {
                if (obj == null) continue;
                if (obj.ObjectKind != ObjectKind.Player) continue;
                if (obj.Name.ToString() == memberName)
                {
                    isVisible = true;
                    break;
                }
            }

            if (isVisible)
                membersInZone++;
        }

        if (membersInZone == 0)
        {
            Plugin.Log.Information($"[ExitBehaviour] No other party members in zone (party list={partyList.Length}, in zone=0) - leaving duty");
            LeaveDuty();
        }
        else
        {
            Plugin.Log.Debug($"[ExitBehaviour] Party check: {membersInZone} member(s) still in zone");
        }
    }

    private void ConfigureCBTAutoLeave(CharacterConfig config)
    {
        // Send IPC commands to configure Automaton (CBT) plugin auto-leave
        // Automaton uses /at commands for configuration
        Plugin.Log.Information($"[ExitBehaviour] Configuring CBT auto-leave: enabled, {config.ExitViaCBTSeconds}s delay");

        try
        {
            // Enable Enhanced Duty Start/End in Automaton
            SendCommand("/at edse on");
            // Enable auto-leave
            SendCommand("/at autoleave on");
            // Configure seconds
            SendCommand($"/at autoleave {config.ExitViaCBTSeconds}");
            cbtConfigured = true;
            Plugin.Log.Information("[ExitBehaviour] CBT auto-leave configured successfully");
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[ExitBehaviour] Failed to configure CBT auto-leave: {ex.Message}");
        }
    }

    private void LeaveDuty()
    {
        var now = DateTime.UtcNow;
        // Throttle leave attempts to every 5 seconds
        if ((now - lastLeaveAttemptTime).TotalSeconds < 5.0) return;
        lastLeaveAttemptTime = now;
        leaveAttemptCount++;

        Plugin.Log.Information($"[ExitBehaviour] Leave duty attempt #{leaveAttemptCount}");

        // Try multiple approaches to leave
        // Approach 1: /pdfinder leave (if pdfinder plugin is available)
        SendCommand("/pdfinder leave");

        // Approach 2: Direct /leaveduty (may work in some contexts)
        SendCommand("/leaveduty");

        StateDetail = $"Leaving duty (attempt #{leaveAttemptCount})...";
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
