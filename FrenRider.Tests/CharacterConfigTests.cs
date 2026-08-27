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
}
