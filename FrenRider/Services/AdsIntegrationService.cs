using System;
using Dalamud.Game.ClientState.Conditions;
using FrenRider.Models;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace FrenRider.Services;

public sealed class AdsIntegrationService
{
    private const uint PraetoriumTerritoryTypeId = 1044;
    private const float PraetoriumTimeLimitSeconds = 7200f;
    private const double PraetoriumReadyFallbackSeconds = 15.0;

    private readonly AdsDutyIpcService adsDutyIpcService;
    private readonly Func<CharacterConfig> getConfig;
    private readonly Action resetRecovery;
    private readonly Func<string, bool> processCommand;
    private readonly Action<string> logInformation;
    private readonly Action<string> logWarning;

    private DateTime dutyEnteredUtc = DateTime.MinValue;
    private DateTime lastPraetoriumReadyWaitLogUtc = DateTime.MinValue;
    private DateTime handoffRequestedAtUtc = DateTime.MinValue;
    private DateTime nextHandoffAttemptUtc = DateTime.MinValue;
    private readonly AdsHandoffState handoffState = new();
    private uint trackedDutyTerritoryId;
    private uint trackedDutyContentFinderConditionId;
    private bool trackedInDuty;
    private bool runtimeOwnedLastUpdate;
    private bool ownershipReleasedForCurrentDuty;

    internal AdsDutySession DutySession { get; } = new();

    public AdsIntegrationService(Plugin plugin, AdsDutyIpcService adsDutyIpcService)
        : this(adsDutyIpcService, () => plugin.ConfigManager.GetActiveConfig(),
            () => plugin.BossModActionTweaksService.ResetRecovery(),
            command => Plugin.CommandManager.ProcessCommand(command),
            message => Plugin.Log.Information(message), message => Plugin.Log.Warning(message))
    {
    }

    internal AdsIntegrationService(AdsDutyIpcService adsDutyIpcService, Func<CharacterConfig> getConfig,
        Action resetRecovery, Func<string, bool> processCommand,
        Action<string> logInformation, Action<string> logWarning)
    {
        this.adsDutyIpcService = adsDutyIpcService;
        this.getConfig = getConfig;
        this.resetRecovery = resetRecovery;
        this.processCommand = processCommand;
        this.logInformation = logInformation;
        this.logWarning = logWarning;
        StatusText = "ADS handoff off; FrenRider local duty logic active.";
    }

    public bool AdsLoaded { get; private set; }
    public bool IsHandoffPending { get; private set; }
    public bool IsControllingDuty { get; private set; }
    public bool HadAdsControlThisDuty => DutySession.HadAdsControl;
    public bool RuntimeOwnershipReadable { get; private set; }
    public string RuntimeOwnershipSource { get; private set; } = AdsDutyOwnershipSource.None.ToString();
    public bool ExitTakeoverActive => DutySession.ExitTakeoverActive;
    internal bool IsSoloCombatHeld => handoffState.IsCombatHeld;
    public bool ShouldPauseDutySystems
        => AdsIntegrationPolicy.ShouldPauseDutySystems(IsHandoffPending, IsControllingDuty, ExitTakeoverActive);
    public bool ShouldPauseExitSystem
        => AdsIntegrationPolicy.ShouldPauseExitSystem(IsHandoffPending, IsControllingDuty, ExitTakeoverActive);
    public string StatusText { get; private set; }

    internal void ResetHandoff()
    {
        IsHandoffPending = false;
        handoffRequestedAtUtc = DateTime.MinValue;
        nextHandoffAttemptUtc = DateTime.MinValue;
        handoffState.Reset();
    }

    internal void ObserveHandoffReadiness()
        => ObserveHandoffReadiness(IsInDuty(), ReadLiveDutyIdentity(), ReadReadinessConditions(), DateTime.UtcNow);

    internal void ObserveHandoffReadiness(bool inDuty,
        (uint TerritoryTypeId, uint ContentFinderConditionId) identity,
        AdsHandoffReadinessConditions conditions, DateTime nowUtc)
    {
        ObserveDutySession(inDuty, identity, conditions, nowUtc);
        if (!inDuty)
            trackedInDuty = false;

        if (!getConfig().Enabled || !inDuty || DutySession.IsCompleted)
        {
            ResetHandoff();
            return;
        }

        handoffState.ObserveReadiness(conditions);
        if (IsSoloCombatHeld && AdsIntegrationPolicy.GetHandoffReadinessBlocker(conditions) is { } blocker)
            StatusText = $"ADS solo combat held; {blocker}.";
    }

    private void ObserveDutySession(
        bool inDuty,
        (uint TerritoryTypeId, uint ContentFinderConditionId) identity,
        AdsHandoffReadinessConditions conditions, DateTime nowUtc)
    {
        if (DutySession.Observe(inDuty, identity.TerritoryTypeId, identity.ContentFinderConditionId,
                conditions, nowUtc))
            ResetDutyTracking();
    }

    internal void OnDutyStarted(uint territoryId)
        => OnDutyStarted(territoryId, DateTime.UtcNow);

    internal void OnDutyStarted(uint territoryId, DateTime nowUtc)
    {
        DutySession.Start(territoryId, nowUtc);
        ResetDutyTracking();
    }

    internal void OnDutyCompleted(uint territoryId)
        => OnDutyCompleted(territoryId, DateTime.UtcNow);

    internal void OnDutyCompleted(uint territoryId, DateTime nowUtc)
    {
        DutySession.Complete(territoryId, nowUtc);
        ResetHandoff();
        ReleaseDutyControlForExit($"DutyCompleted territory {territoryId}");
    }

    private void ResetDutyTracking()
    {
        trackedInDuty = false;
        trackedDutyTerritoryId = 0;
        trackedDutyContentFinderConditionId = 0;
        dutyEnteredUtc = DateTime.MinValue;
        ownershipReleasedForCurrentDuty = false;
        runtimeOwnedLastUpdate = false;
        IsControllingDuty = false;
        lastPraetoriumReadyWaitLogUtc = DateTime.MinValue;
        ResetHandoff();
    }

    private static bool IsInDuty()
        => Plugin.Condition[ConditionFlag.BoundByDuty]
            || Plugin.Condition[ConditionFlag.BoundByDuty56]
            || Plugin.Condition[ConditionFlag.BoundByDuty95];

    private static AdsHandoffReadinessConditions ReadReadinessConditions()
    {
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        return new AdsHandoffReadinessConditions(
            Plugin.ClientState.IsLoggedIn,
            localPlayer is not null,
            localPlayer?.CurrentHp > 0,
            Plugin.Condition[ConditionFlag.Unconscious],
            Plugin.Condition[ConditionFlag.BetweenAreas],
            Plugin.Condition[ConditionFlag.WatchingCutscene],
            Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent],
            Plugin.Condition[ConditionFlag.WatchingCutscene78],
            Plugin.Condition[ConditionFlag.BetweenAreas51]);
    }

    public void Update() => Update(forceOwnershipRefresh: false);

    internal void Update(bool forceOwnershipRefresh)
        => Update(IsInDuty(), ReadLiveDutyIdentity(), ReadReadinessConditions(), DateTime.UtcNow, forceOwnershipRefresh);

    internal void Update(bool inDuty,
        (uint TerritoryTypeId, uint ContentFinderConditionId) liveDutyIdentity,
        AdsHandoffReadinessConditions readinessConditions, DateTime now, bool forceOwnershipRefresh = false)
    {
        var config = getConfig();
        var territoryTypeId = liveDutyIdentity.TerritoryTypeId;
        ObserveDutySession(inDuty, liveDutyIdentity, readinessConditions, now);

        var ownership = adsDutyIpcService.Refresh(
            inDuty,
            liveDutyIdentity.TerritoryTypeId,
            liveDutyIdentity.ContentFinderConditionId,
            force: forceOwnershipRefresh);
        AdsLoaded = ownership.AdsLoaded;
        RuntimeOwnershipReadable = ownership.StatusReadable;
        RuntimeOwnershipSource = ownership.Source.ToString();

        if (inDuty != trackedInDuty
            || territoryTypeId != trackedDutyTerritoryId
            || liveDutyIdentity.ContentFinderConditionId != trackedDutyContentFinderConditionId
            || !inDuty)
        {
            trackedInDuty = inDuty;
            trackedDutyTerritoryId = territoryTypeId;
            trackedDutyContentFinderConditionId = liveDutyIdentity.ContentFinderConditionId;
            dutyEnteredUtc = inDuty ? now : DateTime.MinValue;
            handoffRequestedAtUtc = DateTime.MinValue;
            nextHandoffAttemptUtc = DateTime.MinValue;
            IsControllingDuty = false;
            IsHandoffPending = false;
            lastPraetoriumReadyWaitLogUtc = DateTime.MinValue;
            handoffState.Reset();
        }

        IsControllingDuty = inDuty && readinessConditions.IsLoggedIn && ownership.IsOwned;
        if (IsControllingDuty)
        {
            DutySession.ObserveAdsControl();
            IsHandoffPending = false;
            handoffRequestedAtUtc = DateTime.MinValue;
            nextHandoffAttemptUtc = DateTime.MinValue;
        }
        else if (runtimeOwnedLastUpdate && !ownership.IsOwned && ownership.StatusReadable)
        {
            ownershipReleasedForCurrentDuty = true;
            ResetHandoff();
            logInformation("[FrenRider][ADS] ADS explicitly released runtime duty ownership.");
        }

        runtimeOwnedLastUpdate = IsControllingDuty;

        if (config == null || !config.Enabled || !inDuty)
            ResetHandoff();

        // Completion belongs to the duty session, not the current ADS owner or
        // the transient GameMain identity. Never restart a completed duty.
        if (DutySession.IsCompleted)
        {
            ResetHandoff();
            ReleaseDutyControlForExit("duty completed");
            StatusText = ExitTakeoverActive
                ? "Duty completed; configured FrenRider exit takeover active."
                : "Duty completed; automatic ADS handoff stopped.";
            return;
        }

        // Manual ownership keeps its existing behavior. An automatic solo
        // attempt must finish its safe delay even if ADS confirms early.
        if (IsControllingDuty && !IsSoloCombatHeld)
        {
            StatusText = ExitTakeoverActive
                ? $"ADS runtime ownership active via {RuntimeOwnershipSource}; FrenRider exit takeover active."
                : $"ADS runtime ownership active via {RuntimeOwnershipSource}; FrenRider duty systems paused.";
            return;
        }

        if (config == null || !config.Enabled)
        {
            StatusText = AdsLoaded ? "FrenRider disabled." : "ADS not loaded.";
            return;
        }

        if (!inDuty)
        {
            StatusText = AdsLoaded
                ? "ADS loaded; waiting for duty. FrenRider local logic active."
                : "ADS not loaded. FrenRider local logic active.";
            return;
        }

        if (ExitTakeoverActive)
        {
            ResetHandoff();
            StatusText = $"ADS released duty progression; configured FrenRider exit takeover active via {RuntimeOwnershipSource}.";
            return;
        }

        if (ownershipReleasedForCurrentDuty)
        {
            ResetHandoff();
            StatusText = $"ADS released duty ownership via {RuntimeOwnershipSource}; FrenRider local duty logic active.";
            return;
        }

        var readiness = ResolveReadiness(
            config,
            liveDutyIdentity.TerritoryTypeId,
            liveDutyIdentity.ContentFinderConditionId);
        IsHandoffPending = readiness.CanUseAds && !ownership.IsOwned;
        if (IsHandoffPending)
            resetRecovery();

        if (!readiness.CanUseAds)
        {
            ResetHandoff();
            StatusText = BuildReadinessStatus(readiness, readiness.Reason);
            return;
        }

        if (!ownership.IsOwned
            && handoffRequestedAtUtc != DateTime.MinValue
            && !AdsIntegrationPolicy.IsHandoffConfirmationPending(handoffRequestedAtUtc, now))
        {
            handoffRequestedAtUtc = DateTime.MinValue;
            handoffState.ResetCountdown();
        }

        var countdown = handoffState.Update(
            liveDutyIdentity.TerritoryTypeId,
            liveDutyIdentity.ContentFinderConditionId,
            now,
            readiness.FamilySettings.HandoffDelaySeconds,
            readinessConditions,
            automaticSoloHandoff: readiness.Entry?.Category == AdsDutyCategory.Solo,
            ownershipConfirmed: ownership.IsOwned && ownership.StatusReadable);

        if (countdown.Blocker is not null)
        {
            StatusText = BuildReadinessStatus(readiness, countdown.Blocker);
            return;
        }

        if (!countdown.IsReady)
        {
            StatusText = BuildReadinessStatus(
                readiness,
                $"handoff in {Math.Max(0, countdown.Remaining.TotalSeconds):F1}s of continuous readiness");
            return;
        }

        if (ownership.IsOwned)
        {
            StatusText = IsSoloCombatHeld
                ? "ADS solo combat held; waiting for readable ownership confirmation."
                : $"ADS runtime ownership confirmed via {RuntimeOwnershipSource}; solo combat delay complete.";
            return;
        }

        if (!IsReadyToStartAdsInsideDuty(territoryTypeId, now))
        {
            StatusText = BuildReadinessStatus(readiness, "waiting for duty start seam");
            return;
        }

        if (AdsIntegrationPolicy.IsHandoffConfirmationPending(handoffRequestedAtUtc, now))
        {
            var remaining = AdsIntegrationPolicy.HandoffConfirmationTimeout - (now - handoffRequestedAtUtc);
            StatusText = BuildReadinessStatus(readiness, $"waiting {Math.Max(0, remaining.TotalSeconds):F1}s for ADS ownership confirmation");
            return;
        }

        if (!AdsIntegrationPolicy.CanAttemptHandoff(handoffRequestedAtUtc, nextHandoffAttemptUtc, now))
        {
            StatusText = BuildReadinessStatus(readiness, $"handoff retry backoff until {nextHandoffAttemptUtc:HH:mm:ss}");
            return;
        }

        handoffRequestedAtUtc = DateTime.MinValue;
        var request = adsDutyIpcService.RequestStartDutyFromInside();
        if (request.EndpointAvailable)
        {
            if (request.Accepted)
            {
                AwaitHandoffConfirmation(now, readiness, "ADS.StartDutyFromInside accepted");
                return;
            }

            BackoffFailedHandoff(now, readiness, "ADS.StartDutyFromInside rejected; command fallback suppressed");
            return;
        }

        if (processCommand("/ads inside"))
        {
            AwaitHandoffConfirmation(now, readiness, "typed endpoint unavailable; sent /ads inside fallback");
            return;
        }

        BackoffFailedHandoff(now, readiness, "typed endpoint unavailable and /ads inside fallback failed");
    }

    public void ReleaseDutyControlForExit(string reason)
    {
        var config = getConfig();
        var configuredExit = config.UseAdsLeaveAfterAdsDuty || config.ExitAfterDutyEnds || config.LeaveWhenAllLeft;
        if (!DutySession.TryTakeOverExit(configuredExit))
            return;

        ResetHandoff();
        StatusText = $"ADS duty progression paused; configured FrenRider exit takeover active ({reason}).";
        logInformation($"[FrenRider][ADS] Enabled exit-only takeover while keeping FrenRider duty systems paused: {reason}");
    }

    internal AdsDutyCategory? GetCurrentDutyCategory()
    {
        var liveDutyIdentity = ReadLiveDutyIdentity();
        var snapshot = adsDutyIpcService.CurrentDuty;
        return snapshot?.MatchesIdentity(
            liveDutyIdentity.TerritoryTypeId,
            liveDutyIdentity.ContentFinderConditionId) == true
                ? snapshot.Category
                : null;
    }

    private void AwaitHandoffConfirmation(DateTime now, AdsDutyReadiness readiness, string reason)
    {
        handoffRequestedAtUtc = now;
        nextHandoffAttemptUtc = now + AdsIntegrationPolicy.HandoffConfirmationTimeout;
        StatusText = BuildReadinessStatus(readiness, $"{reason}; waiting for authoritative ownership");
        logInformation(
            $"[FrenRider][ADS] {reason} for {readiness.Entry!.DutyName} ({AdsDutyCategoryCatalog.GetLabel(readiness.Entry.Category)}) with ADS clearance {readiness.Entry.ClearanceStatus} (M{readiness.Entry.ClearanceLevel}), support {readiness.Entry.SupportLevel}, threshold {readiness.FamilySettings.MaturityThreshold}, and {readiness.FamilySettings.HandoffDelaySeconds}s continuous-ready delay.");
    }

    private void BackoffFailedHandoff(DateTime now, AdsDutyReadiness readiness, string reason)
    {
        handoffRequestedAtUtc = DateTime.MinValue;
        nextHandoffAttemptUtc = now + AdsIntegrationPolicy.HandoffConfirmationTimeout;
        handoffState.ResetCountdown();
        StatusText = BuildReadinessStatus(readiness, $"{reason}; restarting readiness delay with 5s retry backoff");
        logWarning($"[FrenRider][ADS] {reason}.");
    }

    private AdsDutyReadiness ResolveReadiness(
        CharacterConfig config,
        uint territoryTypeId,
        uint contentFinderConditionId)
    {
        if (!AdsLoaded)
            return new AdsDutyReadiness(null, default, false, "ADS is not loaded");

        var entry = adsDutyIpcService.CurrentDuty;
        if (entry is null)
            return new AdsDutyReadiness(null, default, false, $"{adsDutyIpcService.CurrentDutyDetail}; FrenRider local duty logic stays active");

        if (!entry.MatchesIdentity(territoryTypeId, contentFinderConditionId))
        {
            return new AdsDutyReadiness(
                null,
                default,
                false,
                $"ADS current-duty identity does not match live GameMain territory/CFC {territoryTypeId}/{contentFinderConditionId}; FrenRider local duty logic stays active");
        }

        var familySettings = config.GetAdsDutyFamilySettings(entry.Category);
        if (!familySettings.Enabled)
            return new AdsDutyReadiness(entry, familySettings, false, $"{AdsDutyCategoryCatalog.GetLabel(entry.Category)} handoff is off; FrenRider local duty logic stays active");

        if (!IsSnapshotReady(config, entry))
        {
            return new AdsDutyReadiness(
                entry,
                familySettings,
                false,
                $"{entry.DutyName} has ADS clearance {entry.ClearanceStatus} (M{entry.ClearanceLevel}), below threshold {familySettings.MaturityThreshold}; FrenRider local duty logic stays active");
        }

        return new AdsDutyReadiness(entry, familySettings, true, "ready");
    }

    private static string BuildReadinessStatus(AdsDutyReadiness readiness, string trailingStatus)
    {
        if (readiness.Entry is null)
            return readiness.CanUseAds
                ? "ADS loaded; ready."
                : $"ADS loaded; {trailingStatus}.";

        var categoryLabel = AdsDutyCategoryCatalog.GetLabel(readiness.Entry.Category);
        return $"{categoryLabel} {readiness.Entry.DutyName}: M{readiness.Entry.ClearanceLevel}/T{readiness.FamilySettings.MaturityThreshold}, {trailingStatus}.";
    }

    private bool IsReadyToStartAdsInsideDuty(uint territoryTypeId, DateTime now)
    {
        var secondsSinceEnter = dutyEnteredUtc == DateTime.MinValue
            ? double.MaxValue
            : (now - dutyEnteredUtc).TotalSeconds;

        if (territoryTypeId != PraetoriumTerritoryTypeId)
            return true;

        var remainingTime = GameHelpers.GetDutyRemainingTime();
        if (remainingTime > 0f && remainingTime < PraetoriumTimeLimitSeconds)
            return true;

        if (remainingTime > 0f)
        {
            if ((now - lastPraetoriumReadyWaitLogUtc).TotalSeconds >= 5.0)
            {
                lastPraetoriumReadyWaitLogUtc = now;
                logInformation($"[FrenRider][ADS] Praetorium entered but timer is still at {remainingTime:F0}s; waiting before sending /ads inside.");
            }

            return false;
        }

        if (secondsSinceEnter < PraetoriumReadyFallbackSeconds)
            return false;

        if ((now - lastPraetoriumReadyWaitLogUtc).TotalSeconds >= 5.0)
        {
            lastPraetoriumReadyWaitLogUtc = now;
            logWarning("[FrenRider][ADS] Praetorium timer never appeared; using fallback readiness window before /ads inside.");
        }

        return true;
    }

    internal static bool IsSnapshotReady(CharacterConfig config, AdsCurrentDutySnapshot snapshot)
    {
        var settings = config.GetAdsDutyFamilySettings(snapshot.Category);
        return settings.Enabled && snapshot.ClearanceLevel >= settings.MaturityThreshold;
    }

    internal static unsafe (uint TerritoryTypeId, uint ContentFinderConditionId) ReadLiveDutyIdentity()
    {
        try
        {
            var gameMain = GameMain.Instance();
            return gameMain is null
                ? (0, 0)
                : (gameMain->CurrentTerritoryTypeId, gameMain->CurrentContentFinderConditionId);
        }
        catch
        {
            return (0, 0);
        }
    }

    private sealed record AdsDutyReadiness(
        AdsCurrentDutySnapshot? Entry,
        AdsDutyFamilySettings FamilySettings,
        bool CanUseAds,
        string Reason);
}

// Shared by ADS handoff and configured exit so dispatch cannot erase completion.
internal sealed class AdsDutySession
{
    private uint dutyTerritoryId;

    public DateTime EnteredAtUtc { get; private set; } = DateTime.MinValue;
    public DateTime CompletedAtUtc { get; private set; } = DateTime.MinValue;
    public bool IsCompleted => CompletedAtUtc != DateTime.MinValue;
    public bool HadAdsControl { get; private set; }
    public bool ExitTakeoverActive { get; private set; }
    public bool LeaveIssued { get; set; }

    public bool Observe(bool inDuty, uint territoryId, uint contentFinderConditionId,
        AdsHandoffReadinessConditions conditions, DateTime nowUtc)
    {
        // A dropped duty flag or missing identity during a cutscene is not an exit.
        var confirmedExit = !inDuty && territoryId != 0 && territoryId != dutyTerritoryId
                            && contentFinderConditionId == 0
                            && AdsIntegrationPolicy.GetHandoffReadinessBlocker(conditions) is null;
        if (!conditions.IsLoggedIn || confirmedExit)
        {
            var hadSession = EnteredAtUtc != DateTime.MinValue || IsCompleted;
            Reset();
            return hadSession;
        }

        if (inDuty)
        {
            ObserveEntry(nowUtc);
            if (dutyTerritoryId == 0)
                dutyTerritoryId = territoryId;
        }

        return false;
    }

    public void ObserveEntry(DateTime nowUtc)
    {
        if (EnteredAtUtc == DateTime.MinValue)
            EnteredAtUtc = nowUtc;
    }

    public void Start(uint territoryId, DateTime nowUtc)
    {
        Reset();
        dutyTerritoryId = territoryId;
        EnteredAtUtc = nowUtc;
    }

    public void Complete(uint territoryId, DateTime nowUtc)
    {
        if (IsCompleted)
            return;

        dutyTerritoryId = territoryId;
        CompletedAtUtc = nowUtc;
    }

    public void ObserveAdsControl() => HadAdsControl = true;

    public bool TryTakeOverExit(bool configuredExit)
    {
        if (!configuredExit || !HadAdsControl || ExitTakeoverActive)
            return false;

        ExitTakeoverActive = true;
        return true;
    }

    private void Reset()
    {
        dutyTerritoryId = 0;
        EnteredAtUtc = DateTime.MinValue;
        CompletedAtUtc = DateTime.MinValue;
        HadAdsControl = false;
        ExitTakeoverActive = false;
        LeaveIssued = false;
    }
}
