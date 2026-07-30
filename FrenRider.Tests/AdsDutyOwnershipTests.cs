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

    private static AdsDutyIpcService CreateService(
        Func<bool> loaded,
        Func<bool> typed,
        Func<string> json,
        Func<DateTime> now,
        Func<bool>? start = null)
        => new(loaded, typed, json, start ?? (() => true), now);
}
