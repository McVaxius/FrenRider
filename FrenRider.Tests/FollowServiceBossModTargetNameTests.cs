using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class FollowServiceBossModTargetNameTests
{
    [Fact]
    public void CrossWorldConcatenatedSuffixIsRemoved()
    {
        Assert.Equal(
            "Xxx Yyy",
            FollowService.ResolveBossModTargetName("Xxx YyyLich", "Lich"));
    }

    [Fact]
    public void ConfiguredWorldIsUsedWhenTrackedWorldIsMissing()
    {
        Assert.Equal(
            "Xxx Yyy",
            FollowService.ResolveBossModTargetName("Xxx YyyAlpha", string.Empty, "Xxx Yyy@Alpha"));
    }

    [Fact]
    public void LiveWorldTakesPrecedenceOverConfiguredWorld()
    {
        Assert.Equal(
            "Xxx Yyy",
            FollowService.ResolveBossModTargetName("Xxx YyyLich", "Lich", "Xxx Yyy@Alpha"));
        Assert.Equal("Lich", FrenTracker.ResolveWorldName("Lich", "Xxx Yyy@Alpha"));
    }

    [Fact]
    public void SameWorldNameIsUnchanged()
    {
        Assert.Equal(
            "Xxx Yyy",
            FollowService.ResolveBossModTargetName("Xxx Yyy", "Lich"));
    }

    [Fact]
    public void SurnameMatchingWorldIsNotRemoved()
    {
        Assert.Equal(
            "Xxx Lich",
            FollowService.ResolveBossModTargetName("Xxx Lich", "Lich"));
        Assert.Equal(
            "Xxx Alpha",
            FollowService.ResolveBossModTargetName("Xxx Alpha", string.Empty, "Xxx Alpha@Alpha"));
    }

    [Fact]
    public void EmptyWorldAndSurroundingWhitespaceAreHandledSafely()
    {
        Assert.Equal(
            "Xxx Yyy",
            FollowService.ResolveBossModTargetName("  Xxx YyyLich  ", "  Lich  "));
        Assert.Equal(
            "Xxx Yyy",
            FollowService.ResolveBossModTargetName("  Xxx Yyy  ", string.Empty));
        Assert.Equal(
            "Xxx Yyy",
            FollowService.ResolveBossModTargetName("  Xxx Yyy  ", "   "));
        Assert.Equal(
            "Xxx YyyAlpha",
            FollowService.ResolveBossModTargetName("Xxx YyyAlpha", string.Empty, "Xxx Yyy"));
        Assert.Equal(string.Empty, FrenTracker.ResolveWorldName(string.Empty, "Xxx Yyy"));
    }

    [Fact]
    public void PartialNameMatchingWorldSuffixIsUnchanged()
    {
        Assert.Equal(
            "XxxAlpha",
            FollowService.ResolveBossModTargetName("XxxAlpha", string.Empty, "Xxx@Alpha"));
    }

    [Fact]
    public void ObjectFallbackWorldUsesConfiguredIdentityWhenLiveWorldIsUnavailable()
    {
        Assert.Equal("Alpha", FrenTracker.ResolveWorldName(string.Empty, "Xxx Yyy@Alpha"));
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, false, false)]
    public void StuckJumpTrackingRequiresRunningNonCalculatingVNavPath(
        bool stateKnown,
        bool pathRunning,
        bool pathfindInProgress,
        bool expected)
    {
        Assert.Equal(
            expected,
            FollowService.ShouldTrackStuckFollowJumpForVNavState(
                stateKnown,
                pathRunning,
                pathfindInProgress));
    }
}
