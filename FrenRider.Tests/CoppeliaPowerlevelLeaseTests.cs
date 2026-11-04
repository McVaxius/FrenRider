using FrenRider.Services;
using System.Text.Json;

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
    public void HeartbeatExpiryRetainsCombatFenceUntilOwnerRestoresAndReleases()
    {
        var harness = new LeaseHarness();
        harness.Coordinator.Acquire("token-a");

        harness.Now = harness.Now.AddSeconds(6);
        harness.Coordinator.Update();

        Assert.True(harness.Coordinator.IsLeaseActive);
        Assert.False(harness.Coordinator.CombatSuppressed);
        Assert.Empty(harness.Recoveries);
        Assert.Contains("expired", harness.Coordinator.LeaseReason, StringComparison.OrdinalIgnoreCase);
        Assert.False(harness.Coordinator.Acquire("token-b").Ok);
        Assert.False(harness.Coordinator.Acquire("token-a").Ok);
        Assert.False(harness.Coordinator.Heartbeat("token-a").Ok);
        Assert.False(harness.Coordinator.Release("token-b").Ok);
        Assert.True(harness.Coordinator.IsLeaseActive);
        Assert.True(harness.Coordinator.Release("token-a").Ok);
        Assert.False(harness.Coordinator.IsLeaseActive);
        Assert.Equal(new[] { "normal release" }, harness.Recoveries);
    }

    [Fact]
    public void SuppressionIsAcknowledgedOnlyAfterConfirmationAndNeverRepeatedOverConsumerState()
    {
        var harness = new LeaseHarness { SuppressionConfirmed = false };
        var acquired = harness.Coordinator.Acquire("token-a");
        Assert.True(acquired.Ok); // Existing ownership semantics remain compatible.
        Assert.False(acquired.CombatSuppressed);
        Assert.False(harness.Coordinator.BuildStatus().CombatSuppressed);
        Assert.Equal(1, harness.SuppressionCalls);
        harness.SuppressionConfirmed = true;
        Assert.True(harness.Coordinator.Heartbeat("token-a").CombatSuppressed);
        Assert.Equal(2, harness.SuppressionCalls);
        harness.SuppressionConfirmed = false; // The consumer now owns a different native state.
        Assert.True(harness.Coordinator.Acquire("token-a").CombatSuppressed);
        Assert.True(harness.Coordinator.Heartbeat("token-a").CombatSuppressed);
        Assert.Equal(2, harness.SuppressionCalls);
        harness.Coordinator.Release("token-a");
        Assert.False(harness.Coordinator.Acquire("token-a").CombatSuppressed);
        Assert.Equal(3, harness.SuppressionCalls);
    }

    [Fact]
    public void FailedProviderCallNeverAcknowledgesSuppression()
    {
        var coordinator = new CoppeliaPowerlevelLeaseCoordinator(() => DateTimeOffset.UtcNow,
            () => LeaseHarness.ValidEnvironment, _ => { }, () => throw new InvalidOperationException("Provider unavailable"));
        Assert.True(coordinator.Acquire("token").Ok);
        Assert.False(coordinator.CombatSuppressed);
        Assert.False(coordinator.Heartbeat("token").CombatSuppressed);
    }

    [Fact]
    public void SuppressionThatFinishesAfterExpiryCannotAcknowledgeTheLease()
    {
        var now = DateTimeOffset.UtcNow;
        var recovered = false;
        var coordinator = new CoppeliaPowerlevelLeaseCoordinator(() => now,
            () => LeaseHarness.ValidEnvironment, _ => recovered = true,
            () => { now += TimeSpan.FromSeconds(6); return true; });
        Assert.False(coordinator.Acquire("token").CombatSuppressed);
        Assert.True(coordinator.IsLeaseActive);
        Assert.False(coordinator.CombatSuppressed);
        Assert.False(recovered);
        Assert.Contains("expired", coordinator.LeaseReason);
    }

    [Fact]
    public void MissingAdditiveAcknowledgementDefaultsToFalse()
    {
        var response = JsonSerializer.Deserialize<CoppeliaPowerlevelResponse>(
            """{"ok":true,"reason":"legacy ownership","contractVersion":1}""", CoppeliaPowerlevelContract.JsonOptions)!;
        Assert.True(response.Ok);
        Assert.False(response.CombatSuppressed);
    }

    [Fact]
    public void ManualOffCanClearAnExpiredFenceWithoutAutomaticRecovery()
    {
        var harness = new LeaseHarness();
        harness.Coordinator.Acquire("token-a");
        harness.Now += TimeSpan.FromSeconds(5);
        harness.Coordinator.Update();
        Assert.True(harness.Coordinator.CombatSuppressed);
        harness.Now += TimeSpan.FromMilliseconds(1);
        harness.Coordinator.Update();
        Assert.False(harness.Coordinator.CombatSuppressed);
        harness.Coordinator.ManualFrenRiderDisable();
        Assert.False(harness.Coordinator.IsLeaseActive);
        Assert.Empty(harness.Recoveries);
        Assert.True(harness.Coordinator.Acquire("token-b").CombatSuppressed);
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
    public void ActiveHyperFocusBlocksAcquire()
    {
        var harness = new LeaseHarness
        {
            Environment = LeaseHarness.ValidEnvironment with { HyperFocusLeaseActive = true },
        };

        var response = harness.Coordinator.Acquire("token-a");

        Assert.False(response.Ok);
        Assert.Contains("hyper focus", response.Reason, StringComparison.OrdinalIgnoreCase);
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
                reason => Recoveries.Add(reason),
                () => { SuppressionCalls++; return SuppressionConfirmed; });
        }

        public DateTimeOffset Now { get; set; } = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
        public CoppeliaPowerlevelEnvironment Environment { get; init; } = ValidEnvironment;
        public List<string> Recoveries { get; } = new();
        public bool SuppressionConfirmed { get; set; } = true;
        public int SuppressionCalls { get; private set; }
        public CoppeliaPowerlevelLeaseCoordinator Coordinator { get; }
    }
}
