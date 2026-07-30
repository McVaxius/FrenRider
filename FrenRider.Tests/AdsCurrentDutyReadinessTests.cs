using System.Text.Json;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class AdsCurrentDutyReadinessTests
{
    private static readonly DateTime CapturedAtUtc =
        new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SastashaUsesAdsM3AndMeetsT3()
    {
        var snapshot = Parse(StatusJson(
            duty: "Sastasha",
            territoryTypeId: 1036,
            contentFinderConditionId: 4,
            category: "FourMan",
            supportLevel: "PassiveOnly",
            clearanceStatus: "FourPlayerSyncCleared"));
        var config = EnabledAtThreshold(3);

        Assert.Equal("Sastasha", snapshot.DutyName);
        Assert.Equal(1036u, snapshot.TerritoryTypeId);
        Assert.Equal(4u, snapshot.ContentFinderConditionId);
        Assert.Equal(AdsDutyCategory.FourMan, snapshot.Category);
        Assert.Equal("PassiveOnly", snapshot.SupportLevel);
        Assert.Equal("FourPlayerSyncCleared", snapshot.ClearanceStatus);
        Assert.Equal(3, snapshot.ClearanceLevel);
        Assert.True(AdsIntegrationService.IsSnapshotReady(config, snapshot));
    }

    [Theory]
    [InlineData("NotCleared", 0)]
    [InlineData("OnePlayerUnsyncCleared", 1)]
    [InlineData("OnePlayerDutySupport", 2)]
    [InlineData("FourPlayerSyncCleared", 3)]
    public void AdsClearanceStringsMapExactlyToMaturityLevels(string clearanceStatus, int expectedLevel)
    {
        var snapshot = Parse(StatusJson(clearanceStatus: clearanceStatus));

        Assert.Equal(clearanceStatus, snapshot.ClearanceStatus);
        Assert.Equal(expectedLevel, snapshot.ClearanceLevel);
    }

    [Theory]
    [InlineData("Solo", AdsDutyCategory.Solo)]
    [InlineData("FourMan", AdsDutyCategory.FourMan)]
    [InlineData("EightMan", AdsDutyCategory.EightMan)]
    [InlineData("AllianceRaid", AdsDutyCategory.Alliance)]
    [InlineData("GuildHest", AdsDutyCategory.GuildHest)]
    [InlineData("DeepDungeon", AdsDutyCategory.DeepDungeon)]
    [InlineData("TreasureDungeon", AdsDutyCategory.TreasureDungeon)]
    [InlineData("Other", AdsDutyCategory.Other)]
    public void AdsCategoriesMapDirectly(string category, AdsDutyCategory expected)
        => Assert.Equal(expected, Parse(StatusJson(category: category)).Category);

    [Fact]
    public void ChangingOnlyAdsClearanceChangesReadiness()
    {
        var config = EnabledAtThreshold(3);
        var lower = Parse(StatusJson(clearanceStatus: "OnePlayerDutySupport"));
        var ready = Parse(StatusJson(clearanceStatus: "FourPlayerSyncCleared"));

        Assert.False(AdsIntegrationService.IsSnapshotReady(config, lower));
        Assert.True(AdsIntegrationService.IsSnapshotReady(config, ready));
    }

    [Theory]
    [InlineData("The Tam-Tara Deepcroft")]
    [InlineData("The Thousand Maws of Toto-Rak")]
    [InlineData("Brayflox's Longstop")]
    [InlineData("The Stone Vigil")]
    [InlineData("The Aurum Vale")]
    [InlineData("Castrum Meridianum")]
    public void FormerPilotDutyNamesReceiveNoIndependentPromotion(string duty)
    {
        var snapshot = Parse(StatusJson(
            duty: duty,
            clearanceStatus: "OnePlayerUnsyncCleared"));

        Assert.Equal(1, snapshot.ClearanceLevel);
        Assert.False(AdsIntegrationService.IsSnapshotReady(EnabledAtThreshold(3), snapshot));
    }

    [Fact]
    public void MissingCatalogMetadataIsRejected()
    {
        Assert.False(AdsCurrentDutySnapshot.TryParseStatusJson(
            StatusJson(hasCatalogMetadata: false),
            CapturedAtUtc,
            out var snapshot,
            out var failure));
        Assert.Null(snapshot);
        Assert.Contains("no catalog metadata", failure);
    }

    [Theory]
    [InlineData("BogusCategory", "PassiveOnly", "FourPlayerSyncCleared")]
    [InlineData("FourMan", "BogusSupport", "FourPlayerSyncCleared")]
    [InlineData("FourMan", "PassiveOnly", "BogusClearance")]
    [InlineData("fourman", "PassiveOnly", "FourPlayerSyncCleared")]
    public void UnknownOrMalformedEnumStringsAreRejected(
        string category,
        string supportLevel,
        string clearanceStatus)
    {
        Assert.False(AdsCurrentDutySnapshot.TryParseStatusJson(
            StatusJson(
                category: category,
                supportLevel: supportLevel,
                clearanceStatus: clearanceStatus),
            CapturedAtUtc,
            out var snapshot,
            out _));
        Assert.Null(snapshot);
    }

    [Theory]
    [InlineData("""{"inInstancedDuty":true""")]
    [InlineData("""{"inInstancedDuty":"true","hasCatalogMetadata":true}""")]
    [InlineData("""{"inInstancedDuty":true,"hasCatalogMetadata":true,"duty":"Sastasha","territoryTypeId":"1036","contentFinderConditionId":4,"dutyCategory":"FourMan","supportLevel":"PassiveOnly","clearanceStatus":"FourPlayerSyncCleared"}""")]
    public void MalformedStatusFieldsAreRejected(string json)
    {
        Assert.False(AdsCurrentDutySnapshot.TryParseStatusJson(
            json,
            CapturedAtUtc,
            out var snapshot,
            out _));
        Assert.Null(snapshot);
    }

    [Fact]
    public void IpcCacheRetainsSameIdentityAcrossTransientJsonFailure()
    {
        var now = CapturedAtUtc;
        var failJson = false;
        var service = CreateService(
            () => true,
            () => false,
            () => failJson
                ? throw new InvalidOperationException("transient")
                : StatusJson(),
            () => now);

        service.Refresh(true, 1036, 4, force: true);
        var captured = service.CurrentDuty;
        Assert.NotNull(captured);

        failJson = true;
        now = now.AddSeconds(1);
        service.Refresh(true, 1036, 4, force: true);

        Assert.Same(captured, service.CurrentDuty);
        Assert.Contains("retaining", service.CurrentDutyDetail);
    }

    [Fact]
    public void IpcCacheClearsOnDutyExitOrLiveIdentityChange()
    {
        var now = CapturedAtUtc;
        var failJson = false;
        var service = CreateService(
            () => true,
            () => false,
            () => failJson
                ? throw new InvalidOperationException("transient")
                : StatusJson(),
            () => now);

        service.Refresh(true, 1036, 4, force: true);
        Assert.NotNull(service.CurrentDuty);

        failJson = true;
        now = now.AddSeconds(1);
        service.Refresh(false, 1036, 4, force: true);
        Assert.Null(service.CurrentDuty);

        failJson = false;
        now = now.AddSeconds(1);
        service.Refresh(true, 1036, 4, force: true);
        Assert.NotNull(service.CurrentDuty);

        failJson = true;
        now = now.AddSeconds(1);
        service.Refresh(true, 1036, 5, force: true);
        Assert.Null(service.CurrentDuty);
    }

    [Fact]
    public void IpcCacheRejectsAdsIdentityMismatchAndMissingMetadata()
    {
        var now = CapturedAtUtc;
        var json = StatusJson();
        var service = CreateService(
            () => true,
            () => false,
            () => json,
            () => now);

        service.Refresh(true, 9999, 4, force: true);
        Assert.Null(service.CurrentDuty);
        Assert.Contains("does not match", service.CurrentDutyDetail);

        json = StatusJson(hasCatalogMetadata: false);
        now = now.AddSeconds(1);
        service.Refresh(true, 1036, 4, force: true);
        Assert.Null(service.CurrentDuty);
        Assert.Contains("no catalog metadata", service.CurrentDutyDetail);
    }

    [Fact]
    public void IpcCacheClearsWhenAdsUnloads()
    {
        var now = CapturedAtUtc;
        var loaded = true;
        var service = CreateService(
            () => loaded,
            () => false,
            () => StatusJson(),
            () => now);

        service.Refresh(true, 1036, 4, force: true);
        Assert.NotNull(service.CurrentDuty);

        loaded = false;
        now = now.AddSeconds(1);
        service.Refresh(true, 1036, 4, force: true);

        Assert.Null(service.CurrentDuty);
        Assert.Equal("ADS unloaded.", service.CurrentDutyDetail);
    }

    private static CharacterConfig EnabledAtThreshold(int threshold)
        => new()
        {
            UseAdsIfAvailable = true,
            AdsMaturityThreshold = threshold,
        };

    private static AdsCurrentDutySnapshot Parse(string json)
    {
        Assert.True(AdsCurrentDutySnapshot.TryParseStatusJson(
            json,
            CapturedAtUtc,
            out var snapshot,
            out var failure), failure);
        return Assert.IsType<AdsCurrentDutySnapshot>(snapshot);
    }

    private static AdsDutyIpcService CreateService(
        Func<bool> loaded,
        Func<bool> typed,
        Func<string> json,
        Func<DateTime> now)
        => new(loaded, typed, json, () => true, now);

    private static string StatusJson(
        string duty = "Sastasha",
        uint territoryTypeId = 1036,
        uint contentFinderConditionId = 4,
        string category = "FourMan",
        string supportLevel = "PassiveOnly",
        string clearanceStatus = "FourPlayerSyncCleared",
        bool hasCatalogMetadata = true)
        => JsonSerializer.Serialize(new
        {
            inInstancedDuty = true,
            ownershipMode = "Observing",
            hasCatalogMetadata,
            duty,
            territoryTypeId,
            contentFinderConditionId,
            dutyCategory = category,
            supportLevel,
            clearanceStatus,
        });
}
