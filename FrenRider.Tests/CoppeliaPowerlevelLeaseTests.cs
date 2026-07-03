using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class CoppeliaPowerlevelLeaseTests
{
    [Fact]
    public void AcquireUsesSessionTokenOwnership()
    {
        var harness = new LeaseHarness();

        var first = harness.Coordinator.Acquire("token-a");
        var second = harness.Coordinator.Acquire("token-b");
        var heartbeat = harness.Coordinator.Heartbeat("token-a");

        Assert.True(first.Ok);
        Assert.False(second.Ok);
        Assert.Contains("another session", second.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(heartbeat.Ok);
        Assert.True(harness.Coordinator.IsLeaseActive);
    }

    [Fact]
    public void HeartbeatExpiryClearsLeaseAndRecovers()
    {
        var harness = new LeaseHarness();
        harness.Coordinator.Acquire("token-a");

        harness.Now = harness.Now.AddSeconds(6);
        harness.Coordinator.Update();

        Assert.False(harness.Coordinator.IsLeaseActive);
        Assert.Equal(new[] { "lease expiry" }, harness.Recoveries);
        Assert.Contains("expired", harness.Coordinator.LeaseReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActiveCompanionBlocksAcquire()
    {
        var harness = new LeaseHarness
        {
            Environment = LeaseHarness.ValidEnvironment with { CompanionActive = true, CompanionName = "Chocobo", CompanionObjectId = 99 },
        };

        var response = harness.Coordinator.Acquire("token-a");

        Assert.False(response.Ok);
        Assert.Contains("companion", response.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(harness.Coordinator.IsLeaseActive);
    }

    [Fact]
    public void CombatSuppressionCommandsStopAllEngines()
    {
        Assert.Equal(
            new[] { "/bmrai off", "/vbmai off", "/rotation cancel", "/wrath auto off" },
            CombatService.BuildCoppeliaPowerlevelCombatOffCommands());
    }

    [Fact]
    public void CombatSuppressionCommandsOmitRsrFallbackWhenIpcHandledIt()
    {
        Assert.Equal(
            new[] { "/bmrai off", "/vbmai off", "/wrath auto off" },
            CombatService.BuildCoppeliaPowerlevelCombatOffCommands(includeRsrFallback: false));
    }

    [Fact]
    public void NormalReleaseInvokesRecoveryCycle()
    {
        var harness = new LeaseHarness();
        harness.Coordinator.Acquire("token-a");

        var response = harness.Coordinator.Release("token-a");

        Assert.True(response.Ok);
        Assert.False(harness.Coordinator.IsLeaseActive);
        Assert.Equal(new[] { "normal release" }, harness.Recoveries);
    }

    [Fact]
    public void ManualDisableRevokesLeaseWithoutRecovery()
    {
        var harness = new LeaseHarness();
        harness.Coordinator.Acquire("token-a");

        harness.Coordinator.ManualFrenRiderDisable();

        Assert.False(harness.Coordinator.IsLeaseActive);
        Assert.Empty(harness.Recoveries);
        Assert.Contains("manually disabled", harness.Coordinator.LeaseReason, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class LeaseHarness
    {
        public static readonly CoppeliaPowerlevelEnvironment ValidEnvironment = new(
            FrenRiderEnabled: true,
            ConfiguredFrenName: "Leader",
            VisibleFrenName: "Leader",
            VisibleFrenObjectId: 123,
            CompanionActive: false,
            CompanionName: string.Empty,
            CompanionObjectId: 0);

        public LeaseHarness()
        {
            Coordinator = new CoppeliaPowerlevelLeaseCoordinator(
                () => Now,
                () => Environment,
                reason => Recoveries.Add(reason));
        }

        public DateTimeOffset Now { get; set; } = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
        public CoppeliaPowerlevelEnvironment Environment { get; init; } = ValidEnvironment;
        public List<string> Recoveries { get; } = new();
        public CoppeliaPowerlevelLeaseCoordinator Coordinator { get; }
    }
}
