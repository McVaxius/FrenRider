using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class FollowServiceStuckJumpTests
{
    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, false, false)]
    public void TrackingRequiresKnownRunningNonCalculatingVNavPath(
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
