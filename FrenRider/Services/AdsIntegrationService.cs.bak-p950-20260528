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
    private readonly Dictionary<uint, AdsDutyCatalogEntry> entriesByTerritory = [];

    private DateTime dutyEnteredUtc = DateTime.MinValue;
    private DateTime lastPraetoriumReadyWaitLogUtc = DateTime.MinValue;
    private uint trackedDutyTerritoryId;
    private bool adsInsideSent;
    private bool exitReleasedForCurrentDuty;

    public AdsIntegrationService(Plugin plugin, ZoneService zoneService)
    {
        this.plugin = plugin;
        this.zoneService = zoneService;
        BuildCatalog();
        StatusText = "ADS handoff off; FrenRider local duty logic active.";
    }

    public bool AdsLoaded { get; private set; }
    public bool IsHandoffPending { get; private set; }
    public bool IsControllingDuty { get; private set; }
    public bool HadAdsControlThisDuty { get; private set; }
    public bool ShouldPauseDutySystems => IsControllingDuty;
    public string StatusText { get; private set; }

    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var inDuty = Plugin.Condition[ConditionFlag.BoundByDuty]
            || Plugin.Condition[ConditionFlag.BoundByDuty56];
        var territoryTypeId = zoneService.TerritoryId;

        AdsLoaded = IsAdsLoaded();

        if (territoryTypeId != trackedDutyTerritoryId || !inDuty)
        {
            trackedDutyTerritoryId = territoryTypeId;
            dutyEnteredUtc = inDuty ? DateTime.UtcNow : DateTime.MinValue;
            adsInsideSent = false;
            exitReleasedForCurrentDuty = false;
            HadAdsControlThisDuty = false;
            IsControllingDuty = false;
            IsHandoffPending = false;
            lastPraetoriumReadyWaitLogUtc = DateTime.MinValue;
        }

        if (config == null || !config.Enabled)
        {
            IsControllingDuty = false;
            IsHandoffPending = false;
            StatusText = AdsLoaded ? "FrenRider disabled." : "ADS not loaded.";
            return;
        }

        if (!inDuty)
        {
            IsControllingDuty = false;
            IsHandoffPending = false;
            StatusText = AdsLoaded
                ? "ADS loaded; waiting for duty. FrenRider local logic active."
                : "ADS not loaded. FrenRider local logic active.";
            return;
        }

        var readiness = ResolveReadiness(config, territoryTypeId);
        IsHandoffPending = readiness.CanUseAds && !adsInsideSent && !exitReleasedForCurrentDuty;
        IsControllingDuty = readiness.CanUseAds && adsInsideSent && !exitReleasedForCurrentDuty;

        if (!readiness.CanUseAds)
        {
            StatusText = BuildReadinessStatus(readiness, readiness.Reason);
            return;
        }

        if (exitReleasedForCurrentDuty)
        {
            StatusText = BuildReadinessStatus(readiness, "ADS handoff complete; FrenRider exit owns duty leave");
            return;
        }

        if (adsInsideSent)
        {
            StatusText = BuildReadinessStatus(readiness, "ADS owns the duty handoff");
            return;
        }

        if (!IsReadyToStartAdsInsideDuty(territoryTypeId))
        {
            StatusText = BuildReadinessStatus(readiness, "waiting for duty start seam");
            return;
        }

        if (!Plugin.CommandManager.ProcessCommand("/ads inside"))
        {
            IsHandoffPending = false;
            IsControllingDuty = false;
            StatusText = BuildReadinessStatus(readiness, "failed to send /ads inside; FrenRider local duty logic stays active");
            return;
        }

        adsInsideSent = true;
        HadAdsControlThisDuty = true;
        IsHandoffPending = false;
        IsControllingDuty = true;
        StatusText = BuildReadinessStatus(readiness, "sent /ads inside");
        Plugin.Log.Information(
            $"[FrenRider][ADS] Sent /ads inside for {readiness.Entry!.EnglishName} ({AdsDutyCategoryCatalog.GetLabel(readiness.Entry.Category)}) with maturity {readiness.Entry.MaturityLevel} and threshold {readiness.FamilySettings.MaturityThreshold}.");
    }

    public void ReleaseDutyControlForExit(string reason)
    {
        if (!adsInsideSent && !HadAdsControlThisDuty)
            return;

        exitReleasedForCurrentDuty = true;
        IsControllingDuty = false;
        IsHandoffPending = false;
        StatusText = $"ADS handoff complete; FrenRider exit owns duty leave ({reason}).";
        Plugin.Log.Information($"[FrenRider][ADS] Released ADS duty pause for FrenRider exit: {reason}");
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
                NormalizeName(row.ContentType.Value.Name.ToString()));
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

    private static AdsDutyCategory ClassifyDutyCategory(
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
            || normalizedType.Contains("guildhest", StringComparison.Ordinal))
        {
            return AdsDutyCategory.GuildHest;
        }

        if (normalizedType.Contains("deep dungeon", StringComparison.Ordinal))
            return AdsDutyCategory.DeepDungeon;

        if (normalizedType.Contains("treasure", StringComparison.Ordinal))
            return AdsDutyCategory.TreasureDungeon;

        if (normalizedType.Contains("alliance", StringComparison.Ordinal) || partySize >= 24)
            return AdsDutyCategory.Alliance;

        if (partySize <= 1)
            return AdsDutyCategory.Solo;

        if (partySize == 4)
            return AdsDutyCategory.FourMan;

        if (partySize == 8)
            return AdsDutyCategory.EightMan;

        return contentTypeRowId switch
        {
            5 => AdsDutyCategory.GuildHest,
            21 => AdsDutyCategory.DeepDungeon,
            _ => contentMemberTypeRowId switch
            {
                3 => AdsDutyCategory.Solo,
                4 => AdsDutyCategory.FourMan,
                5 => AdsDutyCategory.EightMan,
                6 => AdsDutyCategory.Alliance,
                _ => AdsDutyCategory.Other,
            },
        };
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
        AdsDutyCategory Category,
        int MaturityLevel);

    private sealed record AdsDutyReadiness(
        AdsDutyCatalogEntry? Entry,
        AdsDutyFamilySettings FamilySettings,
        bool CanUseAds,
        string Reason);
}
