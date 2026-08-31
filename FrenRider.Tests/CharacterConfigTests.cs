using System.Text.Json;
using FrenRider.Models;

namespace FrenRider.Tests;

public sealed class CharacterConfigTests
{
    [Fact]
    public void DutyNudgeFallbackDefaultsOff()
    {
        var config = new CharacterConfig();

        Assert.False(config.NudgeInDutyWhenFrenNotNearbyOrInZone);
    }

    [Fact]
    public void RespawnScopeSettingsDefaultOffWithSixtySecondDelays()
    {
        var config = new CharacterConfig();

        Assert.False(config.RespawnOutsideDuties);
        Assert.Equal(60, config.RespawnOutsideDutiesDelaySeconds);
        Assert.False(config.RespawnInsideDuties);
        Assert.Equal(60, config.RespawnInsideDutiesDelaySeconds);
    }

    [Fact]
    public void RespawnScopeSettingsSurviveClone()
    {
        var config = new CharacterConfig
        {
            RespawnOutsideDuties = true,
            RespawnOutsideDutiesDelaySeconds = 17,
            RespawnInsideDuties = true,
            RespawnInsideDutiesDelaySeconds = 23,
        };

        var clone = config.Clone();

        Assert.True(clone.RespawnOutsideDuties);
        Assert.Equal(17, clone.RespawnOutsideDutiesDelaySeconds);
        Assert.True(clone.RespawnInsideDuties);
        Assert.Equal(23, clone.RespawnInsideDutiesDelaySeconds);
    }

    [Fact]
    public void MissingInsideRespawnFieldsUseBackwardCompatibleDefaults()
    {
        var config = JsonSerializer.Deserialize<CharacterConfig>("{\"RespawnOutsideDuties\":true,\"RespawnOutsideDutiesDelaySeconds\":19}")!;

        Assert.True(config.RespawnOutsideDuties);
        Assert.Equal(19, config.RespawnOutsideDutiesDelaySeconds);
        Assert.False(config.RespawnInsideDuties);
        Assert.Equal(60, config.RespawnInsideDutiesDelaySeconds);
    }

    [Fact]
    public void CleanupModeDefaultsToRestoreSnapshot()
    {
        var config = new CharacterConfig();

        Assert.Equal(FrenRiderCleanupMode.RestoreSnapshot, config.CleanupMode);
    }

    [Fact]
    public void DaedalusTargetModeDefaultsToNone()
    {
        var config = new CharacterConfig();

        Assert.Equal(DaedalusTargetMode.None, config.DaedalusTargetMode);
    }

    [Fact]
    public void ClonePreservesDutyNudgeFallback()
    {
        var config = new CharacterConfig
        {
            NudgeInDutyWhenFrenNotNearbyOrInZone = true,
        };

        Assert.True(config.Clone().NudgeInDutyWhenFrenNotNearbyOrInZone);
    }

    [Fact]
    public void ClonePreservesCleanupMode()
    {
        var config = new CharacterConfig
        {
            CleanupMode = FrenRiderCleanupMode.TurnEverythingOff,
        };

        Assert.Equal(FrenRiderCleanupMode.TurnEverythingOff, config.Clone().CleanupMode);
    }

    [Fact]
    public void ClonePreservesDaedalusTargetMode()
    {
        var config = new CharacterConfig
        {
            DaedalusTargetMode = DaedalusTargetMode.KillAdds,
        };

        Assert.Equal(DaedalusTargetMode.KillAdds, config.Clone().DaedalusTargetMode);
    }

    [Fact]
    public void ProfileAcceptanceDefaultsTemporaryForNewAndMissingJson()
    {
        Assert.Equal(
            FrenRiderProfileAcceptancePolicy.Temporary,
            new CharacterConfig().ProfileAcceptancePolicy);

        var migrated = JsonSerializer.Deserialize<CharacterConfig>("{\"FrenName\":\"Existing\"}")!;
        Assert.Equal(FrenRiderProfileAcceptancePolicy.Temporary, migrated.ProfileAcceptancePolicy);
    }

    [Fact]
    public void ExistingAcceptancePolicySurvivesJsonAndClone()
    {
        var loaded = JsonSerializer.Deserialize<CharacterConfig>("{\"ProfileAcceptancePolicy\":2}")!;

        Assert.Equal(FrenRiderProfileAcceptancePolicy.Permanent, loaded.ProfileAcceptancePolicy);
        Assert.Equal(FrenRiderProfileAcceptancePolicy.Permanent, loaded.Clone().ProfileAcceptancePolicy);
    }

    [Theory]
    [InlineData(AdsDutyCategory.Solo, 10)]
    [InlineData(AdsDutyCategory.FourMan, 2)]
    [InlineData(AdsDutyCategory.EightMan, 2)]
    [InlineData(AdsDutyCategory.Alliance, 2)]
    [InlineData(AdsDutyCategory.GuildHest, 2)]
    [InlineData(AdsDutyCategory.DeepDungeon, 2)]
    [InlineData(AdsDutyCategory.TreasureDungeon, 2)]
    [InlineData(AdsDutyCategory.Other, 2)]
    public void AdsDutyFamiliesUseBackwardCompatibleHandoffDelayDefaults(
        AdsDutyCategory category,
        int expectedDelaySeconds)
    {
        var settings = new CharacterConfig().GetAdsDutyFamilySettings(category);

        Assert.Equal(expectedDelaySeconds, settings.HandoffDelaySeconds);
    }

    [Fact]
    public void AdsDutyFamilyHandoffDelaysClampAndSurviveClone()
    {
        var config = new CharacterConfig();
        var nextDelay = 20;
        foreach (var entry in AdsDutyCategoryCatalog.Entries)
            config.SetAdsDutyFamilySettings(entry.Category, true, 2, nextDelay++);

        config.SetAdsDutyFamilySettings(AdsDutyCategory.Solo, true, 2, 1);
        config.SetAdsDutyFamilySettings(AdsDutyCategory.Other, true, 2, 301);
        var clone = config.Clone();

        Assert.Equal(2, config.GetAdsDutyFamilySettings(AdsDutyCategory.Solo).HandoffDelaySeconds);
        Assert.Equal(300, config.GetAdsDutyFamilySettings(AdsDutyCategory.Other).HandoffDelaySeconds);
        foreach (var entry in AdsDutyCategoryCatalog.Entries)
            Assert.Equal(config.GetAdsDutyFamilySettings(entry.Category), clone.GetAdsDutyFamilySettings(entry.Category));
    }

    [Fact]
    public void LegacyAdsConfigurationLoadsFamilyDelayDefaults()
    {
        var legacy = JsonSerializer.Deserialize<CharacterConfig>(
            "{\"UseAdsIfAvailable\":true,\"AdsMaturityThreshold\":1}")!;
        var migratedWithoutDelayFields = JsonSerializer.Deserialize<CharacterConfig>(
            "{\"AdsDutyFamilySettingsMigrated\":true,\"AdsSoloEnabled\":true,\"AdsSoloMaturityThreshold\":2}")!;

        foreach (var entry in AdsDutyCategoryCatalog.Entries)
        {
            var settings = legacy.GetAdsDutyFamilySettings(entry.Category);
            Assert.True(settings.Enabled);
            Assert.Equal(1, settings.MaturityThreshold);
            Assert.Equal(entry.Category == AdsDutyCategory.Solo ? 10 : 2, settings.HandoffDelaySeconds);
        }

        legacy.EnsureAdsDutyFamilySettingsInitialized();
        Assert.Equal(10, legacy.AdsSoloHandoffDelaySeconds);
        Assert.Equal(2, legacy.AdsFourManHandoffDelaySeconds);
        Assert.Equal(10, migratedWithoutDelayFields.GetAdsDutyFamilySettings(AdsDutyCategory.Solo).HandoffDelaySeconds);
        Assert.Equal(2, migratedWithoutDelayFields.GetAdsDutyFamilySettings(AdsDutyCategory.FourMan).HandoffDelaySeconds);
    }

    [Fact]
    public void DeserializedAdsHandoffDelaysAreClamped()
    {
        var config = JsonSerializer.Deserialize<CharacterConfig>(
            "{\"AdsDutyFamilySettingsMigrated\":true,\"AdsSoloHandoffDelaySeconds\":1,\"AdsOtherHandoffDelaySeconds\":301}")!;

        Assert.Equal(2, config.AdsSoloHandoffDelaySeconds);
        Assert.Equal(300, config.AdsOtherHandoffDelaySeconds);
    }
}
