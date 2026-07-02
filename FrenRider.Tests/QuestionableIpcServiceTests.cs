using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class QuestionableIpcServiceTests
{
    [Fact]
    public void UnreadableIpcFallsBackToNotRunning()
    {
        var now = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new QuestionableIpcService(
            queryIsRunning: () => throw new InvalidOperationException("missing IPC"),
            utcNow: () => now);

        var snapshot = service.Refresh(force: true);

        Assert.False(snapshot.IsRunning);
        Assert.False(snapshot.StatusReadable);
        Assert.Equal(QuestionableRunningSource.Unreadable, snapshot.Source);
        Assert.False(service.WasRunningWithin(QuestionableIpcService.RecentRunningHold));
    }

    [Fact]
    public void RecentRunningLatchSurvivesTransitionBoundary()
    {
        var now = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        var running = true;
        var service = new QuestionableIpcService(queryIsRunning: () => running, utcNow: () => now);

        var runningSnapshot = service.Refresh(force: true);
        Assert.True(runningSnapshot.IsRunning);

        running = false;
        now = now.AddSeconds(10);
        var stoppedSnapshot = service.Refresh(force: true);

        Assert.False(stoppedSnapshot.IsRunning);
        Assert.True(service.WasRunningWithin(QuestionableIpcService.RecentRunningHold));

        now = now.AddSeconds(6);
        Assert.False(service.WasRunningWithin(QuestionableIpcService.RecentRunningHold));
    }
}
