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
}
