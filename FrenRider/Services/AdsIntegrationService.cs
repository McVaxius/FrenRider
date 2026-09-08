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

    private readonly Plugin plugin;
    private readonly AdsDutyIpcService adsDutyIpcService;

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

    public AdsIntegrationService(Plugin plugin, AdsDutyIpcService adsDutyIpcService)
    {
        this.plugin = plugin;
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
    {
        var inDuty = IsInDuty();
        if (!inDuty)
            trackedInDuty = false;

        if (!plugin.ConfigManager.GetActiveConfig().Enabled || !inDuty)
        {
            ResetHandoff();
            return;
        }

        var conditions = ReadReadinessConditions();
        handoffState.ObserveReadiness(conditions);
        if (IsSoloCombatHeld && AdsIntegrationPolicy.GetHandoffReadinessBlocker(conditions) is { } blocker)
            StatusText = $"ADS solo combat held; {blocker}.";
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
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var inDuty = IsInDuty();
        var liveDutyIdentity = ReadLiveDutyIdentity();
        var territoryTypeId = liveDutyIdentity.TerritoryTypeId;

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
            handoffState.Reset();
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
            ResetHandoff();
            Plugin.Log.Information("[FrenRider][ADS] ADS explicitly released runtime duty ownership; FrenRider local duty logic may resume.");
        }

        runtimeOwnedLastUpdate = ownership.IsOwned;

        if (config == null || !config.Enabled || !inDuty)
            ResetHandoff();

        // Manual ownership keeps its existing behavior. An automatic solo
        // attempt must finish its safe delay even if ADS confirms early.
        if (ownership.IsOwned && !IsSoloCombatHeld)
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

        var now = DateTime.UtcNow;
        var readinessConditions = ReadReadinessConditions();
        var readiness = ResolveReadiness(
            config,
            liveDutyIdentity.TerritoryTypeId,
            liveDutyIdentity.ContentFinderConditionId);
        IsHandoffPending = readiness.CanUseAds && !ownership.IsOwned;
        if (IsHandoffPending)
            plugin.BossModActionTweaksService.ResetRecovery();

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
        ResetHandoff();
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
            $"[FrenRider][ADS] {reason} for {readiness.Entry!.DutyName} ({AdsDutyCategoryCatalog.GetLabel(readiness.Entry.Category)}) with ADS clearance {readiness.Entry.ClearanceStatus} (M{readiness.Entry.ClearanceLevel}), support {readiness.Entry.SupportLevel}, threshold {readiness.FamilySettings.MaturityThreshold}, and {readiness.FamilySettings.HandoffDelaySeconds}s continuous-ready delay.");
    }

    private void BackoffFailedHandoff(DateTime now, AdsDutyReadiness readiness, string reason)
    {
        handoffRequestedAtUtc = DateTime.MinValue;
        nextHandoffAttemptUtc = now + AdsIntegrationPolicy.HandoffConfirmationTimeout;
        handoffState.ResetCountdown();
        StatusText = BuildReadinessStatus(readiness, $"{reason}; restarting readiness delay with 5s retry backoff");
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
