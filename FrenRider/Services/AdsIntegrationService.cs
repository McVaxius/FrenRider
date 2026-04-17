using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game;
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

    private static readonly HashSet<string> LaterWaveDutyNames =
    [
        "sastasha",
        "copperbell mines",
        "haukke manor",
        "the keeper of the lake",
        "the praetorium",
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

    private readonly Plugin plugin;
    private readonly ZoneService zoneService;
    private readonly Dictionary<uint, AdsDutyCatalogEntry> entriesByTerritory = [];

    private DateTime dutyEnteredUtc = DateTime.MinValue;
    private DateTime lastPraetoriumReadyWaitLogUtc = DateTime.MinValue;
    private uint trackedDutyTerritoryId;
    private bool adsInsideSent;

    public AdsIntegrationService(Plugin plugin, ZoneService zoneService)
    {
        this.plugin = plugin;
        this.zoneService = zoneService;
        BuildCatalog();
        StatusText = "ADS disabled.";
    }

    public bool AdsLoaded { get; private set; }
    public bool IsHandoffPending { get; private set; }
    public bool IsControllingDuty { get; private set; }
    public bool ShouldPauseDutySystems => IsHandoffPending || IsControllingDuty;
    public string StatusText { get; private set; }

    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var inDuty = Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BoundByDuty]
            || Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BoundByDuty56];
        var territoryTypeId = zoneService.TerritoryId;

        AdsLoaded = IsAdsLoaded();

        if (territoryTypeId != trackedDutyTerritoryId || !inDuty)
        {
            trackedDutyTerritoryId = territoryTypeId;
            dutyEnteredUtc = inDuty ? DateTime.UtcNow : DateTime.MinValue;
            adsInsideSent = false;
            IsControllingDuty = false;
            IsHandoffPending = false;
            lastPraetoriumReadyWaitLogUtc = DateTime.MinValue;
        }

        if (config == null || !config.Enabled)
        {
            StatusText = AdsLoaded ? "ADS available, FrenRider disabled." : "ADS not loaded.";
            return;
        }

        if (!config.UseAdsIfAvailable)
        {
            StatusText = AdsLoaded ? "ADS loaded but local handoff is off." : "ADS handoff off and ADS not loaded.";
            return;
        }

        if (!inDuty)
        {
            StatusText = BuildReadinessStatus(config, null, "waiting for duty");
            return;
        }

        var readiness = ResolveReadiness(config, territoryTypeId);
        IsHandoffPending = readiness.CanUseAds && !adsInsideSent;
        IsControllingDuty = readiness.CanUseAds && adsInsideSent;

        if (!readiness.CanUseAds)
        {
            StatusText = BuildReadinessStatus(config, readiness, readiness.Reason);
            return;
        }

        if (adsInsideSent)
        {
            StatusText = BuildReadinessStatus(config, readiness, "ADS owns the local duty handoff");
            return;
        }

        if (!IsReadyToStartAdsInsideDuty(territoryTypeId))
        {
            StatusText = BuildReadinessStatus(config, readiness, "waiting for duty start seam");
            return;
        }

        if (!Plugin.CommandManager.ProcessCommand("/ads inside"))
        {
            IsHandoffPending = false;
            StatusText = BuildReadinessStatus(config, readiness, "failed to send /ads inside");
            return;
        }

        adsInsideSent = true;
        IsHandoffPending = false;
        IsControllingDuty = true;
        StatusText = BuildReadinessStatus(config, readiness, "sent /ads inside");
        Plugin.Log.Information($"[FrenRider][ADS] Sent /ads inside for {readiness.Entry!.EnglishName} with maturity {readiness.Entry.MaturityLevel} and threshold {Math.Clamp(config.AdsMaturityThreshold, 0, 3)}.");
    }

    private void BuildCatalog()
    {
        var contentFinderSheet = Plugin.DataManager.GetExcelSheet<ContentFinderCondition>();
        var englishSheet = Plugin.DataManager.GetExcelSheet<ContentFinderCondition>(ClientLanguage.English);
        if (contentFinderSheet is null)
            return;

        foreach (var row in contentFinderSheet)
        {
            if (row.ContentType.ValueNullable is null || row.ContentType.Value.RowId != 2)
                continue;

            if (row.TerritoryType.ValueNullable is null || row.ContentMemberType.ValueNullable is null)
                continue;

            var partySize = row.ContentMemberType.Value.TanksPerParty
                + row.ContentMemberType.Value.HealersPerParty
                + row.ContentMemberType.Value.MeleesPerParty
                + row.ContentMemberType.Value.RangedPerParty;
            if (partySize != 4)
                continue;

            var englishRow = englishSheet?.GetRow(row.RowId) ?? row;
            var englishName = NormalizeName(englishRow.Name.ToString());
            if (string.IsNullOrWhiteSpace(englishName))
                continue;

            var lowered = englishName.ToLowerInvariant();
            entriesByTerritory[row.TerritoryType.Value.RowId] = new AdsDutyCatalogEntry(
                row.TerritoryType.Value.RowId,
                englishName,
                PilotDutyNames.Contains(lowered) || LaterWaveDutyNames.Contains(lowered),
                PilotDutyNames.Contains(lowered),
                ClearanceMaturityByDutyName.GetValueOrDefault(englishName, 0));
        }
    }

    private AdsDutyReadiness ResolveReadiness(CharacterConfig config, uint territoryTypeId)
    {
        var threshold = Math.Clamp(config.AdsMaturityThreshold, 0, 3);
        if (!AdsLoaded)
            return new AdsDutyReadiness(null, false, "ADS is not loaded");

        if (!entriesByTerritory.TryGetValue(territoryTypeId, out var entry))
            return new AdsDutyReadiness(null, false, $"territory {territoryTypeId} is not in the mirrored ADS catalog");

        if (!entry.SupportsPassiveObservation)
            return new AdsDutyReadiness(entry, false, $"{entry.EnglishName} is not marked ADS-supported");

        if (entry.MaturityLevel < threshold)
            return new AdsDutyReadiness(entry, false, $"{entry.EnglishName} is maturity {entry.MaturityLevel}, below threshold {threshold}");

        return new AdsDutyReadiness(entry, true, "ready");
    }

    private string BuildReadinessStatus(CharacterConfig config, AdsDutyReadiness? readiness, string trailingStatus)
    {
        if (!AdsLoaded)
            return "ADS not loaded.";

        if (readiness?.Entry is null)
            return $"ADS loaded; {trailingStatus}.";

        var threshold = Math.Clamp(config.AdsMaturityThreshold, 0, 3);
        var supportText = readiness.Entry.SupportsActiveExecution ? "active" : "passive";
        return $"{readiness.Entry.EnglishName}: M{readiness.Entry.MaturityLevel}/T{threshold}, {supportText}, {trailingStatus}.";
    }

    private bool IsReadyToStartAdsInsideDuty(uint territoryTypeId)
    {
        if (Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas]
            || Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas51])
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

    private static string NormalizeName(string name)
        => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static bool IsAdsLoaded()
    {
        try
        {
            return Plugin.PluginInterface.InstalledPlugins.Any(plugin =>
                plugin.IsLoaded
                && (string.Equals(plugin.InternalName, "ADS", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(plugin.Name, "AI Duty Solver", StringComparison.OrdinalIgnoreCase)
                    || plugin.Name.Contains("ADS", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[FrenRider][ADS] Failed to inspect installed plugins: {ex.Message}");
            return false;
        }
    }

    private sealed record AdsDutyCatalogEntry(
        uint TerritoryTypeId,
        string EnglishName,
        bool SupportsPassiveObservation,
        bool SupportsActiveExecution,
        int MaturityLevel);

    private sealed record AdsDutyReadiness(
        AdsDutyCatalogEntry? Entry,
        bool CanUseAds,
        string Reason);
}
