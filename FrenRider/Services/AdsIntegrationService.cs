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
    private const double GenericDutyReadyDelaySeconds = 2.0;

    private readonly Plugin plugin;
    private readonly ZoneService zoneService;
    private readonly AdsDutyIpcService adsDutyIpcService;

    private DateTime dutyEnteredUtc = DateTime.MinValue;
    private DateTime lastPraetoriumReadyWaitLogUtc = DateTime.MinValue;
    private DateTime handoffRequestedAtUtc = DateTime.MinValue;
    private DateTime nextHandoffAttemptUtc = DateTime.MinValue;
    private uint trackedDutyTerritoryId;
    private uint trackedDutyContentFinderConditionId;
    private bool runtimeOwnedLastUpdate;
    private bool ownershipReleasedForCurrentDuty;

    public AdsIntegrationService(Plugin plugin, ZoneService zoneService, AdsDutyIpcService adsDutyIpcService)
    {
        this.plugin = plugin;
        this.zoneService = zoneService;
        this.adsDutyIpcService = adsDutyIpcService;
        StatusText = "ADS handoff off; FrenRider local duty logic active.";
    }

    public bool AdsLoaded { get; private set; }
    public bool IsHandoffPending { get; private set; }
    public bool IsControllingDuty { get; private set; }
    public bool HadAdsControlThisDuty { get; private set; }
    public bool RuntimeOwnershipReadable { get; private set; }
    public string RuntimeOwnershipSource { get; private set; } = AdsDutyOwnershipSource.None.ToString();
    public bool ExitTakeoverActive { get; private set; }
    public bool ShouldPauseDutySystems
        => AdsIntegrationPolicy.ShouldPauseDutySystems(IsHandoffPending, IsControllingDuty, ExitTakeoverActive);
    public bool ShouldPauseExitSystem
        => AdsIntegrationPolicy.ShouldPauseExitSystem(IsHandoffPending, IsControllingDuty, ExitTakeoverActive);
    public string StatusText { get; private set; }

    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var inDuty = Plugin.Condition[ConditionFlag.BoundByDuty]
            || Plugin.Condition[ConditionFlag.BoundByDuty56]
            || Plugin.Condition[ConditionFlag.BoundByDuty95];
        var territoryTypeId = zoneService.TerritoryId;
        var liveDutyIdentity = ReadLiveDutyIdentity();

        var ownership = adsDutyIpcService.Refresh(
            inDuty,
            liveDutyIdentity.TerritoryTypeId,
            liveDutyIdentity.ContentFinderConditionId);
        AdsLoaded = ownership.AdsLoaded;
        RuntimeOwnershipReadable = ownership.StatusReadable;
        RuntimeOwnershipSource = ownership.Source.ToString();

        if (territoryTypeId != trackedDutyTerritoryId
            || liveDutyIdentity.ContentFinderConditionId != trackedDutyContentFinderConditionId
            || !inDuty)
        {
            trackedDutyTerritoryId = territoryTypeId;
            trackedDutyContentFinderConditionId = liveDutyIdentity.ContentFinderConditionId;
            dutyEnteredUtc = inDuty ? DateTime.UtcNow : DateTime.MinValue;
            handoffRequestedAtUtc = DateTime.MinValue;
            nextHandoffAttemptUtc = DateTime.MinValue;
            ExitTakeoverActive = false;
            HadAdsControlThisDuty = false;
            ownershipReleasedForCurrentDuty = false;
            IsControllingDuty = false;
            IsHandoffPending = false;
            runtimeOwnedLastUpdate = false;
            lastPraetoriumReadyWaitLogUtc = DateTime.MinValue;
        }

        IsControllingDuty = ownership.IsOwned;
        if (ownership.IsOwned)
        {
            HadAdsControlThisDuty = true;
            IsHandoffPending = false;
            handoffRequestedAtUtc = DateTime.MinValue;
            nextHandoffAttemptUtc = DateTime.MinValue;
        }
        else if (runtimeOwnedLastUpdate && ownership.StatusReadable)
        {
            ownershipReleasedForCurrentDuty = true;
            IsHandoffPending = false;
            handoffRequestedAtUtc = DateTime.MinValue;
            nextHandoffAttemptUtc = DateTime.MinValue;
            Plugin.Log.Information("[FrenRider][ADS] ADS explicitly released runtime duty ownership; FrenRider local duty logic may resume.");
        }

        runtimeOwnedLastUpdate = ownership.IsOwned;

        if (ownership.IsOwned)
        {
            StatusText = ExitTakeoverActive
                ? $"ADS runtime ownership active via {RuntimeOwnershipSource}; FrenRider exit takeover active."
                : $"ADS runtime ownership active via {RuntimeOwnershipSource}; FrenRider duty systems paused.";
            return;
        }

        if (config == null || !config.Enabled)
        {
            IsHandoffPending = false;
            StatusText = AdsLoaded ? "FrenRider disabled." : "ADS not loaded.";
            return;
        }

        if (!inDuty)
        {
            IsHandoffPending = false;
            StatusText = AdsLoaded
                ? "ADS loaded; waiting for duty. FrenRider local logic active."
                : "ADS not loaded. FrenRider local logic active.";
            return;
        }

        if (ExitTakeoverActive)
        {
            IsHandoffPending = false;
            StatusText = $"ADS released duty progression; configured FrenRider exit takeover active via {RuntimeOwnershipSource}.";
            return;
        }

        if (ownershipReleasedForCurrentDuty)
        {
            IsHandoffPending = false;
            StatusText = $"ADS released duty ownership via {RuntimeOwnershipSource}; FrenRider local duty logic active.";
            return;
        }

        var readiness = ResolveReadiness(
            config,
            liveDutyIdentity.TerritoryTypeId,
            liveDutyIdentity.ContentFinderConditionId);
        IsHandoffPending = readiness.CanUseAds;

        if (!readiness.CanUseAds)
        {
            StatusText = BuildReadinessStatus(readiness, readiness.Reason);
            return;
        }

        if (!IsReadyToStartAdsInsideDuty(territoryTypeId))
        {
            StatusText = BuildReadinessStatus(readiness, "waiting for duty start seam");
            return;
        }

        var now = DateTime.UtcNow;
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

        if (Plugin.CommandManager.ProcessCommand("/ads inside"))
        {
            AwaitHandoffConfirmation(now, readiness, "typed endpoint unavailable; sent /ads inside fallback");
            return;
        }

        BackoffFailedHandoff(now, readiness, "typed endpoint unavailable and /ads inside fallback failed");
    }

    public void ReleaseDutyControlForExit(string reason)
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var configuredExit = config.UseAdsLeaveAfterAdsDuty || config.ExitAfterDutyEnds || config.LeaveWhenAllLeft;
        if (!configuredExit || !HadAdsControlThisDuty)
            return;

        ExitTakeoverActive = true;
        IsHandoffPending = false;
        handoffRequestedAtUtc = DateTime.MinValue;
        nextHandoffAttemptUtc = DateTime.MinValue;
        StatusText = $"ADS duty progression paused; configured FrenRider exit takeover active ({reason}).";
        Plugin.Log.Information($"[FrenRider][ADS] Enabled exit-only takeover while keeping FrenRider duty systems paused: {reason}");
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
        Plugin.Log.Information(
            $"[FrenRider][ADS] {reason} for {readiness.Entry!.DutyName} ({AdsDutyCategoryCatalog.GetLabel(readiness.Entry.Category)}) with ADS clearance {readiness.Entry.ClearanceStatus} (M{readiness.Entry.ClearanceLevel}), support {readiness.Entry.SupportLevel}, and threshold {readiness.FamilySettings.MaturityThreshold}.");
    }

    private void BackoffFailedHandoff(DateTime now, AdsDutyReadiness readiness, string reason)
    {
        handoffRequestedAtUtc = DateTime.MinValue;
        nextHandoffAttemptUtc = now + AdsIntegrationPolicy.HandoffConfirmationTimeout;
        StatusText = BuildReadinessStatus(readiness, $"{reason}; retrying after 5s");
        Plugin.Log.Warning($"[FrenRider][ADS] {reason}.");
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

    private bool IsReadyToStartAdsInsideDuty(uint territoryTypeId)
    {
        if (Plugin.Condition[ConditionFlag.BetweenAreas]
            || Plugin.Condition[ConditionFlag.BetweenAreas51])
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var secondsSinceEnter = dutyEnteredUtc == DateTime.MinValue
            ? double.MaxValue
            : (now - dutyEnteredUtc).TotalSeconds;

        if (territoryTypeId != PraetoriumTerritoryTypeId)
            return secondsSinceEnter >= GenericDutyReadyDelaySeconds;

        var remainingTime = GameHelpers.GetDutyRemainingTime();
        if (remainingTime > 0f && remainingTime < PraetoriumTimeLimitSeconds)
            return true;

        if (remainingTime > 0f)
        {
            if ((now - lastPraetoriumReadyWaitLogUtc).TotalSeconds >= 5.0)
            {
                lastPraetoriumReadyWaitLogUtc = now;
                Plugin.Log.Information($"[FrenRider][ADS] Praetorium entered but timer is still at {remainingTime:F0}s; waiting before sending /ads inside.");
            }

            return false;
        }

        if (secondsSinceEnter < PraetoriumReadyFallbackSeconds)
            return false;

        if ((now - lastPraetoriumReadyWaitLogUtc).TotalSeconds >= 5.0)
        {
            lastPraetoriumReadyWaitLogUtc = now;
            Plugin.Log.Warning("[FrenRider][ADS] Praetorium timer never appeared; using fallback readiness window before /ads inside.");
        }

        return true;
    }

    internal static bool IsSnapshotReady(CharacterConfig config, AdsCurrentDutySnapshot snapshot)
    {
        var settings = config.GetAdsDutyFamilySettings(snapshot.Category);
        return settings.Enabled && snapshot.ClearanceLevel >= settings.MaturityThreshold;
    }

    private static unsafe (uint TerritoryTypeId, uint ContentFinderConditionId) ReadLiveDutyIdentity()
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
