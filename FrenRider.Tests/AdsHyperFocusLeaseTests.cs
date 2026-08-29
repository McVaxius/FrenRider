using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class AdsHyperFocusLeaseTests
{
    [Fact]
    public void AcquireUsesTokenOwnershipAndSameTokenDoesNotCreateAnotherGeneration()
    {
        var harness = new LeaseHarness();

        var first = harness.Coordinator.Acquire("token-a");
        var sameToken = harness.Coordinator.Acquire("token-a");
        var otherToken = harness.Coordinator.Acquire("token-b");

        Assert.True(first.Ok);
        Assert.True(sameToken.Ok);
        Assert.False(otherToken.Ok);
        Assert.Equal(1, harness.Coordinator.LeaseGeneration);
    }

    [Theory]
    [InlineData(false, true, AdsDutyCategory.Solo, true, false, false)]
    [InlineData(true, false, AdsDutyCategory.Solo, true, false, false)]
    [InlineData(true, true, AdsDutyCategory.FourMan, true, false, false)]
    [InlineData(true, true, AdsDutyCategory.Solo, false, false, false)]
    [InlineData(true, true, AdsDutyCategory.Solo, true, false, true)]
    public void AcquireRejectsInvalidEnvironment(
        bool enabled,
        bool inDuty,
        AdsDutyCategory category,
        bool pending,
        bool controlling,
        bool coppeliaActive)
    {
        var harness = new LeaseHarness
        {
            Environment = new AdsHyperFocusEnvironment(enabled, inDuty, category, pending, controlling, coppeliaActive),
        };

        Assert.False(harness.Coordinator.Acquire("token-a").Ok);
        Assert.False(harness.Coordinator.IsLeaseActive);
    }

    [Fact]
    public void HeartbeatExpiryReleasesCombatOnce()
    {
        var harness = new LeaseHarness();
        harness.Coordinator.Acquire("token-a");
        harness.Now = harness.Now.AddSeconds(6);

        harness.Coordinator.Update();

        Assert.False(harness.Coordinator.IsLeaseActive);
        Assert.Equal(new[] { "lease expiry" }, harness.Releases);
    }

    [Fact]
    public void HeartbeatRenewsTheFiveSecondLease()
    {
        var harness = new LeaseHarness();
        harness.Coordinator.Acquire("token-a");
        harness.Now = harness.Now.AddSeconds(4);

        Assert.True(harness.Coordinator.Heartbeat("token-a").Ok);
        harness.Now = harness.Now.AddSeconds(4);
        harness.Coordinator.Update();

        Assert.True(harness.Coordinator.IsLeaseActive);
        Assert.Empty(harness.Releases);

        harness.Now = harness.Now.AddSeconds(2);
        harness.Coordinator.Update();

        Assert.False(harness.Coordinator.IsLeaseActive);
        Assert.Equal(new[] { "lease expiry" }, harness.Releases);
    }

    [Fact]
    public void NormalReleaseRestoresCombatButManualDisableDoesNot()
    {
        var releaseHarness = new LeaseHarness();
        releaseHarness.Coordinator.Acquire("token-a");
        releaseHarness.Coordinator.Release("token-a");

        var disableHarness = new LeaseHarness();
        disableHarness.Coordinator.Acquire("token-a");
        disableHarness.Coordinator.ManualFrenRiderDisable();

        Assert.Equal(new[] { "normal release" }, releaseHarness.Releases);
        Assert.Empty(disableHarness.Releases);
    }

    private sealed class LeaseHarness
    {
        public LeaseHarness()
        {
            Coordinator = new AdsHyperFocusLeaseCoordinator(
                () => Now,
                () => Environment,
                reason => Releases.Add(reason));
        }

        public DateTimeOffset Now { get; set; } = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
        public AdsHyperFocusEnvironment Environment { get; init; } = new(true, true, AdsDutyCategory.Solo, true, false, false);
        public List<string> Releases { get; } = new();
        public AdsHyperFocusLeaseCoordinator Coordinator { get; }
    }
}
