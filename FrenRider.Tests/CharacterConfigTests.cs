using System.Text.Json;
using FrenRider.Models;

namespace FrenRider.Tests;

public sealed class CharacterConfigTests
{
    [Fact]
    public void FatePreferencesDefaultOffWithoutChangingExistingSyncDefault()
    {
        foreach (var config in new[]
        {
            new CharacterConfig(),
            JsonSerializer.Deserialize<CharacterConfig>("{}")!,
        })
        {
            Assert.False(config.PauseClingForFate);
            Assert.False(config.IgnoreFates);
            Assert.True(config.AutoSyncFate);
        }
    }

    [Fact]
    public void FatePreferencesSurviveJsonAndCloneWithoutRewritingSyncOrDistance()
    {
        var config = new CharacterConfig
        {
            PauseClingForFate = true,
            IgnoreFates = true,
            AutoSyncFate = false,
            FDistance = 7f,
            Cling = 3f,
        };
        foreach (var copy in new[] { config.Clone(), JsonSerializer.Deserialize<CharacterConfig>(JsonSerializer.Serialize(config))! })
        {
            Assert.True(copy.PauseClingForFate);
            Assert.True(copy.IgnoreFates);
            Assert.False(copy.AutoSyncFate);
            Assert.Equal(7f, copy.FDistance);
            Assert.Equal(3f, copy.Cling);
        }
        var legacy = JsonSerializer.Deserialize<CharacterConfig>("""{"AutoSyncFate":false}""")!;
        Assert.False(legacy.AutoSyncFate);
        Assert.False(legacy.PauseClingForFate);
        Assert.False(legacy.IgnoreFates);
    }

    [Fact]
    public void ClingExclusionsSeedCurrentCitiesAndSecondaryHubsOnly()
    {
        var config = new CharacterConfig();

        Assert.Equal(new uint[]
        {
            128, 129, 130, 131, 132, 133, 418, 419, 478,
            628, 635, 759, 819, 820, 962, 963, 1185, 1186,
        }, config.ClingExcludedTerritoryIds);
        Assert.All(config.ClingExcludedTerritoryIds, id => Assert.True(config.IsClingExcluded(id)));
        Assert.All(new uint[] { 0, 144, 156, 339, 340, 341, 641, 979 },
            id => Assert.False(config.IsClingExcluded(id)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LiveClingExclusionsFollowTerritoryWithoutChangingSavedMovementPolicies(int clingType)
    {
        var config = new CharacterConfig
        {
            Enabled = true,
            ClingType = clingType,
            ClingTypeDuty = clingType,
            Formation = true,
            MountUpToChaseFren = true,
            FollowLocalAetheryteNetworks = true,
            TryTeleportToFrenWhenOutOfZone = true,
        };

        Assert.False(config.IsClingExcluded(156)); // Leaving a city restores eligibility.
        Assert.True(config.IsClingExcluded(129));
        config.ClingExcludedTerritoryIds.Remove(129);
        Assert.False(config.IsClingExcluded(129));
        config.ClingExcludedTerritoryIds.Add(156);
        Assert.True(config.IsClingExcluded(156));
        config.ClingExcludedTerritoryIds.Clear();
        Assert.False(config.IsClingExcluded(129));
        Assert.False(config.IsClingExcluded(156));

        Assert.True(config.Enabled);
        Assert.Equal(clingType, config.ClingType);
        Assert.Equal(clingType, config.ClingTypeDuty);
        Assert.True(config.Formation);
        Assert.True(config.MountUpToChaseFren);
        Assert.True(config.FollowLocalAetheryteNetworks);
        Assert.True(config.TryTeleportToFrenWhenOutOfZone);
    }

    [Fact]
    public void AccountJsonPreservesCustomAndDeliberatelyEmptyClingExclusions()
    {
        var account = new AccountConfig
        {
            DefaultConfig = new CharacterConfig { ClingExcludedTerritoryIds = [156, 9999, 156] },
            Characters =
            {
                ["Custom@World"] = new CharacterConfig { ClingExcludedTerritoryIds = [129, 156] },
                ["Empty@World"] = new CharacterConfig { ClingExcludedTerritoryIds = [] },
            },
        };

        var loaded = JsonSerializer.Deserialize<AccountConfig>(JsonSerializer.Serialize(account))!;

        Assert.Equal(new uint[] { 156, 9999, 156 }, loaded.DefaultConfig.ClingExcludedTerritoryIds);
        Assert.Equal(new uint[] { 129, 156 }, loaded.Characters["Custom@World"].ClingExcludedTerritoryIds);
        Assert.Empty(loaded.Characters["Empty@World"].ClingExcludedTerritoryIds);
        Assert.NotSame(account.DefaultConfig.ClingExcludedTerritoryIds, loaded.DefaultConfig.ClingExcludedTerritoryIds);
    }

    [Fact]
    public void LegacyAccountAndIndependentClonesGetSeparateClingExclusionLists()
    {
        var account = JsonSerializer.Deserialize<AccountConfig>(
            """{"DefaultConfig":{},"Characters":{"Legacy@World":{}}}""")!;
        var local = account.Characters["Legacy@World"];
        Assert.Equal(account.DefaultConfig.ClingExcludedTerritoryIds, local.ClingExcludedTerritoryIds);
        Assert.NotSame(account.DefaultConfig.ClingExcludedTerritoryIds, local.ClingExcludedTerritoryIds);

        var clone = local.Clone();
        Assert.Equal(local.ClingExcludedTerritoryIds, clone.ClingExcludedTerritoryIds);
        Assert.NotSame(local.ClingExcludedTerritoryIds, clone.ClingExcludedTerritoryIds);
        clone.ClingExcludedTerritoryIds.Clear();
        Assert.True(local.IsClingExcluded(129));
        Assert.True(account.DefaultConfig.IsClingExcluded(129));
        Assert.Empty(clone.Clone().ClingExcludedTerritoryIds);

        local.ClingExcludedTerritoryIds = [156, 9999, 156];
        clone = local.Clone();
        clone.ClingExcludedTerritoryIds.RemoveAt(0);
        Assert.Equal(new uint[] { 156, 9999, 156 }, local.ClingExcludedTerritoryIds);
        Assert.Equal(new uint[] { 9999, 156 }, clone.ClingExcludedTerritoryIds);
    }

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
