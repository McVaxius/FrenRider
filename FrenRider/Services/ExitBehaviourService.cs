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
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FrenRider.Models;

namespace FrenRider.Services;

internal readonly record struct DutyExitConditions(
    bool InDuty,
    bool IsBetweenAreas,
    bool InCombat,
    bool UtilityActive,
    bool AdsPaused);

internal readonly record struct DutyPartyMember(uint TerritoryId, bool HasLocalObject);

internal sealed record DutyPartyRoster(ulong LocalCharacterId, long PartyId, bool CanConfirmSolo,
    IReadOnlyDictionary<ulong, DutyPartyMember> Members, IReadOnlySet<ulong> HudMembers)
{
    public bool RostersAgree => Members.ContainsKey(LocalCharacterId) && HudMembers.SetEquals(Members.Keys);
}

internal sealed class DutyPartyExitState
{
    private enum Departure { Present, MissingFromReadyRoster, Confirmed }

    private Dictionary<ulong, Departure>? entryTeammates;
    private ulong localCharacterId;
    private long entryPartyId;
    private (uint TerritoryTypeId, uint ContentFinderConditionId) entryDuty;
    private DateTime nextSampleUtc;
    private bool observationReady;

    internal bool AllTeammatesDeparted => observationReady && entryTeammates is { Count: > 0 }
        && entryTeammates.Values.All(state => state == Departure.Confirmed);

    internal void Observe((uint TerritoryTypeId, uint ContentFinderConditionId) duty, bool dutyCompleted,
        DateTime nowUtc, Func<uint, DutyPartyRoster?> readRoster)
    {
        if (entryTeammates != null && (duty.TerritoryTypeId != entryDuty.TerritoryTypeId
            || entryDuty.ContentFinderConditionId != 0 && duty.ContentFinderConditionId != entryDuty.ContentFinderConditionId))
        {
            InterruptObservation();
            return;
        }
        observationReady = true;
        if (nowUtc < nextSampleUtc || entryTeammates is { Count: 0 } || dutyCompleted && entryTeammates == null)
            return;
        nextSampleUtc = nowUtc.AddSeconds(3);
        var roster = readRoster(duty.TerritoryTypeId);
        if (entryTeammates == null)
        {
            if (roster != null) CaptureEntry(duty, roster);
            return;
        }
        if (entryDuty.ContentFinderConditionId == 0)
            entryDuty = duty;

        if (roster != null && roster.LocalCharacterId != localCharacterId)
        {
            InterruptObservation();
            return;
        }
        if (roster == null || !roster.Members.ContainsKey(localCharacterId) || !roster.HudMembers.Contains(localCharacterId))
        {
            ClearUnconfirmedDepartures();
            return;
        }

        // Two consecutive agreeing, nonempty rosters confirm a departure when the native row is removed.
        var readyRoster = roster.RostersAgree && (roster.PartyId == entryPartyId
            || roster.PartyId == 0 && roster.Members.Count == 1);
        foreach (var id in entryTeammates.Keys)
        {
            var previous = entryTeammates[id];
            if (roster.Members.TryGetValue(id, out var member))
            {
                if (member.HasLocalObject || member.TerritoryId == duty.TerritoryTypeId)
                    entryTeammates[id] = Departure.Present;
                else if (member.TerritoryId != 0)
                    entryTeammates[id] = Departure.Confirmed;
                else if (previous == Departure.MissingFromReadyRoster)
                    entryTeammates[id] = Departure.Present;
            }
            else if (readyRoster && previous != Departure.Confirmed)
            {
                entryTeammates[id] = previous == Departure.MissingFromReadyRoster
                    ? Departure.Confirmed : Departure.MissingFromReadyRoster;
            }
            else if (!readyRoster && previous == Departure.MissingFromReadyRoster)
                entryTeammates[id] = Departure.Present;
        }
    }

    private void CaptureEntry((uint TerritoryTypeId, uint ContentFinderConditionId) duty, DutyPartyRoster roster)
    {
        if (roster.Members.Count <= 1 && roster.PartyId == 0 && roster.CanConfirmSolo
            && roster.HudMembers.Count == 1 && roster.HudMembers.Contains(roster.LocalCharacterId)
            && (roster.Members.Count == 0 || roster.RostersAgree))
        {
            entryDuty = duty;
            entryTeammates = [];
            return;
        }
        if (roster.Members.Count <= 1 || !roster.RostersAgree || roster.Members.Any(entry => entry.Key != roster.LocalCharacterId
                && !entry.Value.HasLocalObject && entry.Value.TerritoryId != duty.TerritoryTypeId))
            return;

        entryDuty = duty;
        localCharacterId = roster.LocalCharacterId;
        entryPartyId = roster.PartyId;
        entryTeammates = roster.Members.Keys.Where(id => id != localCharacterId).ToDictionary(id => id, _ => Departure.Present);
    }

    internal void InterruptObservation()
    {
        observationReady = false;
        ClearUnconfirmedDepartures();
    }

    private void ClearUnconfirmedDepartures()
    {
        if (entryTeammates == null) return;
        foreach (var id in entryTeammates.Keys)
        {
            if (entryTeammates[id] == Departure.MissingFromReadyRoster)
                entryTeammates[id] = Departure.Present;
        }
    }

    internal void Reset()
    {
        entryTeammates = null;
        localCharacterId = 0;
        entryPartyId = 0;
        entryDuty = default;
        nextSampleUtc = default;
        observationReady = false;
    }
}

/// <summary>
/// Handles automatic exit behaviour based on configurable rules:
/// 1. Exit if an exit object (Cairn of Return, etc.) exists in the zone - path to it + interact
/// 2. Exit N seconds after duty ends (via IDutyState.DutyCompleted)
/// 3. Leave completed duty after the entry party has departed
/// 4. Exit via CBT (Automaton) auto-leave after N seconds
/// </summary>
public class ExitBehaviourService : IDisposable
{
    private readonly Plugin plugin;
    private readonly FrenTracker tracker;
    private readonly ZoneService zoneService;

    // Duty completion tracking
    private AdsDutySession DutySession => plugin.AdsIntegrationService.DutySession;
    private bool dutyCompleted => DutySession.IsCompleted;
    private DateTime dutyCompletedTime => DutySession.CompletedAtUtc;
    private bool dutyLeaveIssued => DutySession.LeaveIssued;
    private const double DutyGracePeriodSeconds = 30.0;

    // Exit object navigation state
    private IGameObject? exitTarget;
    private bool isNavigatingToExit;
    private DateTime lastExitInteractTime = DateTime.MinValue;
    private DateTime lastExitScanTime = DateTime.MinValue;
    private DateTime lastLeaveAttemptTime = DateTime.MinValue;
    private int leaveAttemptCount;
    private LeaveDutyStep leaveDutyStep = LeaveDutyStep.None;
    private DateTime leaveDutyStepDueAt = DateTime.MinValue;
    private string pendingLeaveReason = "";
    private bool pendingPartyDepartureExit;
    private int leaveConfirmationAttemptCount;
    private const int MaxLeaveConfirmationAttempts = 3;

    private enum LeaveDutyStep
    {
        None,
        OpenContentsFinderMenu,
        ClickLeaveButton,
        ConfirmLeave,
    }

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
        Plugin.DutyState.DutyStarted += OnDutyStarted;
    }

    public void Dispose()
    {
        CancelLeaveDutySequence("dispose");
        Plugin.DutyState.DutyCompleted -= OnDutyCompleted;
        Plugin.DutyState.DutyStarted -= OnDutyStarted;
    }

    private void OnDutyStarted(Dalamud.Game.DutyState.IDutyStateEventArgs args)
    {
        ResetOutsideDutyState();
        plugin.AdsIntegrationService.OnDutyStarted(args.TerritoryType.RowId);
        plugin.RaidModeService?.OnDutyStarted();
    }

    private void OnDutyCompleted(Dalamud.Game.DutyState.IDutyStateEventArgs args)
        => OnDutyCompleted(args.TerritoryType.RowId);

    private void OnDutyCompleted(uint territoryId)
    {
        plugin.AdsIntegrationService.OnDutyCompleted(territoryId);
        Plugin.Log.Information($"[ExitBehaviour] Duty completed in territory {territoryId}");
    }

    private void ResetOutsideDutyState()
    {
        var hadState = dutyCompleted
                       || dutyLeaveIssued
                       || leaveAttemptCount > 0
                       || exitTarget != null
                       || isNavigatingToExit
                       || leaveDutyStep != LeaveDutyStep.None;

        CancelLeaveDutySequence("not in duty");
        if (isNavigatingToExit)
            SendCommand("/vnav stop");

        leaveAttemptCount = 0;
        lastExitInteractTime = DateTime.MinValue;
        lastExitScanTime = DateTime.MinValue;
        lastLeaveAttemptTime = DateTime.MinValue;
        leaveConfirmationAttemptCount = 0;
        pendingLeaveReason = "";
        exitTarget = null;
        isNavigatingToExit = false;
        StateDetail = "";

        if (hadState)
            Plugin.Log.Debug("[ExitBehaviour] No longer in duty - reset exit state");
    }

    /// <summary>
    /// Called every framework tick. Evaluates exit rules and takes action if needed.
    /// </summary>
    public void Update()
    {
        if (plugin.RaidModeService?.IsEnabled == true)
        {
            CancelAutomaticExitForRaid();
            StateDetail = "Raid mode";
            return;
        }
        var config = plugin.ConfigManager.GetActiveConfig();
        var inDuty = Plugin.Condition[ConditionFlag.BoundByDuty] ||
                     Plugin.Condition[ConditionFlag.BoundByDuty56] ||
                     Plugin.Condition[ConditionFlag.BoundByDuty95];
        var now = DateTime.UtcNow;

        // Entry tracking must run even while ADS or a utility pauses exit checks.
        if (inDuty)
            DutySession.ObserveEntry(now);

        if (!config.Enabled)
        {
            StopForDisable();
            if (!inDuty && DutySession.EnteredAtUtc == DateTime.MinValue && !dutyCompleted)
                ResetOutsideDutyState();
            return;
        }

        if (!plugin.ConfigManager.IsQuestionableDutyFamilyControlled(config, Models.AdsDutyCategory.Solo) &&
            config.NormalizeExitMethodSelection())
        {
            Plugin.Log.Warning("[ExitBehaviour] Normalized mutually exclusive exit method settings");
            plugin.ConfigManager.SaveCurrentAccount();
        }

        var exits = plugin.ConfigManager.GetEffectiveDutyExitSettings(config);

        if (!inDuty)
        {
            if (DutySession.EnteredAtUtc == DateTime.MinValue && !dutyCompleted)
                ResetOutsideDutyState();
            return;
        }

        if (plugin.AutomationService.IsUtilityGateActive)
        {
            StateDetail = "ADS utility active";
            return;
        }

        if (plugin.AdsUtilityIpcService.ShouldSuppressGenericYesNo())
        {
            StateDetail = "ADS utility dialog active";
            return;
        }

        if (plugin.AdsIntegrationService.ShouldPauseExitSystem)
        {
            StateDetail = plugin.AdsIntegrationService.IsHandoffPending
                ? "ADS handoff pending"
                : "ADS active";
            return;
        }

        // Don't try to leave during loading screens
        if (Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51])
        {
            CancelLeaveDutySequence("between areas");
            return;
        }

        var partyExitReady = CanLeaveAfterPartyDeparture(DutySession, exits) && !Plugin.Condition[ConditionFlag.InCombat];
        if (pendingPartyDepartureExit && !partyExitReady)
            CancelLeaveDutySequence("party departure condition no longer met");
        TickLeaveDutySequence();

        // Don't try to leave during combat
        if (Plugin.Condition[ConditionFlag.InCombat])
            return;

        // Exit object feature removed - no longer needed

        if (dutyCompleted && (exits.UseAdsLeaveAfterAdsDuty || exits.ExitAfterDutyEnds))
        {
            var elapsed = (now - dutyCompletedTime).TotalSeconds;
            TryIssueCompletedExit(DutySession, config, now, new DutyExitConditions(
                inDuty,
                Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51],
                Plugin.Condition[ConditionFlag.InCombat],
                plugin.AutomationService.IsUtilityGateActive || plugin.AdsUtilityIpcService.ShouldSuppressGenericYesNo(),
                plugin.AdsIntegrationService.ShouldPauseExitSystem), useAds =>
            {
                Plugin.Log.Information($"[ExitBehaviour] Configured completion exit: elapsed={elapsed:F1}s >= configured={exits.ExitAfterDutySeconds}s, ADS={useAds}");
                if (useAds)
                {
                    Plugin.Log.Information("[ExitBehaviour] ADS exit method enabled - sending /ads leave once.");
                    SendCommand("/ads leave");
                }
                else
                    LeaveDuty();
            }, exits);

            StateDetail = dutyLeaveIssued
                ? "Duty exit requested; waiting to leave duty."
                : $"Duty completed, {(exits.UseAdsLeaveAfterAdsDuty ? "ADS " : "")}leaving in {Math.Max(0, exits.ExitAfterDutySeconds - elapsed):F0}s...";
            return;
        }

        // Only premature-exit checks need the entry grace while party members load.
        if (!dutyCompleted && DutySession.EnteredAtUtc != DateTime.MinValue)
        {
            var sinceDutyEntry = (now - DutySession.EnteredAtUtc).TotalSeconds;
            if (sinceDutyEntry < DutyGracePeriodSeconds)
            {
                StateDetail = $"Grace period ({DutyGracePeriodSeconds - sinceDutyEntry:F0}s)...";
                return;
            }
        }

        if (partyExitReady)
            LeaveDuty(partyDeparture: true);

        // CBT auto-leave feature removed
    }

    internal static bool CanLeaveAfterPartyDeparture(AdsDutySession session, DutyExitSettings exits)
        => exits.LeaveWhenAllLeft && session.IsCompleted && session.PartyExit.AllTeammatesDeparted;

    internal static bool TryIssueCompletedExit(AdsDutySession session, CharacterConfig config,
        DateTime nowUtc, DutyExitConditions conditions, Action<bool> issueExit, Models.DutyExitSettings? effectiveSettings = null)
    {
        var exits = effectiveSettings ?? Models.DutyExitSettings.FromConfig(config);
        if (!config.Enabled || !conditions.InDuty || conditions.IsBetweenAreas || conditions.InCombat
            || conditions.UtilityActive || conditions.AdsPaused || !session.IsCompleted || session.LeaveIssued
            || (!exits.UseAdsLeaveAfterAdsDuty && !exits.ExitAfterDutyEnds)
            || nowUtc - session.CompletedAtUtc < TimeSpan.FromSeconds(exits.ExitAfterDutySeconds))
            return false;

        session.LeaveIssued = true;
        issueExit(exits.UseAdsLeaveAfterAdsDuty);
        return true;
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

    internal static unsafe DutyPartyRoster? ReadPartyRoster(uint territoryId)
    {
        try
        {
            var local = Plugin.ObjectTable.LocalPlayer;
            var characterId = Plugin.PlayerState.ContentId;
            var party = Plugin.PartyList;
            var hud = AgentHUD.Instance();
            if (local == null || characterId == 0 || party.GroupManagerAddress == 0 || hud == null
                || Plugin.ClientState.TerritoryType != territoryId)
                return null;

            var count = party.Length;
            var partyId = party.PartyId;
            var hudCount = hud->PartyMemberCount;
            if (count is < 0 or > 8 || hudCount is < 0 or > 8)
                return null;

            var members = new Dictionary<ulong, DutyPartyMember>(count);
            foreach (var member in party)
            {
                var id = member.ContentId;
                if (id == 0 || id == characterId && member.EntityId != local.EntityId
                    || !members.TryAdd(id, new(member.Territory.RowId, member.GameObject?.ObjectKind == ObjectKind.Pc)))
                    return null;
            }

            var hudMembers = new HashSet<ulong>();
            for (var index = 0; index < hudCount; index++)
            {
                var member = hud->PartyMembers[index];
                if (member.ContentId == 0 || member.ContentId == characterId && member.EntityId != local.EntityId
                    || !hudMembers.Add(member.ContentId))
                    return null;
            }

            // An early local-only HUD can precede the matched roster. Commence and
            // a settled non-cross-realm party state are required to classify solo.
            var canConfirmSolo = count <= 1 && partyId == 0 && Plugin.DutyState.IsDutyStarted
                && !InfoProxyCrossRealm.IsCrossRealmParty();
            return members.Count == count && party.Length == count && party.PartyId == partyId
                && hud->PartyMemberCount == hudCount && Plugin.PlayerState.ContentId == characterId
                && Plugin.ClientState.TerritoryType == territoryId
                    ? new DutyPartyRoster(characterId, partyId, canConfirmSolo, members, hudMembers)
                    : null;
        }
        catch
        {
            return null;
        }
    }

    private void LeaveDuty(bool partyDeparture = false)
    {
        var now = DateTime.UtcNow;
        // Throttle leave attempts to every 5 seconds
        if ((now - lastLeaveAttemptTime).TotalSeconds < 5.0) return;
        lastLeaveAttemptTime = now;
        leaveAttemptCount++;

        // Determine why we're leaving
        var config = plugin.ConfigManager.GetActiveConfig();
        string leaveReason = "Unknown";
        
        if (partyDeparture)
        {
            leaveReason = "Duty completed and all entry teammates have departed";
        }
        else if (config.ExitAfterDutyEnds && dutyCompleted)
        {
            var elapsed = (DateTime.UtcNow - dutyCompletedTime).TotalSeconds;
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
        leaveConfirmationAttemptCount = 0;
        pendingPartyDepartureExit = partyDeparture;
        ScheduleLeaveDutyStep(LeaveDutyStep.OpenContentsFinderMenu, TimeSpan.FromMilliseconds(500), leaveReason);

        StateDetail = $"Leaving duty (attempt #{leaveAttemptCount}) - {leaveReason}";
    }

    private void TickLeaveDutySequence()
    {
        if (plugin.RaidModeService?.IsEnabled == true)
        {
            CancelAutomaticExitForRaid();
            return;
        }
        if (leaveDutyStep == LeaveDutyStep.None || DateTime.UtcNow < leaveDutyStepDueAt)
            return;

        var step = leaveDutyStep;
        leaveDutyStep = LeaveDutyStep.None;

        try
        {
            switch (step)
            {
                case LeaveDutyStep.OpenContentsFinderMenu:
                    TryClickLeaveDutyButton();
                    break;
                case LeaveDutyStep.ClickLeaveButton:
                    TryClickLeaveButton();
                    break;
                case LeaveDutyStep.ConfirmLeave:
                    HandleLeaveConfirmation();
                    break;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[ExitBehaviour] Leave duty sequence step {step} failed: {ex.Message}");
            CancelLeaveDutySequence("step exception");
        }
        if (leaveDutyStep == LeaveDutyStep.None)
            pendingPartyDepartureExit = false;
    }

    private void ScheduleLeaveDutyStep(LeaveDutyStep step, TimeSpan delay, string leaveReason)
    {
        leaveDutyStep = step;
        leaveDutyStepDueAt = DateTime.UtcNow + delay;
        pendingLeaveReason = leaveReason;
    }

    private void CancelLeaveDutySequence(string reason)
    {
        pendingPartyDepartureExit = false;
        if (leaveDutyStep == LeaveDutyStep.None)
            return;

        Plugin.Log.Debug($"[ExitBehaviour] Cancelling leave duty sequence: {reason}");
        leaveDutyStep = LeaveDutyStep.None;
        leaveDutyStepDueAt = DateTime.MinValue;
        pendingLeaveReason = "";
        leaveConfirmationAttemptCount = 0;
    }

    internal void StopForDisable() => CancelAutomaticExit("plugin disabled");

    internal void CancelAutomaticExitForRaid() => CancelAutomaticExit("Raid mode");

    private void CancelAutomaticExit(string reason)
    {
        CancelLeaveDutySequence(reason);
        if (isNavigatingToExit) SendCommand("/vnav stop");
        isNavigatingToExit = false;
        exitTarget = null;
    }

    private void TryClickLeaveDutyButton()
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

            ScheduleLeaveDutyStep(LeaveDutyStep.ClickLeaveButton, TimeSpan.FromMilliseconds(500), pendingLeaveReason);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[ExitBehaviour] Error trying to leave duty: {ex.Message}");
        }
    }

    private void TryClickLeaveButton()
    {
        try
        {
            // Click Leave button using xa docs pattern: ClickAddonButton("ContentsFinderMenu", 43)
            Plugin.Log.Information("[ExitBehaviour] Clicking Leave button on ContentsFinderMenu");
            GameHelpers.FireAddonCallback("ContentsFinderMenu", true, 43);
            ScheduleLeaveDutyStep(LeaveDutyStep.ConfirmLeave, TimeSpan.FromMilliseconds(500), pendingLeaveReason);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[ExitBehaviour] Error clicking Leave button: {ex.Message}");
            ScheduleLeaveDutyStep(LeaveDutyStep.ConfirmLeave, TimeSpan.FromMilliseconds(500), pendingLeaveReason);
        }
    }

    private void HandleLeaveConfirmation()
    {
        try
        {
            // Click Yes on SelectYesno confirmation dialog
            Plugin.Log.Information("[ExitBehaviour] Clicking Yes on leave confirmation dialog");
            leaveConfirmationAttemptCount++;
            if (GameHelpers.ClickYesIfVisible())
            {
                Plugin.Log.Information("[ExitBehaviour] Successfully clicked Yes on leave duty confirmation");
                return;
            }

            if (leaveConfirmationAttemptCount < MaxLeaveConfirmationAttempts)
            {
                ScheduleLeaveDutyStep(LeaveDutyStep.ConfirmLeave, TimeSpan.FromMilliseconds(500), pendingLeaveReason);
                return;
            }

            Plugin.Log.Debug("[ExitBehaviour] Leave confirmation was not visible after retry window");
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
