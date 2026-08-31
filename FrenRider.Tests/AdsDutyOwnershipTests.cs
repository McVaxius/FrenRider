using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class AdsDutyOwnershipTests
{
    [Fact]
    public void TypedOwnershipIsAuthoritative()
    {
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
        var jsonCalls = 0;
        var service = CreateService(
            () => true,
            () => true,
            () =>
            {
                jsonCalls++;
                return """{"inInstancedDuty":true,"ownershipMode":"Observing","hasCatalogMetadata":true,"duty":"Sastasha","territoryTypeId":1036,"contentFinderConditionId":4,"dutyCategory":"FourMan","supportLevel":"PassiveOnly","clearanceStatus":"FourPlayerSyncCleared"}""";
            },
            () => now);

        var snapshot = service.Refresh(true, 1036, 4, force: true);

        Assert.True(snapshot.IsOwned);
        Assert.True(snapshot.StatusReadable);
        Assert.Equal(AdsDutyOwnershipSource.Typed, snapshot.Source);
        Assert.Equal(1, jsonCalls);
        Assert.Equal("Sastasha", service.CurrentDuty?.DutyName);
    }

    [Theory]
    [InlineData("OwnedStartOutside", true)]
    [InlineData("OwnedStartInside", true)]
    [InlineData("OwnedResumeInside", true)]
    [InlineData("Leaving", true)]
    [InlineData("Observing", false)]
    [InlineData("Idle", false)]
    [InlineData("Failed", false)]
    public void JsonFallbackRequiresInDutyAndOwnedOrLeavingMode(string mode, bool expected)
    {
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
        var service = CreateService(
            () => true,
            () => throw new InvalidOperationException("typed unavailable"),
            () => $$"""{"inInstancedDuty":true,"ownershipMode":"{{mode}}"}""",
            () => now);

        var snapshot = service.Refresh(true, 1036, 4, force: true);

        Assert.Equal(expected, snapshot.IsOwned);
        Assert.True(snapshot.StatusReadable);
        Assert.Equal(AdsDutyOwnershipSource.JsonFallback, snapshot.Source);
    }

    [Fact]
    public void JsonFallbackNeverOwnsOutsideDuty()
    {
        Assert.True(AdsDutyIpcService.TryParseFallbackStatus(
            """{"inInstancedDuty":false,"ownershipMode":"OwnedStartOutside"}""",
            out var inDuty,
            out var mode,
            out var owned));
        Assert.False(inDuty);
        Assert.Equal("OwnedStartOutside", mode);
        Assert.False(owned);
    }

    [Fact]
    public void TransientFailuresHoldOwnedStateForFiveSeconds()
    {
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
        var typedFails = false;
        var service = CreateService(
            () => true,
            () => typedFails ? throw new InvalidOperationException("typed failed") : true,
            () => throw new InvalidOperationException("json failed"),
            () => now);

        Assert.True(service.Refresh(true, 1036, 4, force: true).IsOwned);

        typedFails = true;
        now = now.AddSeconds(4);
        var held = service.Refresh(true, 1036, 4, force: true);
        Assert.True(held.IsOwned);
        Assert.False(held.StatusReadable);
        Assert.Equal(AdsDutyOwnershipSource.StaleHold, held.Source);

        now = now.AddSeconds(2);
        var expired = service.Refresh(true, 1036, 4, force: true);
        Assert.False(expired.IsOwned);
        Assert.Equal(AdsDutyOwnershipSource.Unreadable, expired.Source);
    }

    [Fact]
    public void UnloadAndExplicitNotOwnedClearImmediately()
    {
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
        var loaded = true;
        var owned = true;
        var service = CreateService(
            () => loaded,
            () => owned,
            () => throw new InvalidOperationException(),
            () => now);

        Assert.True(service.Refresh(true, 1036, 4, force: true).IsOwned);

        owned = false;
        Assert.False(service.Refresh(true, 1036, 4, force: true).IsOwned);
        Assert.True(service.Current.StatusReadable);

        owned = true;
        Assert.True(service.Refresh(true, 1036, 4, force: true).IsOwned);
        loaded = false;
        Assert.False(service.Refresh(true, 1036, 4, force: true).IsOwned);
        Assert.Equal(AdsDutyOwnershipSource.Unloaded, service.Current.Source);
    }

    [Fact]
    public void StartRequestDistinguishesRejectionFromUnavailableEndpoint()
    {
        var rejected = CreateService(
            () => true,
            () => false,
            () => string.Empty,
            () => DateTime.UtcNow,
            () => false).RequestStartDutyFromInside();
        Assert.True(rejected.EndpointAvailable);
        Assert.False(rejected.Accepted);

        var unavailable = CreateService(
            () => true,
            () => false,
            () => string.Empty,
            () => DateTime.UtcNow,
            () => throw new InvalidOperationException("missing")).RequestStartDutyFromInside();
        Assert.False(unavailable.EndpointAvailable);
    }

    [Fact]
    public void AcceptedStartRequestReturnsExplicitAcknowledgement()
    {
        var result = CreateService(
            () => true,
            () => false,
            () => string.Empty,
            () => DateTime.UtcNow,
            () => true).RequestStartDutyFromInside();

        Assert.True(result.EndpointAvailable);
        Assert.True(result.Accepted);
        Assert.Contains("accepted", result.Detail);
    }

    [Fact]
    public void ManualRuntimeOwnershipPausesRegardlessOfAutomaticHandoff()
        => Assert.True(AdsIntegrationPolicy.ShouldPauseDutySystems(
            handoffPending: false,
            runtimeOwned: true,
            exitTakeoverActive: false));

    [Fact]
    public void ObservingModeDoesNotPauseWithoutAutomaticHandoff()
        => Assert.False(AdsIntegrationPolicy.ShouldPauseDutySystems(
            handoffPending: false,
            runtimeOwned: false,
            exitTakeoverActive: false));

    [Fact]
    public void HandoffWaitsFiveSecondsBeforeRetry()
    {
        var requested = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(AdsIntegrationPolicy.CanAttemptHandoff(requested, requested, requested.AddSeconds(4.999)));
        Assert.True(AdsIntegrationPolicy.CanAttemptHandoff(requested, requested.AddSeconds(5), requested.AddSeconds(5)));
    }

    [Fact]
    public void ContinuousReadinessCountdownExpiresAtTheExactConfiguredDelay()
    {
        var startedAt = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
        var state = default(AdsHandoffCountdownState);

        var started = AdsIntegrationPolicy.EvaluateHandoffCountdown(
            state, 1036, 4, startedAt, 10, ReadyConditions);
        Assert.False(started.IsReady);
        Assert.Equal(TimeSpan.FromSeconds(10), started.Remaining);

        var beforeExpiry = AdsIntegrationPolicy.EvaluateHandoffCountdown(
            started.State, 1036, 4, startedAt.AddSeconds(9.999), 10, ReadyConditions);
        Assert.False(beforeExpiry.IsReady);

        var atExpiry = AdsIntegrationPolicy.EvaluateHandoffCountdown(
            beforeExpiry.State, 1036, 4, startedAt.AddSeconds(10), 10, ReadyConditions);
        Assert.True(atExpiry.IsReady);
        Assert.Equal(TimeSpan.Zero, atExpiry.Remaining);
    }

    [Fact]
    public void ReadinessInterruptionRestartsTheFullCountdown()
    {
        var startedAt = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
        var started = AdsIntegrationPolicy.EvaluateHandoffCountdown(
            default, 1036, 4, startedAt, 10, ReadyConditions);

        var interrupted = AdsIntegrationPolicy.EvaluateHandoffCountdown(
            started.State,
            1036,
            4,
            startedAt.AddSeconds(9),
            10,
            ReadyConditions with { IsWatchingCutscene = true });
        Assert.False(interrupted.IsReady);
        Assert.Equal(DateTime.MinValue, interrupted.State.ReadySinceUtc);

        var restartedAt = startedAt.AddSeconds(10);
        var restarted = AdsIntegrationPolicy.EvaluateHandoffCountdown(
            interrupted.State, 1036, 4, restartedAt, 10, ReadyConditions);
        Assert.False(restarted.IsReady);
        Assert.Equal(restartedAt, restarted.State.ReadySinceUtc);

        var atRestartedExpiry = AdsIntegrationPolicy.EvaluateHandoffCountdown(
            restarted.State, 1036, 4, restartedAt.AddSeconds(10), 10, ReadyConditions);
        Assert.True(atRestartedExpiry.IsReady);
    }

    [Fact]
    public void DutyIdentityChangeRestartsTheFullCountdown()
    {
        var startedAt = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
        var original = AdsIntegrationPolicy.EvaluateHandoffCountdown(
            default, 1036, 4, startedAt, 2, ReadyConditions);

        var changedAt = startedAt.AddSeconds(2);
        var changed = AdsIntegrationPolicy.EvaluateHandoffCountdown(
            original.State, 1037, 5, changedAt, 2, ReadyConditions);

        Assert.False(changed.IsReady);
        Assert.Equal(changedAt, changed.State.ReadySinceUtc);
        Assert.Equal(TimeSpan.FromSeconds(2), changed.Remaining);
    }

    [Theory]
    [InlineData(0, "waiting for login")]
    [InlineData(1, "waiting for local player")]
    [InlineData(2, "waiting for local player to be alive")]
    [InlineData(3, "waiting for unconscious state to clear")]
    [InlineData(4, "waiting for area transition to finish")]
    [InlineData(5, "waiting for cutscene to finish")]
    [InlineData(6, "waiting for cutscene event to finish")]
    public void RuntimeReadinessBlockersResetTheCountdown(int blockerCase, string expectedBlocker)
    {
        var now = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
        var conditions = blockerCase switch
        {
            0 => ReadyConditions with { IsLoggedIn = false },
            1 => ReadyConditions with { HasLocalPlayer = false },
            2 => ReadyConditions with { IsPlayerAlive = false },
            3 => ReadyConditions with { IsUnconscious = true },
            4 => ReadyConditions with { IsBetweenAreas = true },
            5 => ReadyConditions with { IsWatchingCutscene = true },
            6 => ReadyConditions with { IsOccupiedInCutSceneEvent = true },
            _ => throw new ArgumentOutOfRangeException(nameof(blockerCase)),
        };

        var result = AdsIntegrationPolicy.EvaluateHandoffCountdown(
            new AdsHandoffCountdownState(1036, 4, now.AddSeconds(-1)),
            1036,
            4,
            now,
            2,
            conditions);

        Assert.False(result.IsReady);
        Assert.Equal(expectedBlocker, result.Blocker);
        Assert.Equal(DateTime.MinValue, result.State.ReadySinceUtc);
    }

    [Fact]
    public void PendingHandoffPausesDutyAndExitSystems()
    {
        Assert.True(AdsIntegrationPolicy.ShouldPauseDutySystems(
            handoffPending: true,
            runtimeOwned: false,
            exitTakeoverActive: false));
        Assert.True(AdsIntegrationPolicy.ShouldPauseExitSystem(
            handoffPending: true,
            runtimeOwned: false,
            exitTakeoverActive: false));
    }

    [Fact]
    public void ClearingRequestCancelsPendingConfirmation()
    {
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

        Assert.False(AdsIntegrationPolicy.IsHandoffConfirmationPending(DateTime.MinValue, now));
        Assert.True(AdsIntegrationPolicy.CanAttemptHandoff(DateTime.MinValue, DateTime.MinValue, now));
    }

    [Fact]
    public void ExplicitRuntimeReleaseRemovesPause()
        => Assert.False(AdsIntegrationPolicy.ShouldPauseDutySystems(
            handoffPending: false,
            runtimeOwned: false,
            exitTakeoverActive: false));

    [Fact]
    public void ExitOnlyTakeoverKeepsDutySystemsPausedButAllowsExit()
    {
        Assert.True(AdsIntegrationPolicy.ShouldPauseDutySystems(
            handoffPending: false,
            runtimeOwned: false,
            exitTakeoverActive: true));
        Assert.False(AdsIntegrationPolicy.ShouldPauseExitSystem(
            handoffPending: false,
            runtimeOwned: false,
            exitTakeoverActive: true));
    }

    private static AdsHandoffReadinessConditions ReadyConditions => new(
        IsLoggedIn: true,
        HasLocalPlayer: true,
        IsPlayerAlive: true,
        IsUnconscious: false,
        IsBetweenAreas: false,
        IsWatchingCutscene: false,
        IsOccupiedInCutSceneEvent: false);

    private static AdsDutyIpcService CreateService(
        Func<bool> loaded,
        Func<bool> typed,
        Func<string> json,
        Func<DateTime> now,
        Func<bool>? start = null)
        => new(loaded, typed, json, start ?? (() => true), now);
}
