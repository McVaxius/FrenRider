using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using FrenRider.Models;
using Lumina.Excel.Sheets;

namespace FrenRider.Services;

public sealed class AdsIntegrationService
{
    private const uint PraetoriumTerritoryTypeId = 1044;
    private const float PraetoriumTimeLimitSeconds = 7200f;
    private const double PraetoriumReadyFallbackSeconds = 15.0;
    private const double GenericDutyReadyDelaySeconds = 2.0;

    private static readonly HashSet<string> PilotDutyNames =
    [
        "the tam-tara deepcroft",
        "the thousand maws of toto-rak",
        "brayflox's longstop",
        "the stone vigil",
        "the aurum vale",
        "castrum meridianum",
    ];

    private static readonly Dictionary<string, int> ClearanceMaturityByDutyName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sastasha"] = 1,
        ["copperbell mines"] = 1,
        ["haukke manor"] = 1,
        ["halatali"] = 1,
        ["the tam-tara deepcroft"] = 2,
        ["the thousand maws of toto-rak"] = 1,
        ["the keeper of the lake"] = 1,
        ["the stone vigil"] = 1,
        ["the aurum vale"] = 1,
        ["hells' lid"] = 1,
        ["the sunken temple of qarn"] = 1,
        ["castrum meridianum"] = 3,
        ["the praetorium"] = 3,
        ["dzemael darkhold"] = 1,
        ["the burn"] = 1,
        ["cutter's cry"] = 1,
        ["pharos sirius"] = 1,
        ["hullbreaker isle"] = 1,
        ["doma castle"] = 1,
        ["castrum abania"] = 1,
        ["brayflox's longstop"] = 1,
    };

    private static readonly HashSet<uint> TreasureDutyTerritoryIds =
    [
        558,
        712,
        725,
        879,
        1000,
        1209,
    ];

    private readonly Plugin plugin;
    private readonly ZoneService zoneService;
    private readonly AdsDutyIpcService adsDutyIpcService;
    private readonly Dictionary<uint, AdsDutyCatalogEntry> entriesByTerritory = [];

    private DateTime dutyEnteredUtc = DateTime.MinValue;
    private DateTime lastPraetoriumReadyWaitLogUtc = DateTime.MinValue;
    private DateTime handoffRequestedAtUtc = DateTime.MinValue;
    private DateTime nextHandoffAttemptUtc = DateTime.MinValue;
    private uint trackedDutyTerritoryId;
    private bool runtimeOwnedLastUpdate;
    private bool ownershipReleasedForCurrentDuty;

    public AdsIntegrationService(Plugin plugin, ZoneService zoneService, AdsDutyIpcService adsDutyIpcService)
    {
        this.plugin = plugin;
        this.zoneService = zoneService;
        this.adsDutyIpcService = adsDutyIpcService;
        BuildCatalog();
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

        var ownership = adsDutyIpcService.Refresh();
        AdsLoaded = ownership.AdsLoaded;
        RuntimeOwnershipReadable = ownership.StatusReadable;
        RuntimeOwnershipSource = ownership.Source.ToString();

        if (territoryTypeId != trackedDutyTerritoryId || !inDuty)
        {
            trackedDutyTerritoryId = territoryTypeId;
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

        var readiness = ResolveReadiness(config, territoryTypeId);
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
        var territoryTypeId = zoneService.TerritoryId != 0
            ? zoneService.TerritoryId
            : Plugin.ClientState.TerritoryType;
        return GetDutyCategory(territoryTypeId);
    }

    internal AdsDutyCategory? GetDutyCategory(uint territoryTypeId)
        => entriesByTerritory.TryGetValue(territoryTypeId, out var entry)
            ? entry.Category
            : null;

    private void AwaitHandoffConfirmation(DateTime now, AdsDutyReadiness readiness, string reason)
    {
        handoffRequestedAtUtc = now;
        nextHandoffAttemptUtc = now + AdsIntegrationPolicy.HandoffConfirmationTimeout;
        StatusText = BuildReadinessStatus(readiness, $"{reason}; waiting for authoritative ownership");
        Plugin.Log.Information(
            $"[FrenRider][ADS] {reason} for {readiness.Entry!.EnglishName} ({AdsDutyCategoryCatalog.GetLabel(readiness.Entry.Category)}) with maturity {readiness.Entry.MaturityLevel} and threshold {readiness.FamilySettings.MaturityThreshold}.");
    }

    private void BackoffFailedHandoff(DateTime now, AdsDutyReadiness readiness, string reason)
    {
        handoffRequestedAtUtc = DateTime.MinValue;
        nextHandoffAttemptUtc = now + AdsIntegrationPolicy.HandoffConfirmationTimeout;
        StatusText = BuildReadinessStatus(readiness, $"{reason}; retrying after 5s");
        Plugin.Log.Warning($"[FrenRider][ADS] {reason}.");
    }

    private void BuildCatalog()
    {
        var contentFinderSheet = Plugin.DataManager.GetExcelSheet<ContentFinderCondition>();
        var englishSheet = Plugin.DataManager.GetExcelSheet<ContentFinderCondition>(ClientLanguage.English);
        if (contentFinderSheet is null)
            return;

        foreach (var row in contentFinderSheet)
        {
            if (row.ContentType.ValueNullable is null)
                continue;

            if (row.TerritoryType.ValueNullable is null || row.ContentMemberType.ValueNullable is null)
                continue;

            var partySize = row.ContentMemberType.Value.TanksPerParty
                + row.ContentMemberType.Value.HealersPerParty
                + row.ContentMemberType.Value.MeleesPerParty
                + row.ContentMemberType.Value.RangedPerParty;
            var englishRow = englishSheet?.GetRow(row.RowId) ?? row;
            var englishName = NormalizeName(englishRow.Name.ToString());
            if (string.IsNullOrWhiteSpace(englishName))
                continue;

            var category = ClassifyDutyCategory(
                row.TerritoryType.Value.RowId,
                row.ContentType.Value.RowId,
                row.ContentMemberType.Value.RowId,
                partySize,
                NormalizeName(englishRow.ContentType.Value.Name.ToString()));
            var lowered = englishName.ToLowerInvariant();
            var maturity = PilotDutyNames.Contains(lowered)
                ? Math.Max(ClearanceMaturityByDutyName.GetValueOrDefault(englishName, 0), 3)
                : ClearanceMaturityByDutyName.GetValueOrDefault(englishName, 0);

            entriesByTerritory[row.TerritoryType.Value.RowId] = new AdsDutyCatalogEntry(
                row.TerritoryType.Value.RowId,
                englishName,
                category,
                maturity);
        }

        foreach (var territoryId in TreasureDutyTerritoryIds)
        {
            if (entriesByTerritory.ContainsKey(territoryId))
                continue;

            entriesByTerritory[territoryId] = new AdsDutyCatalogEntry(
                territoryId,
                $"Treasure Duty {territoryId}",
                AdsDutyCategory.TreasureDungeon,
                3);
        }
    }

    private AdsDutyReadiness ResolveReadiness(CharacterConfig config, uint territoryTypeId)
    {
        if (!AdsLoaded)
            return new AdsDutyReadiness(null, default, false, "ADS is not loaded");

        if (!entriesByTerritory.TryGetValue(territoryTypeId, out var entry))
            return new AdsDutyReadiness(null, default, false, $"territory {territoryTypeId} is not in the mirrored ADS catalog; FrenRider local duty logic stays active");

        var familySettings = config.GetAdsDutyFamilySettings(entry.Category);
        if (!familySettings.Enabled)
            return new AdsDutyReadiness(entry, familySettings, false, $"{AdsDutyCategoryCatalog.GetLabel(entry.Category)} handoff is off; FrenRider local duty logic stays active");

        if (entry.MaturityLevel < familySettings.MaturityThreshold)
        {
            return new AdsDutyReadiness(
                entry,
                familySettings,
                false,
                $"{entry.EnglishName} is maturity {entry.MaturityLevel}, below threshold {familySettings.MaturityThreshold}; FrenRider local duty logic stays active");
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
        return $"{categoryLabel} {readiness.Entry.EnglishName}: M{readiness.Entry.MaturityLevel}/T{readiness.FamilySettings.MaturityThreshold}, {trailingStatus}.";
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

    internal static AdsDutyCategory ClassifyDutyCategory(
        uint territoryTypeId,
        uint contentTypeRowId,
        uint contentMemberTypeRowId,
        int partySize,
        string contentTypeName)
    {
        if (TreasureDutyTerritoryIds.Contains(territoryTypeId))
            return AdsDutyCategory.TreasureDungeon;

        var normalizedType = NormalizeName(contentTypeName).ToLowerInvariant();
        if (normalizedType.Contains("guild hest", StringComparison.Ordinal)
            || normalizedType.Contains("guildhest", StringComparison.Ordinal)
            || contentTypeRowId == 5)
        {
            return AdsDutyCategory.GuildHest;
        }

        if (normalizedType.Contains("deep dungeon", StringComparison.Ordinal)
            || contentTypeRowId == 21)
            return AdsDutyCategory.DeepDungeon;

        if (normalizedType.Contains("treasure", StringComparison.Ordinal))
            return AdsDutyCategory.TreasureDungeon;

        if (normalizedType.Contains("alliance", StringComparison.Ordinal) || partySize >= 24)
            return AdsDutyCategory.Alliance;

        if (partySize == 1)
            return AdsDutyCategory.Solo;

        if (partySize == 4)
            return AdsDutyCategory.FourMan;

        if (partySize == 8)
            return AdsDutyCategory.EightMan;

        if (partySize > 0)
            return AdsDutyCategory.Other;

        return contentMemberTypeRowId switch
        {
            3 => AdsDutyCategory.Solo,
            4 => AdsDutyCategory.FourMan,
            5 => AdsDutyCategory.EightMan,
            6 => AdsDutyCategory.Alliance,
            _ => AdsDutyCategory.Other,
        };
    }

    private static string NormalizeName(string name)
        => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private sealed record AdsDutyCatalogEntry(
        uint TerritoryTypeId,
        string EnglishName,
        AdsDutyCategory Category,
        int MaturityLevel);

    private sealed record AdsDutyReadiness(
        AdsDutyCatalogEntry? Entry,
        AdsDutyFamilySettings FamilySettings,
        bool CanUseAds,
        string Reason);
}
