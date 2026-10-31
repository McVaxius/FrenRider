using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

[Collection("Activation mode static services")]
public sealed class AdsDutyOwnershipTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CofferSettingDoesNotRequireDutyOwnershipOrCatalogMetadata(bool enabled)
    {
        using var service = CreateService(
            () => true, () => false,
            () => $$"""{"considerTreasureCoffers":{{enabled.ToString().ToLowerInvariant()}}}""",
            () => DateTime.UtcNow);

        service.Refresh(true, 4242, 88, force: true);

        Assert.Equal(enabled, service.ConsiderTreasureCoffers);
        Assert.False(service.Current.IsOwned);
        Assert.Null(service.CurrentDuty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"considerTreasureCoffers":null}""")]
    [InlineData("""{"considerTreasureCoffers":"true"}""")]
    [InlineData("""{"considerTreasureCoffers":1}""")]
    public void UnreadableCofferSettingCannotRetainEnabledAuthority(string unreadable)
    {
        var json = """{"considerTreasureCoffers":true}""";
        using var service = CreateService(() => true, () => true, () => json, () => DateTime.UtcNow);
        service.Refresh(true, 4242, 88, force: true);
        Assert.True(service.ConsiderTreasureCoffers);

        json = unreadable;
        service.Refresh(true, 4242, 88, force: true);

        Assert.Null(service.ConsiderTreasureCoffers);
        Assert.True(service.Current.IsOwned);
    }

    [Fact]
    public void JsonFailureRetainsCurrentDutyWithoutRetainingCofferAuthority()
    {
        var unavailable = false;
        using var service = CreateService(() => true, () => false,
            () => unavailable ? throw new InvalidOperationException("fabricated IPC failure")
                : """{"considerTreasureCoffers":true,"inInstancedDuty":true,"hasCatalogMetadata":true,"duty":"Fabricated Duty","territoryTypeId":4242,"contentFinderConditionId":88,"dutyCategory":"FourMan","supportLevel":"PassiveOnly","clearanceStatus":"FourPlayerSyncCleared"}""",
            () => DateTime.UtcNow);
        service.Refresh(true, 4242, 88, force: true);
        var duty = service.CurrentDuty;
        Assert.NotNull(duty);
        Assert.True(service.ConsiderTreasureCoffers);

        unavailable = true;
        service.Refresh(true, 4242, 88, force: true);

        Assert.Same(duty, service.CurrentDuty);
        Assert.Null(service.ConsiderTreasureCoffers);
    }

    [Fact]
    public void CofferSettingUsesExistingPollCadenceAndClearsOnUnload()
    {
        var now = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        var enabled = true;
        var loaded = true;
        var queries = 0;
        using var service = CreateService(() => loaded, () => false,
            () => { queries++; return $$"""{"considerTreasureCoffers":{{enabled.ToString().ToLowerInvariant()}}}"""; },
            () => now);
        service.Refresh(true, 4242, 88);
        enabled = false;
        now = now.AddMilliseconds(249);
        service.Refresh(true, 4242, 88);
        Assert.True(service.ConsiderTreasureCoffers);
        Assert.Equal(1, queries);

        now = now.AddMilliseconds(1);
        service.Refresh(true, 4242, 88);
        Assert.False(service.ConsiderTreasureCoffers);
        Assert.Equal(2, queries);

        loaded = false;
        now += AdsDutyIpcService.PollInterval;
        service.Refresh(true, 4242, 88);
        Assert.Null(service.ConsiderTreasureCoffers);
        Assert.Equal(2, queries);
    }

    [Theory]
    [InlineData(ObjectKind.EventObj, "Treasure Coffer")]
    [InlineData(ObjectKind.EventObj, "Ornate Coffer")]
    [InlineData(ObjectKind.EventObj, "Staging Coffer: Gilded Strongbox")]
    [InlineData(ObjectKind.EventObj, "Treasure Chest")]
    [InlineData(ObjectKind.EventObj, "Wooden Chest")]
    [InlineData(ObjectKind.EventObj, "Chest")]
    [InlineData(ObjectKind.Treasure, "Treasure Coffer")]
    [InlineData(ObjectKind.Treasure, "Ornate Coffer")]
    [InlineData(ObjectKind.Treasure, "Treasure Chest")]
    public void OffFiltersLootBeforeCustomKeywordsAndRepeatInteractions(ObjectKind kind, string name)
    {
        using var fixture = new CofferInteractionFixture();
        var coffer = fixture.Add(name, kind, 1);
        fixture.SetField("interactableKeywords", new List<string> { name });
        fixture.SetField("lastInteractedEntityId", (uint)coffer.GameObjectId);
        fixture.SetCoffers(true);
        Assert.Same(coffer, fixture.FindNearest());

        fixture.SetCoffers(false);
        Assert.Null(fixture.FindNearest());
        fixture.SetCoffers(null);
        Assert.Null(fixture.FindNearest());
    }

    [Theory]
    [InlineData(ObjectKind.EventObj, "Vault Door", "door")]
    [InlineData(ObjectKind.Treasure, "Vault Door", "door")]
    [InlineData(ObjectKind.Treasure, "Sluice Gate", "gate")]
    [InlineData(ObjectKind.Treasure, "Elaborate Gate", "gate")]
    [InlineData(ObjectKind.Treasure, "Stage Door", "door")]
    [InlineData(ObjectKind.Treasure, "Arcane Sphere", "sphere")]
    [InlineData(ObjectKind.Treasure, "Hypnoslot Machine", "hypnoslot")]
    [InlineData(ObjectKind.EventObj, "Magitek Console", "console")]
    [InlineData(ObjectKind.Treasure, "Treasure Portal", "portal")]
    [InlineData(ObjectKind.EventObj, "Ancient Mechanism", "mechanism")]
    [InlineData(ObjectKind.EventObj, "Treasure Beacon", "beacon")]
    [InlineData(ObjectKind.Treasure, "Treasure Beacon", "beacon")]
    [InlineData(ObjectKind.Treasure, "Ancient Mechanism", "mechanism")]
    [InlineData(ObjectKind.Treasure, "Mysterious Cache", "cache")]
    [InlineData(ObjectKind.Treasure, "Treasure Console", "console")]
    [InlineData(ObjectKind.EventObj, "Chestnut Door", "door")]
    public void OffPreservesProgressionAndCustomNonLootMatches(ObjectKind kind, string name, string keyword)
    {
        using var fixture = new CofferInteractionFixture();
        fixture.Add("Treasure Coffer", ObjectKind.EventObj, 1);
        var progression = fixture.Add(name, kind, 2);
        fixture.SetField("interactableKeywords", new List<string> { "coffer", keyword });
        fixture.SetCoffers(false);

        Assert.Same(progression, fixture.FindNearest());
    }

    [Fact]
    public void OffStopsAnOwnedCofferApproachOnce()
    {
        using var fixture = new CofferInteractionFixture();
        var coffer = fixture.Add("Treasure Coffer", ObjectKind.Treasure, 12);
        fixture.SetCoffers(true);
        fixture.Call("NavigateToInteractable", coffer, Vector3.Zero, 12f, DateTime.UtcNow);
        Assert.StartsWith("/vnav moveto ", Assert.Single(fixture.Commands));
        fixture.Commands.Clear();

        fixture.SetCoffers(false);
        fixture.Call("ApplyCofferPolicy");
        fixture.Call("ApplyCofferPolicy");

        Assert.Equal(new[] { "/vnavmesh stop" }, fixture.Commands);
        Assert.Null(fixture.FindNearest());
        Assert.False((bool)fixture.Field("isNavigatingToInteractable")!);
    }

    [Theory]
    [InlineData("Vault Door", ObjectKind.Treasure)]
    [InlineData("Magitek Console", ObjectKind.EventObj)]
    public void OffDoesNotStopOwnedProgressionNavigation(string name, ObjectKind kind)
    {
        using var fixture = new CofferInteractionFixture();
        var progression = fixture.Add(name, kind, 12);
        fixture.SetCoffers(true);
        fixture.Call("NavigateToInteractable", progression, Vector3.Zero, 12f, DateTime.UtcNow);
        fixture.Commands.Clear();

        fixture.SetCoffers(false);
        fixture.Call("ApplyCofferPolicy");

        Assert.Empty(fixture.Commands);
        Assert.True((bool)fixture.Field("isNavigatingToInteractable")!);
    }

    [Theory]
    [InlineData("Vault Door")]
    [InlineData("Exit Portal")]
    public void CompletedRaidStopsProgressionNavigationBeforeOrdinaryDutyInteractionResumes(string name)
    {
        using var fixture = new CofferInteractionFixture();
        var exit = fixture.Add(name, ObjectKind.EventObj, 12);
        fixture.Call("NavigateToInteractable", exit, Vector3.Zero, 12f, DateTime.UtcNow);
        fixture.SetField("lastInteractedEntityId", (uint)exit.GameObjectId);
        fixture.Commands.Clear();
        fixture.ConfigureRaid(enabled: true, completed: true);

        fixture.Update();
        fixture.Update();

        Assert.Equal(new[] { "/vnavmesh stop" }, fixture.Commands);
        Assert.False((bool)fixture.Field("isNavigatingToInteractable")!);
        Assert.Null(fixture.Field("navigatingInteractableGameObjectId"));
        Assert.Null(fixture.Field("lastInteractedEntityId"));
        Assert.False(fixture.IsActive);
        Assert.Equal("Remaining until manually exited", fixture.StateDetail);
    }

    [Fact]
    public void CompletedRaidBlocksFinalInteractionAndMovementDispatchAfterAdsReleasesOwnership()
    {
        using var fixture = new CofferInteractionFixture();
        var exit = fixture.Add("Exit Portal", ObjectKind.EventObj, 12);
        fixture.ConfigureRaid(enabled: true, completed: true);
        var plugin = (Plugin)fixture.Field("plugin")!;
        Assert.True(plugin.AdsIntegrationService.DutySession.IsCompleted);
        Assert.False(plugin.AdsIntegrationService.ShouldPauseDutySystems);
        var now = DateTime.UtcNow;

        fixture.Call("InteractWithObject", exit, now);
        fixture.Call("IssueInteractableNavCommand", exit, "fabricated pending exit");
        fixture.Call("NavigateToInteractable", exit, Vector3.Zero, 12f, now);
        fixture.Call("NudgeForward");
        Assert.False((bool)fixture.Call("TryMountTowardInteractable", new CharacterConfig(), exit, 12f, now)!);

        Assert.Empty(fixture.Commands);
        Assert.False((bool)fixture.Field("isNavigatingToInteractable")!);
        Assert.Null(fixture.Field("lastInteractedEntityId"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void RaidCompletionGuardPreservesOrdinaryAndDuringPullNavigation(bool enabled, bool completed)
    {
        using var fixture = new CofferInteractionFixture();
        var door = fixture.Add("Vault Door", ObjectKind.EventObj, 12);
        fixture.ConfigureRaid(enabled, completed);

        fixture.Call("NavigateToInteractable", door, Vector3.Zero, 12f, DateTime.UtcNow);

        Assert.StartsWith("/vnav moveto ", Assert.Single(fixture.Commands));
        Assert.True((bool)fixture.Field("isNavigatingToInteractable")!);
    }

    [Fact]
    public void OffDoesNotStopForwardNudgeOrOrdinaryFollow()
    {
        using var fixture = new CofferInteractionFixture();
        fixture.SetCoffers(false);
        fixture.Call("NudgeForward");
        fixture.Commands.Clear();
        fixture.Call("ApplyCofferPolicy");
        Assert.Empty(fixture.Commands);

        fixture.SetField("isNavigatingToInteractable", false);
        fixture.Follow.GetType().GetProperty(nameof(FollowService.State))!.SetValue(fixture.Follow, FollowState.Following);
        fixture.Call("ApplyCofferPolicy");
        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public void FollowingStateCannotHideAnOwnedCofferPathBeforeFollowReacquiresMovement()
    {
        using var fixture = new CofferInteractionFixture();
        var coffer = fixture.Add("Treasure Coffer", ObjectKind.Treasure, 12);
        fixture.SetCoffers(true);
        fixture.Call("NavigateToInteractable", coffer, Vector3.Zero, 12f, DateTime.UtcNow);
        fixture.Commands.Clear();
        fixture.Follow.GetType().GetProperty(nameof(FollowService.State))!.SetValue(fixture.Follow, FollowState.Following);
        fixture.SetCoffers(false);
        fixture.Call("ApplyCofferPolicy");

        Assert.Equal(new[] { "/vnavmesh stop" }, fixture.Commands);
        Assert.False((bool)fixture.Field("isNavigatingToInteractable")!);
    }

    [Fact]
    public void DisappearingCofferStopsItsPathBeforeOwnershipIsCleared()
    {
        using var fixture = new CofferInteractionFixture();
        var coffer = fixture.Add("Treasure Coffer", ObjectKind.Treasure, 12);
        fixture.SetCoffers(true);
        fixture.Call("NavigateToInteractable", coffer, Vector3.Zero, 12f, DateTime.UtcNow);
        fixture.Commands.Clear();
        fixture.ClearObjects();
        Assert.Null(fixture.FindNearest());

        fixture.Call("ClearUnavailableInteractable");
        fixture.SetCoffers(false);
        fixture.Call("ApplyCofferPolicy");

        Assert.Equal(new[] { "/vnavmesh stop" }, fixture.Commands);
        Assert.False((bool)fixture.Field("isNavigatingToInteractable")!);
        Assert.False((bool)fixture.Field("navigatingToTreasureCoffer")!);
    }

    [Fact]
    public void ResetBeforeOffStopsCofferPathBeforeOwnershipIsCleared()
    {
        using var fixture = new CofferInteractionFixture();
        var coffer = fixture.Add("Treasure Coffer", ObjectKind.Treasure, 12);
        fixture.SetCoffers(true);
        fixture.Call("NavigateToInteractable", coffer, Vector3.Zero, 12f, DateTime.UtcNow);
        fixture.Commands.Clear();

        fixture.Call("Reset");
        fixture.SetCoffers(false);
        fixture.Call("ApplyCofferPolicy");

        Assert.Equal(new[] { "/vnavmesh stop" }, fixture.Commands);
        Assert.False((bool)fixture.Field("isNavigatingToInteractable")!);
    }

    [Theory]
    [InlineData("Reset")]
    [InlineData("ClearUnavailableInteractable")]
    [InlineData("ReleaseCofferNavigationForFollow")]
    public void CofferCleanupDoesNotStopProgressionNavigation(string cleanup)
    {
        using var fixture = new CofferInteractionFixture();
        var progression = fixture.Add("Treasure Beacon", ObjectKind.Treasure, 12);
        fixture.SetCoffers(true);
        fixture.Call("NavigateToInteractable", progression, Vector3.Zero, 12f, DateTime.UtcNow);
        fixture.Commands.Clear();

        fixture.Call(cleanup);
        fixture.SetCoffers(false);
        fixture.Call("ApplyCofferPolicy");

        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public void FollowReleasesOnlyOwnedCofferBeforeReplacementNavigation()
    {
        using var fixture = new CofferInteractionFixture();
        var coffer = fixture.Add("Treasure Coffer", ObjectKind.Treasure, 12);
        fixture.SetCoffers(true);
        fixture.Call("NavigateToInteractable", coffer, Vector3.Zero, 12f, DateTime.UtcNow);
        fixture.Commands.Clear();

        fixture.CallFollow("IssueNavCommand", new CharacterConfig { ClingType = 0 },
            new Vector3(20, 0, 0), false, "fabricated follow replacement", true);
        Assert.Equal(2, fixture.Commands.Count);
        Assert.Equal("/vnavmesh stop", fixture.Commands[0]);
        Assert.StartsWith("/vnav moveto ", fixture.Commands[1]);
        fixture.Commands.Clear();
        fixture.Call("Reset");
        fixture.SetCoffers(false);
        fixture.Call("ApplyCofferPolicy");

        Assert.Empty(fixture.Commands);
    }

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

    [Theory]
    [InlineData("logout")]
    [InlineData("missing player")]
    [InlineData("death")]
    [InlineData("unconscious")]
    [InlineData("BetweenAreas")]
    [InlineData("BetweenAreas51")]
    [InlineData("WatchingCutscene")]
    [InlineData("WatchingCutscene78")]
    [InlineData("OccupiedInCutSceneEvent")]
    [InlineData("duty re-entry")]
    [InlineData("re-enable")]
    [InlineData("cancellation")]
    [InlineData("territory change")]
    [InlineData("CFC change")]
    public void AutomaticSoloCombatWaitsForContinuousReadinessAndConfirmedOwnership(string interruption)
    {
        // Exercise the same handoff state, IPC contract and combat authority
        // together, with a controllable clock and no live command dispatch.
        foreach (var rotationType in new[] { 0, 2 })
        {
            var epoch = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
            var now = epoch;
            var handoff = new AdsHandoffState();
            var authority = new DutyCombatAuthorityPolicy();
            var owned = false;
            var acceptStart = false;
            var requests = 0;
            var activations = 0;
            var bootstraps = 0;
            var suppressions = 0;
            var rotationActive = true;
            uint territory = 100;
            uint cfc = 1;
            var requestedAt = DateTime.MinValue;
            var nextAttempt = DateTime.MinValue;
            var ipc = CreateService(() => true, () => owned, () => "{}", () => now, () =>
            {
                requests++;
                return acceptStart;
            });

            // Account for an earlier bootstrap before ADS classified the duty.
            Assert.True(authority.Update(new DutyCombatAuthorityInput(
                true, true, false, null, false, false, true)).ShouldBootstrapFrenRider);
            authority.MarkFrenRiderBootstrapComplete();

            AdsHandoffCountdownResult Frame(double seconds, AdsHandoffReadinessConditions conditions,
                bool bootstrapAllowed = true)
            {
                now = epoch.AddSeconds(seconds);
                handoff.ObserveReadiness(conditions);
                var ownership = ipc.Refresh(true, territory, cfc, force: true);
                if (!ownership.IsOwned && requestedAt != DateTime.MinValue
                    && !AdsIntegrationPolicy.IsHandoffConfirmationPending(requestedAt, now))
                {
                    requestedAt = DateTime.MinValue;
                    handoff.ResetCountdown();
                }
                var countdown = handoff.Update(territory, cfc, now, 10, conditions,
                    automaticSoloHandoff: true, ownershipConfirmed: ownership.IsOwned && ownership.StatusReadable);
                if (countdown.IsReady && !ownership.IsOwned
                    && AdsIntegrationPolicy.CanAttemptHandoff(requestedAt, nextAttempt, now))
                {
                    var request = ipc.RequestStartDutyFromInside();
                    requestedAt = request.Accepted ? now : DateTime.MinValue;
                    nextAttempt = now + AdsIntegrationPolicy.HandoffConfirmationTimeout;
                    if (!request.Accepted)
                        handoff.ResetCountdown();
                }

                var decision = authority.Update(new DutyCombatAuthorityInput(
                    true, true, true, AdsDutyCategory.Solo, true, true,
                    bootstrapAllowed, handoff.IsCombatHeld));
                if (decision.ShouldForceCombatOff)
                {
                    suppressions++;
                    rotationActive = false;
                }
                if (decision.ShouldBootstrapFrenRider)
                {
                    bootstraps++;
                    if (CombatService.ShouldActivateConfiguredRotation(rotationType))
                    {
                        activations++;
                        rotationActive = true;
                    }
                    authority.MarkFrenRiderBootstrapComplete();
                }
                return countdown;
            }

            void AssertHeld(int expectedRequests)
            {
                Assert.True(handoff.IsCombatHeld);
                Assert.False(rotationActive);
                Assert.False(authority.FrenRiderBootstrapComplete);
                Assert.Equal(0, activations);
                Assert.Equal(0, bootstraps);
                Assert.Equal(expectedRequests, requests);
            }

            Assert.Equal(TimeSpan.FromSeconds(10), Frame(0, ReadyConditions).Remaining);
            Assert.Equal(1, suppressions);
            AssertHeld(0);
            Frame(9, ReadyConditions);
            AssertHeld(0);

            var unsafeConditions = interruption switch
            {
                "logout" => ReadyConditions with { IsLoggedIn = false },
                "missing player" => ReadyConditions with { HasLocalPlayer = false },
                "death" => ReadyConditions with { IsPlayerAlive = false },
                "unconscious" => ReadyConditions with { IsUnconscious = true },
                "BetweenAreas" => ReadyConditions with { IsBetweenAreas = true },
                "BetweenAreas51" => ReadyConditions with { IsBetweenAreas51 = true },
                "WatchingCutscene" => ReadyConditions with { IsWatchingCutscene = true },
                "WatchingCutscene78" => ReadyConditions with { IsWatchingCutscene78 = true },
                "OccupiedInCutSceneEvent" => ReadyConditions with { IsOccupiedInCutSceneEvent = true },
                _ => ReadyConditions,
            };

            if (interruption is "duty re-entry" or "re-enable" or "cancellation")
            {
                handoff.Reset();
                authority.Update(new DutyCombatAuthorityInput(
                    interruption != "re-enable", interruption != "duty re-entry", false,
                    AdsDutyCategory.Solo, false, false, false));
                Assert.False(handoff.IsCombatHeld);
            }
            else if (interruption == "territory change")
                territory++;
            else if (interruption == "CFC change")
                cfc++;
            else if (interruption is "BetweenAreas" or "BetweenAreas51")
            {
                // Framework early return: no normal ADS or combat update runs.
                handoff.ObserveReadiness(unsafeConditions);
                AssertHeld(0);
            }
            else
            {
                Assert.NotNull(Frame(9.5, unsafeConditions).Blocker);
                AssertHeld(0);
            }

            Assert.Equal(TimeSpan.FromSeconds(10), Frame(20, ReadyConditions).Remaining);
            Frame(29.999, ReadyConditions);
            AssertHeld(0);
            Frame(30, ReadyConditions); // Rejected start must not release combat.
            AssertHeld(1);

            acceptStart = true;
            Assert.Equal(TimeSpan.FromSeconds(10), Frame(31, ReadyConditions).Remaining);
            Frame(40.999, ReadyConditions);
            AssertHeld(1);
            Frame(41, ReadyConditions); // Accepted is not confirmed ownership.
            AssertHeld(2);
            Frame(44, ReadyConditions);
            AssertHeld(2);

            Assert.Equal(TimeSpan.FromSeconds(10), Frame(46, ReadyConditions).Remaining);
            Frame(55.999, ReadyConditions);
            AssertHeld(2);
            Frame(56, ReadyConditions); // A timed-out attempt also gets a fresh delay.
            AssertHeld(3);

            // Confirmation during a new cutscene cannot bypass a restarted wait.
            owned = true;
            Frame(60, ReadyConditions with { IsWatchingCutscene78 = true });
            AssertHeld(3);
            Assert.Equal(TimeSpan.FromSeconds(10), Frame(65, ReadyConditions).Remaining);
            Frame(74.999, ReadyConditions);
            AssertHeld(3);

            // The handoff is ready, but existing lease/utility exclusions can
            // still defer bootstrap without consuming it.
            Frame(75, ReadyConditions, bootstrapAllowed: false);
            Assert.False(handoff.IsCombatHeld);
            Assert.False(authority.FrenRiderBootstrapComplete);
            Assert.Equal(0, bootstraps);
            Frame(76, ReadyConditions);
            Frame(77, ReadyConditions);
            Assert.Equal(1, bootstraps);
            Assert.True(authority.FrenRiderBootstrapComplete);
            Assert.Equal(rotationType == 2 ? 0 : 1, activations);
            Assert.Equal(rotationType != 2, rotationActive);
            Assert.Equal(3, requests);
        }
    }

    private static AdsHandoffReadinessConditions ReadyConditions => new(
        IsLoggedIn: true,
        HasLocalPlayer: true,
        IsPlayerAlive: true,
        IsUnconscious: false,
        IsBetweenAreas: false,
        IsWatchingCutscene: false,
        IsOccupiedInCutSceneEvent: false);

    private sealed class CofferInteractionFixture : IDisposable
    {
        private readonly Dictionary<PropertyInfo, object?> savedStatics = new();
        private readonly List<IGameObject> objects = new();
        private string statusJson = "{}";
        private readonly AdsDutyIpcService ads;
        private readonly DutyInteractService interaction;
        private RaidModeService? raid;
        internal readonly FollowService Follow = (FollowService)RuntimeHelpers.GetUninitializedObject(typeof(FollowService));
        internal readonly List<string> Commands = new();
        internal bool IsActive => interaction.IsActive;
        internal string StateDetail => interaction.StateDetail;

        internal CofferInteractionFixture()
        {
            ads = CreateService(() => true, () => false, () => statusJson, () => DateTime.UtcNow);
            var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
            typeof(Plugin).GetProperty(nameof(Plugin.AdsDutyIpcService))!.SetValue(plugin, ads);
            typeof(Plugin).GetProperty(nameof(Plugin.FollowService))!.SetValue(plugin, Follow);
            interaction = (DutyInteractService)RuntimeHelpers.GetUninitializedObject(typeof(DutyInteractService));
            typeof(Plugin).GetProperty(nameof(Plugin.DutyInteractService))!.SetValue(plugin, interaction);
            typeof(FollowService).GetField("plugin", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Follow, plugin);
            typeof(FollowService).GetField("zoneService", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Follow, new ZoneService());
            SetField("plugin", plugin);
            SetField("interactableKeywords", new List<string>());
            typeof(DutyInteractService).GetProperty(nameof(DutyInteractService.IsActive))!.SetValue(interaction, true);

            var player = Proxy<IPlayerCharacter>((method, _) => method.Name switch
            {
                "get_Position" => Vector3.Zero,
                "get_Rotation" => 0f,
                _ => Default(method.ReturnType),
            });
            ReplaceStatic("ObjectTable", Proxy<IObjectTable>((method, _) => method.Name switch
            {
                "get_LocalPlayer" => player,
                "GetEnumerator" => objects.GetEnumerator(),
                _ => Default(method.ReturnType),
            }));
            ReplaceStatic("Log", Proxy<IPluginLog>((_, _) => null));
            ReplaceStatic("CommandManager", Proxy<ICommandManager>((method, args) =>
            {
                if (method.Name != "ProcessCommand") return Default(method.ReturnType);
                Commands.Add((string)args![0]!);
                return true;
            }));
        }

        internal IGameObject Add(string name, ObjectKind kind, float distance)
        {
            var gameObject = Proxy<IGameObject>((method, _) => method.Name switch
            {
                "get_Name" => new SeStringBuilder().AddText(name).Build(),
                "get_ObjectKind" => kind,
                "get_IsTargetable" => true,
                "get_GameObjectId" => (ulong)distance,
                "get_Position" => new Vector3(distance, 0, 0),
                _ => Default(method.ReturnType),
            });
            objects.Add(gameObject);
            return gameObject;
        }

        internal void SetCoffers(bool? enabled)
        {
            statusJson = enabled.HasValue
                ? $$"""{"considerTreasureCoffers":{{enabled.Value.ToString().ToLowerInvariant()}}}"""
                : "{}";
            ads.Refresh(true, 4242, 88, force: true);
        }

        internal void ConfigureRaid(bool enabled, bool completed)
        {
            var plugin = (Plugin)Field("plugin")!;
            var config = new CharacterConfig { Enabled = true };
            var raidService = new RaidModeService(() => config, () => { }, () => new RaidRuntimeFrame
            {
                LoggedIn = true, CharacterId = 701, TerritoryId = 100, DutyId = 1,
                InDuty = true, Ready = true, LootReadable = true, DutyCompleted = completed,
            }, () => true, () => false);
            raid = raidService;
            if (enabled)
                raidService.Enable();
            var integration = new AdsIntegrationService(ads, () => config, () => { }, _ => true,
                _ => { }, _ => { }, raidModeEnabled: () => raidService.IsEnabled);
            typeof(Plugin).GetProperty(nameof(Plugin.RaidModeService))!.SetValue(plugin, raidService);
            typeof(Plugin).GetProperty(nameof(Plugin.AdsIntegrationService))!.SetValue(plugin, integration);
            if (completed)
                integration.OnDutyCompleted(100, DateTime.UtcNow);
            if (enabled)
                raidService.Update();
        }

        internal void Update() => interaction.Update();
        internal IGameObject? FindNearest() => (IGameObject?)Call("FindNearestInteractable");
        internal void ClearObjects() => objects.Clear();
        internal object? Call(string method, params object?[] args)
            => typeof(DutyInteractService).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(interaction, args);
        internal object? CallFollow(string method, params object?[] args)
            => typeof(FollowService).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Follow, args);
        internal object? Field(string name)
            => typeof(DutyInteractService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(interaction);
        internal void SetField(string name, object value)
            => typeof(DutyInteractService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(interaction, value);
        private void ReplaceStatic(string name, object value)
        {
            var property = typeof(Plugin).GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic)!;
            savedStatics[property] = property.GetValue(null);
            property.SetValue(null, value);
        }

        private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
        {
            var proxy = DispatchProxy.Create<T, QuestionableTestProxy>();
            ((QuestionableTestProxy)(object)proxy).Handler = handler;
            return proxy;
        }

        private static object? Default(Type type)
            => type == typeof(void) || !type.IsValueType ? null : Activator.CreateInstance(type);
        public void Dispose()
        {
            raid?.Dispose();
            foreach (var (property, value) in savedStatics) property.SetValue(null, value);
            ads.Dispose();
        }
    }

    private static AdsDutyIpcService CreateService(
        Func<bool> loaded,
        Func<bool> typed,
        Func<string> json,
        Func<DateTime> now,
        Func<bool>? start = null)
        => new(loaded, typed, json, start ?? (() => true), now);
}
