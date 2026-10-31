using System.Text.Json;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class AdsDutyExitLifecycleTests
{
    private static RaidRuntimeFrame ReadyRaidFrame => new()
    {
        LoggedIn = true, CharacterId = 701, TerritoryId = 100, DutyId = 1,
        InDuty = true, Ready = true, LootReadable = true,
    };

    [Fact]
    public void CountdownRevisionAdvancesOnlyForDemonstrablyNewOrReplacedNativeTimers()
    {
        var config = new CharacterConfig { Enabled = true };
        var frame = ReadyRaidFrame;
        var unreadable = false;
        var starts = 0;
        using var raid = new RaidModeService(() => config, () => { },
            () => unreadable ? throw new InvalidOperationException("Synthetic native read failure") : frame,
            () => { starts++; return true; }, () => false);
        long Revision()
        {
            using var state = JsonDocument.Parse(raid.GetRuntimeStateJson());
            return state.RootElement.GetProperty("countdownRevision").GetInt64();
        }
        raid.Enable();
        Assert.Equal(0, Revision());
        raid.ObserveNativeCountdownEvent(false, 0, true, 10, 0, 17);
        Assert.Equal(1, Revision());
        raid.ObserveNativeCountdownTick(true, 10, 9, frame, frame);
        raid.ObserveNativeCountdownEvent(true, 9, false, 0, 17, 0);
        raid.ObserveNativeCountdownEvent(false, 0, true, float.NaN, 0, 17);
        raid.ObserveNativeCountdownEvent(true, float.NaN, true, 10, 17, 17);
        raid.ObserveNativeCountdownTick(true, float.NaN, 10, frame, frame,
            raid.CancellationEpoch, raid.CancellationEpoch, 17, 18);
        unreadable = true;
        raid.Update();
        Assert.Equal(1, Revision());
        unreadable = false;
        raid.ObserveNativeCountdownEvent(false, 0, true, 20, 0, 17);
        raid.ObserveNativeCountdownEvent(true, 20, true, 30, 17, 17);
        raid.ObserveNativeCountdownTick(true, 5, 6, frame, frame);
        raid.ObserveNativeCountdownEvent(true, 6, true, 6, 17, 18);
        Assert.Equal(5, Revision());
        raid.ObserveNativeCountdownTick(true, .02f, 0, frame, frame);
        raid.Update();
        Assert.Equal(1, starts);
        Assert.Equal(5, Revision());
        raid.OnDutyStarted();
        Assert.Equal(0, Revision());
    }

    [Fact]
    public void OldActiveTimerKeepsItsRevisionAcrossReadFailureAndRejectedCompletionUntilAFreshCountdown()
    {
        var config = new CharacterConfig { Enabled = true };
        var frame = ReadyRaidFrame;
        var unreadable = false;
        var accept = false;
        var revisions = new List<long>();
        RaidModeService? raid = null;
        using var service = raid = new RaidModeService(() => config, () => { },
            () => unreadable ? throw new InvalidOperationException("Synthetic native read failure") : frame,
            () =>
            {
                using var state = JsonDocument.Parse(raid!.GetRuntimeStateJson());
                Assert.True(state.RootElement.GetProperty("automaticStartAllowedNow").GetBoolean());
                revisions.Add(state.RootElement.GetProperty("countdownRevision").GetInt64());
                return accept;
            }, () => false);
        service.Enable();
        service.ObserveNativeCountdownEvent(false, 0, true, 10, 0, 17);
        unreadable = true;
        service.Update();
        unreadable = false;
        service.ObserveNativeCountdownTick(true, .02f, 0, frame, frame);
        service.Update();
        Assert.Equal(new long[] { 1 }, revisions);
        Assert.Equal("ADS start rejected", service.StatusText);
        service.Update();
        Assert.Single(revisions);
        accept = true;
        service.ObserveNativeCountdownEvent(false, 0, true, 10, 0, 17);
        service.ObserveNativeCountdownTick(true, .02f, 0, frame, frame);
        service.Update();
        service.Update();
        Assert.Equal(new long[] { 1, 2 }, revisions);
    }

    [Fact]
    public void NestedNativeCancelAndTimerReplacementCannotMasqueradeAsCompletion()
    {
        var config = new CharacterConfig { Enabled = true };
        var frame = ReadyRaidFrame;
        var starts = 0;
        using var raid = new RaidModeService(() => config, () => { }, () => frame,
            () => { starts++; return true; }, () => false);
        raid.Enable();
        var epochBeforeUpdate = raid.CancellationEpoch;
        // A ReceiveEvent nested in Original Update cancels/clears the active timer.
        raid.ObserveNativeCountdownEvent(true, 0.02f, false, 0, 17, 17);
        raid.ObserveNativeCountdownTick(true, 0.02f, 0, frame, frame,
            epochBeforeUpdate, raid.CancellationEpoch, 17, 17);
        raid.Update();
        Assert.Equal(0, starts);
        Assert.False(RaidModeService.IsNativeCountdownCompletion(true, 20, 0));
        Assert.False(RaidModeService.IsNativeCountdownCompletion(true, 0.02f, 0, 1, 1, 17, 18));
        Assert.False(RaidModeService.IsNativeCountdownCompletion(false, 0.02f, 0));
        Assert.False(RaidModeService.IsNativeCountdownCompletion(true, 0.02f, -10));
        var fresh = raid.CancellationEpoch;
        raid.ObserveNativeCountdownTick(true, 0.02f, 0, frame, frame, fresh, fresh, 17, 17);
        raid.Update();
        Assert.Equal(1, starts);
    }

    [Fact]
    public void ANewNativeCountdownInvalidatesAQueuedCompletionUntilItsOwnFreshFinish()
    {
        var config = new CharacterConfig { Enabled = true };
        var frame = ReadyRaidFrame;
        var starts = 0;
        using var raid = new RaidModeService(() => config, () => { }, () => frame,
            () => { starts++; return true; }, () => false);
        raid.Enable();
        raid.ObserveNativeCountdownTick(true, 0.02f, 0, frame, frame);
        raid.ObserveNativeCountdownEvent(false, 0, true, 10, 0, 17);
        raid.Update();
        Assert.Equal(0, starts);
        var epoch = raid.CancellationEpoch;
        raid.ObserveNativeCountdownTick(true, 0.02f, 0, frame, frame, epoch, epoch, 17, 17);
        raid.Update();
        Assert.Equal(1, starts);
    }

    [Fact]
    public void ReentrantDisableEnableOrDutyResetCannotConsumeAnOldNativeUpdateObservation()
    {
        var config = new CharacterConfig { Enabled = true };
        var frame = ReadyRaidFrame;
        var starts = 0;
        using var raid = new RaidModeService(() => config, () => { }, () => frame,
            () => { starts++; return true; }, () => false);
        raid.Enable();
        var oldEpoch = raid.CancellationEpoch;
        raid.Disable();
        raid.Enable();
        raid.ObserveNativeCountdownTick(true, 0.02f, 0, frame, frame, oldEpoch, oldEpoch);
        raid.Update();
        Assert.Equal(0, starts);
        var beforeDutyReset = raid.CancellationEpoch;
        raid.OnDutyStarted();
        raid.ObserveNativeCountdownTick(true, 0.02f, 0, frame, frame, beforeDutyReset, raid.CancellationEpoch);
        raid.Update();
        Assert.Equal(0, starts);
        raid.ObserveNativeCountdownTick(true, 0.02f, 0, frame, frame);
        raid.Update();
        Assert.Equal(1, starts);
    }

    [Fact]
    public void FailedNativeSourceAndIdentityChangeCannotGrantCachedRaidPermissions()
    {
        var config = new CharacterConfig { Enabled = true, RaidAdsLootEnabled = true };
        var frame = ReadyRaidFrame;
        var fail = false;
        using var raid = new RaidModeService(() => config, () => { },
            () => fail ? throw new InvalidOperationException("native unavailable") : frame, () => true, () => false);
        raid.Enable();
        raid.ObserveNativeCountdownTick(true, 0.02f, 0, frame, frame);
        raid.Update();
        using (var known = JsonDocument.Parse(raid.GetRuntimeStateJson()))
        {
            Assert.True(known.RootElement.GetProperty("sourceReadable").GetBoolean());
            Assert.True(known.RootElement.GetProperty("adsLootEnabled").GetBoolean());
            Assert.False(known.RootElement.GetProperty("awaitingCountdown").GetBoolean());
        }
        fail = true;
        using (var failed = JsonDocument.Parse(raid.GetRuntimeStateJson()))
        {
            Assert.True(failed.RootElement.GetProperty("enabled").GetBoolean());
            Assert.False(failed.RootElement.GetProperty("sourceReadable").GetBoolean());
            Assert.False(failed.RootElement.GetProperty("adsLootEnabled").GetBoolean());
            Assert.True(failed.RootElement.GetProperty("awaitingCountdown").GetBoolean());
            Assert.False(failed.RootElement.GetProperty("automaticStartAllowedNow").GetBoolean());
            Assert.False(failed.RootElement.GetProperty("automaticExitAllowedNow").GetBoolean());
        }
        fail = false;
        frame = frame with { DutyId = 2 };
        using (var moved = JsonDocument.Parse(raid.GetRuntimeStateJson()))
            Assert.False(moved.RootElement.GetProperty("sourceReadable").GetBoolean());
        raid.Update();
        Assert.True(raid.IsEnabled);
        using var nextDuty = JsonDocument.Parse(raid.GetRuntimeStateJson());
        Assert.True(nextDuty.RootElement.GetProperty("awaitingCountdown").GetBoolean());
    }

    [Fact]
    public void UnknownCharacterDuringTransitionRetainsRaidHoldUntilIdentityIsConfirmed()
    {
        var config = new CharacterConfig { Enabled = true, RaidAdsLootEnabled = true };
        var frame = ReadyRaidFrame;
        using var raid = new RaidModeService(() => config, () => { }, () => frame, () => true, () => false);
        raid.Enable();
        raid.ObserveNativeCountdownTick(true, 0.02f, 0, frame, frame);
        raid.Update();
        frame = frame with { CharacterId = 0, Ready = false, Transition = true };
        raid.Update();
        Assert.True(raid.IsEnabled);
        using (var unknown = JsonDocument.Parse(raid.GetRuntimeStateJson()))
        {
            Assert.True(unknown.RootElement.GetProperty("enabled").GetBoolean());
            Assert.False(unknown.RootElement.GetProperty("sourceReadable").GetBoolean());
            Assert.True(unknown.RootElement.GetProperty("awaitingCountdown").GetBoolean());
            Assert.False(unknown.RootElement.GetProperty("adsLootEnabled").GetBoolean());
            Assert.False(unknown.RootElement.GetProperty("automaticStartAllowedNow").GetBoolean());
            Assert.False(unknown.RootElement.GetProperty("automaticExitAllowedNow").GetBoolean());
        }
        frame = ReadyRaidFrame;
        raid.Update();
        Assert.True(raid.IsEnabled);
        frame = frame with { CharacterId = 702 };
        raid.Update();
        Assert.False(raid.IsEnabled);
    }

    [Fact]
    public void DepartureKeepsRaidModeButClearsOldCountdownLootAndExitWorkForTheNextDuty()
    {
        var config = new CharacterConfig { Enabled = true, RaidAdsLootEnabled = true, RaidRemainUntilManualExit = false };
        var frame = ReadyRaidFrame;
        var starts = 0;
        var leaves = 0;
        var now = 0L;
        using var raid = new RaidModeService(() => config, () => { }, () => frame,
            () => { starts++; return true; }, () => { leaves++; return true; }, () => now);
        raid.Enable();
        using var initial = JsonDocument.Parse(raid.GetRuntimeStateJson());
        var firstSession = initial.RootElement.GetProperty("sessionId").GetString();
        raid.ObserveNativeCountdownTick(true, 0.02f, 0, frame, frame);
        frame = frame with { InDuty = false, TerritoryId = 200, DutyId = 0 };
        raid.Update();
        now = 100_000;
        raid.Update();
        Assert.True(raid.IsEnabled);
        Assert.Equal(0, starts);
        Assert.Equal(0, leaves);
        using (var outside = JsonDocument.Parse(raid.GetRuntimeStateJson()))
        {
            Assert.NotEqual(firstSession, outside.RootElement.GetProperty("sessionId").GetString());
            Assert.False(outside.RootElement.GetProperty("adsLootEnabled").GetBoolean());
            Assert.False(outside.RootElement.GetProperty("automaticExitAllowedNow").GetBoolean());
        }
        frame = ReadyRaidFrame with { DutyId = 2 };
        raid.OnDutyStarted();
        raid.Update();
        Assert.Equal(0, starts);
        raid.ObserveNativeCountdownTick(true, 0.02f, 0, frame, frame);
        raid.Update();
        Assert.Equal(1, starts);
    }

    [Fact]
    public void RolledLootRemainsOutstandingUntilNativeAwardRatherThanUntilAnotherRollIsEligible()
    {
        var item = new FFXIVClientStructs.FFXIV.Client.Game.UI.LootItem
        {
            ChestObjectId = 123, ItemId = 456,
            RollState = FFXIVClientStructs.FFXIV.Client.Game.UI.RollState.Rolled,
            RollResult = FFXIVClientStructs.FFXIV.Client.Game.UI.RollResult.UnAwarded,
        };
        Assert.True(RaidModeService.IsOutstandingLoot(item));
        item.RollResult = FFXIVClientStructs.FFXIV.Client.Game.UI.RollResult.Needed;
        Assert.False(RaidModeService.IsOutstandingLoot(item));
    }

    [Fact]
    public void RecoveryGenerationChangesOncePerWipeEpisodeIncludingBeforeFrameworkObservation()
    {
        var config = new CharacterConfig { Enabled = true };
        var frame = ReadyRaidFrame;
        using var raid = new RaidModeService(() => config, () => { }, () => frame, () => true, () => false);
        raid.Enable();
        frame = frame with { Dead = true, PartyWiped = true };
        int Generation()
        {
            using var json = JsonDocument.Parse(raid.GetRuntimeStateJson());
            return json.RootElement.GetProperty("recoveryGeneration").GetInt32();
        }
        Assert.Equal(1, Generation());
        raid.Update();
        raid.Update();
        Assert.Equal(1, Generation());
        frame = frame with { PartyWiped = false, Transition = true };
        raid.Update();
        Assert.Equal(1, Generation());
        frame = ReadyRaidFrame;
        raid.ObserveNativeCountdownTick(true, 0.1f, 0, frame, frame);
        raid.Update();
        frame = frame with { Dead = true, PartyWiped = true };
        Assert.Equal(2, Generation());
    }

    [Fact]
    public void RaidStartsOnlyFromAnObservedNativeTickAndFreshCountdownAfterRelease()
    {
        var config = new CharacterConfig { Enabled = true, RotationType = 2 };
        var frame = ReadyRaidFrame;
        var starts = 0;
        var now = 0L;
        RaidModeService? raid = null;
        using var service = raid = new RaidModeService(() => config, () => { }, () => frame,
            () =>
            {
                using var state = JsonDocument.Parse(raid!.GetRuntimeStateJson());
                Assert.True(state.RootElement.GetProperty("automaticStartAllowedNow").GetBoolean());
                starts++;
                return true;
            }, () => throw new InvalidOperationException("Remain must prevent leave"), () => now);
        service.Enable();
        service.ObserveNativeCountdownTick(false, 3, 0, frame, frame); // Inactive/cancelled, no native tick crossing.
        service.Update();
        now = 600_000;
        service.Update();
        Assert.Equal(0, starts);
        service.ObserveNativeCountdownTick(true, 0.01f, -0.001f, frame, frame);
        service.Update();
        service.Update();
        Assert.Equal(1, starts);
        Assert.Equal(2, config.RotationType); // Explicit rotation None was not rewritten.
        using (var state = JsonDocument.Parse(service.GetRuntimeStateJson()))
            Assert.False(state.RootElement.GetProperty("automaticStartAllowedNow").GetBoolean());
        frame = frame with { AdsOwned = true };
        service.Update();
        frame = frame with { AdsOwned = false };
        service.Update();
        now += 600_000;
        service.Update();
        Assert.Equal(1, starts);
        service.ObserveNativeCountdownTick(true, 0.02f, 0, frame, frame);
        service.Update();
        Assert.Equal(2, starts);
    }

    [Theory]
    [InlineData("dead")]
    [InlineData("transition")]
    [InlineData("complete")]
    [InlineData("other duty")]
    [InlineData("other character")]
    [InlineData("Run off")]
    public void RaidDropsUnsafeOrStaleNativeCountdownCompletion(string change)
    {
        var config = new CharacterConfig { Enabled = true };
        var frame = ReadyRaidFrame;
        var starts = 0;
        using var raid = new RaidModeService(() => config, () => { }, () => frame,
            () => { starts++; return true; }, () => false);
        raid.Enable();
        var before = frame;
        frame = change switch
        {
            "dead" => frame with { Dead = true },
            "transition" => frame with { Transition = true },
            "complete" => frame with { DutyCompleted = true },
            "other duty" => frame with { DutyId = 2 },
            "other character" => frame with { CharacterId = 702 },
            _ => frame,
        };
        if (change == "Run off") config.Enabled = false;
        raid.ObserveNativeCountdownTick(true, 0.1f, 0, before, frame);
        raid.Update();
        Assert.Equal(0, starts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RaidFirstLootWaitIsAMaximumAndNeverBoundsOutstandingDistribution(bool lootAppears)
    {
        var config = new CharacterConfig { Enabled = true, RaidRemainUntilManualExit = false };
        var frame = ReadyRaidFrame;
        var now = 0L;
        var leaves = 0;
        RaidModeService? raid = null;
        using var service = raid = new RaidModeService(() => config, () => { }, () => frame, () => false,
            () =>
            {
                using var state = JsonDocument.Parse(raid!.GetRuntimeStateJson());
                Assert.True(state.RootElement.GetProperty("automaticExitAllowedNow").GetBoolean());
                leaves++;
                return true;
            }, () => now);
        service.Enable();
        frame = frame with { DutyCompleted = true };
        service.Update();
        now = 5_000;
        frame = frame with { LootOutstanding = lootAppears };
        service.Update();
        now = 9_999;
        service.Update();
        Assert.Equal(0, leaves);
        now = 10_000;
        service.Update();
        Assert.Equal(lootAppears ? 0 : 1, leaves);
        now = 60_000;
        service.Update();
        Assert.Equal(lootAppears ? 0 : 1, leaves);
        frame = frame with { LootOutstanding = false };
        service.Update();
        service.Update();
        Assert.Equal(1, leaves);
        service.RemainUntilManualExit = true;
        using var held = JsonDocument.Parse(service.GetRuntimeStateJson());
        Assert.False(held.RootElement.GetProperty("automaticExitAllowedNow").GetBoolean());
    }

    [Fact]
    public void RaidDeathAndUnreadableLootNeverAuthorizeExitAndDisableDropsPendingWork()
    {
        var config = new CharacterConfig { Enabled = true, RaidRemainUntilManualExit = false, RaidLootAppearanceWaitSeconds = 0 };
        var frame = ReadyRaidFrame;
        var leaves = 0;
        var starts = 0;
        using var raid = new RaidModeService(() => config, () => { }, () => frame,
            () => { starts++; return true; }, () => { leaves++; return true; });
        raid.Enable();
        raid.ObserveNativeCountdownTick(true, 1, 0, frame, frame);
        raid.Disable();
        raid.Update();
        Assert.Equal(0, starts);
        raid.Enable();
        frame = frame with { DutyCompleted = true, Dead = true };
        raid.Update();
        frame = frame with { Dead = false, LootReadable = false };
        raid.Update();
        Assert.Equal(0, leaves);
        frame = frame with { LoggedIn = false, CharacterId = 0 };
        raid.Update();
        Assert.False(raid.IsEnabled);
    }

    [Theory]
    [InlineData(false, "ADS leave rejected")]
    [InlineData(true, "ADS unavailable")]
    public void AFailedOneShotRaidLeaveRetainsItsTruthfulStatusAcrossReadHolds(bool unavailable, string status)
    {
        var config = new CharacterConfig { Enabled = true, RaidRemainUntilManualExit = false, RaidLootAppearanceWaitSeconds = 0 };
        var frame = ReadyRaidFrame;
        var attempts = 0;
        var failRead = false;
        using var raid = new RaidModeService(() => config, () => { },
            () => failRead ? throw new InvalidOperationException("temporary native read hold") : frame, () => false,
            () =>
            {
                attempts++;
                if (unavailable) throw new InvalidOperationException("ADS endpoint unavailable");
                return false;
            });
        raid.Enable();
        frame = frame with { DutyCompleted = true };
        raid.Update();
        raid.Update();
        Assert.Equal(1, attempts);
        Assert.Equal(status, raid.StatusText);
        failRead = true;
        raid.Update();
        Assert.Equal(status, raid.StatusText);
        failRead = false;
        frame = frame with { Ready = false, Transition = true };
        raid.Update();
        Assert.Equal(status, raid.StatusText);
        frame = frame with { Ready = true, Transition = false };
        raid.Update();
        Assert.Equal(1, attempts);
        Assert.Equal(status, raid.StatusText);
        raid.RemainUntilManualExit = true;
        raid.RemainUntilManualExit = false; // Existing explicit control permits a new attempt.
        raid.Update();
        Assert.Equal(2, attempts);
        frame = ReadyRaidFrame;
        raid.OnDutyStarted();
        raid.Update();
        Assert.Equal("Waiting for countdown", raid.StatusText);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void RaidWipeOrManualReturnRequiresFreshCountdownButNormalCombatRaiseDoesNot()
    {
        var config = new CharacterConfig { Enabled = true };
        var frame = ReadyRaidFrame;
        var starts = 0;
        using var raid = new RaidModeService(() => config, () => { }, () => frame,
            () => { starts++; return true; }, () => false);
        raid.Enable();
        raid.ObserveNativeCountdownTick(true, 1, 0, frame, frame);
        raid.Update();
        frame = frame with { AdsOwned = true, InCombat = true, Dead = true };
        raid.Update();
        frame = frame with { Dead = false };
        raid.Update();
        using (var state = JsonDocument.Parse(raid.GetRuntimeStateJson()))
            Assert.False(state.RootElement.GetProperty("awaitingCountdown").GetBoolean());
        frame = frame with { Dead = true, PartyWiped = true };
        raid.Update();
        frame = frame with { Dead = false, PartyWiped = false, InCombat = false };
        raid.Update();
        using (var state = JsonDocument.Parse(raid.GetRuntimeStateJson()))
            Assert.True(state.RootElement.GetProperty("awaitingCountdown").GetBoolean());
        raid.ObserveNativeCountdownTick(true, 0.02f, 0, frame, frame);
        raid.Update();
        Assert.Equal(2, starts); // ADS can retain held ownership across a wipe.
    }

    [Fact]
    public void RaidGatePreservesRealOwnershipAndSkipsAllGenericAdmissionDelayRetryAndExitTakeover()
    {
        var now = new DateTime(2026, 10, 31, 1, 0, 0, DateTimeKind.Utc);
        var config = new CharacterConfig { Enabled = true };
        var owned = false;
        var starts = 0;
        using var ipc = new AdsDutyIpcService(() => true, () => owned, () => "{}",
            () => { starts++; return true; }, () => now);
        var ads = new AdsIntegrationService(ipc, () => config, () => throw new InvalidOperationException("generic recovery"),
            _ => throw new InvalidOperationException("generic fallback"), _ => { }, _ => { },
            (_, _) => throw new InvalidOperationException("generic family/maturity"),
            _ => throw new InvalidOperationException("generic exit settings"), () => true);
        var ready = new AdsHandoffReadinessConditions(true, true, true, false, false, false, false);
        ads.ObserveHandoffReadiness(true, (100, 1), ready, now);
        ads.Update(true, (100, 1), ready, now, forceOwnershipRefresh: true);
        Assert.False(ads.IsHandoffPending);
        Assert.False(ads.IsSoloCombatHeld);
        Assert.False(ads.ShouldPauseDutySystems);
        owned = true;
        now = now.AddSeconds(1);
        ads.Update(true, (100, 1), ready, now, forceOwnershipRefresh: true);
        Assert.True(ads.IsControllingDuty);
        Assert.True(ads.ShouldPauseDutySystems);
        owned = false;
        now = now.AddSeconds(1);
        ads.Update(true, (100, 1), ready, now, forceOwnershipRefresh: true);
        ads.OnDutyCompleted(100, now);
        now = now.AddHours(1);
        ads.Update(true, (100, 1), ready, now, forceOwnershipRefresh: true);
        Assert.False(ads.ExitTakeoverActive);
        Assert.Equal(0, starts);
    }

    public static IEnumerable<object[]> CompletionCases()
    {
        // Synthetic identities: both duties use the same FourMan lifecycle.
        foreach (var duty in new[] { ("The Stone Vigil", 100u, 1u), ("Castrum Meridianum", 101u, 2u) })
        foreach (var entryAtCompletion in new[] { false, true })
        foreach (var blocker in new[] { "none", "loading", "loading51", "combat", "utility", "disabled", "no auto-exit" })
            yield return [duty.Item1, duty.Item2, duty.Item3, entryAtCompletion, blocker];
    }

    [Theory]
    [MemberData(nameof(CompletionCases))]
    public void CompletedDutyLeavesOnceWithoutRestartingUntilARealSessionBoundary(
        string dutyName, uint territory, uint cfc, bool entryAtCompletion, string blocker)
    {
        var epoch = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        var now = epoch;
        var owned = true;
        var starts = 0;
        var commands = new List<string>();
        var config = new CharacterConfig
        {
            Enabled = true,
            AdsDutyFamilySettingsMigrated = true,
            AdsFourManEnabled = true,
            AdsFourManMaturityThreshold = 3,
            AdsFourManHandoffDelaySeconds = 2,
            UseAdsLeaveAfterAdsDuty = blocker != "no auto-exit",
            ExitAfterDutyEnds = false,
            LeaveWhenAllLeft = false,
            ExitAfterDutySeconds = 5,
        };
        var ready = new AdsHandoffReadinessConditions(true, true, true, false, false, false, false);
        var identity = (TerritoryTypeId: territory, ContentFinderConditionId: cfc);
        using var ipc = new AdsDutyIpcService(() => true, () => owned,
            () => JsonSerializer.Serialize(new
            {
                inInstancedDuty = true,
                ownershipMode = owned ? "OwnedStartInside" : "Observing",
                hasCatalogMetadata = true,
                duty = dutyName,
                territoryTypeId = territory,
                contentFinderConditionId = cfc,
                dutyCategory = "FourMan",
                supportLevel = "ActiveSupported",
                clearanceStatus = "FourPlayerSyncCleared",
            }),
            () => { starts++; return true; }, () => now);
        var ads = new AdsIntegrationService(ipc, () => config, () => { },
            command => { commands.Add(command); return true; }, _ => { }, _ => { });

        void Frame(double seconds, bool inDuty = true,
            (uint TerritoryTypeId, uint ContentFinderConditionId)? liveIdentity = null,
            AdsHandoffReadinessConditions? readiness = null, bool combat = false, bool utility = false,
            bool runExit = true)
        {
            now = epoch.AddSeconds(seconds);
            var conditions = readiness ?? ready;
            var live = liveIdentity ?? identity;
            // Match the framework's readiness observation before the loading return.
            ads.ObserveHandoffReadiness(inDuty, live, conditions, now);
            if (!conditions.IsBetweenAreas && !conditions.IsBetweenAreas51)
                ads.Update(inDuty, live, conditions, now, forceOwnershipRefresh: true);

            if (runExit)
                ExitBehaviourService.TryIssueCompletedExit(ads.DutySession, config, now,
                    new DutyExitConditions(inDuty, conditions.IsBetweenAreas || conditions.IsBetweenAreas51,
                        combat, utility, ads.ShouldPauseExitSystem),
                    useAds => commands.Add(useAds ? "/ads leave" : "/dutyfinder"));
        }

        var enteredAt = entryAtCompletion ? 0 : -120;
        ads.OnDutyStarted(territory, epoch.AddSeconds(enteredAt));
        Frame(enteredAt, runExit: false);
        Assert.True(ads.IsControllingDuty);
        ads.OnDutyCompleted(territory, epoch);
        Assert.Equal(epoch, ads.DutySession.CompletedAtUtc);
        Assert.Equal(blocker != "no auto-exit", ads.ExitTakeoverActive);
        Frame(0);
        Assert.Empty(commands);

        owned = false;
        Frame(1);
        Assert.False(ads.IsControllingDuty);
        Assert.Equal(blocker != "no auto-exit", ads.ShouldPauseDutySystems);
        Assert.False(ads.ShouldPauseExitSystem);

        // Duty flags, CFC, territory identity and every cutscene flag may flicker.
        Frame(2, false, (0, 0), ready with { IsBetweenAreas = true, HasLocalPlayer = false });
        Frame(2.5, false, (territory, 0), ready with { IsWatchingCutscene = true });
        Frame(3, false, (999, 0), ready with { IsOccupiedInCutSceneEvent = true });
        Frame(3.5, false, (0, 0), ready with { IsWatchingCutscene78 = true });
        Frame(3.75, false, (territory, 0)); // Duty flag alone cannot confirm exit.
        Frame(4);
        Frame(4.999);
        Assert.True(ads.DutySession.IsCompleted);
        Assert.True(ads.HadAdsControlThisDuty);
        Assert.False(ads.IsHandoffPending);
        Assert.Equal(0, starts);
        Assert.Empty(commands);

        config.Enabled = blocker != "disabled";
        var blockedReadiness = blocker switch
        {
            "loading" => ready with { IsBetweenAreas = true },
            "loading51" => ready with { IsBetweenAreas51 = true },
            _ => ready,
        };
        Frame(5, readiness: blockedReadiness, combat: blocker == "combat", utility: blocker == "utility");
        Frame(8, readiness: blockedReadiness, combat: blocker == "combat", utility: blocker == "utility");
        Assert.Equal(blocker == "none" ? 1 : 0, commands.Count);
        Assert.Equal(0, starts);

        // Loading/combat/utility clear without restarting the completion timer.
        Frame(9);
        Frame(30);
        Frame(60);
        Assert.Equal(blocker is "disabled" or "no auto-exit" ? 0 : 1, commands.Count);
        Assert.True(ads.DutySession.IsCompleted);
        Assert.Equal(0, starts);

        config.Enabled = true;
        config.UseAdsLeaveAfterAdsDuty = true;
        ads.ResetHandoff(); // Enable-time reset must leave completion intact.
        Frame(61);
        Assert.Equal(new[] { "/ads leave" }, commands);
        Assert.True(ads.ExitTakeoverActive);
        Assert.True(ads.ShouldPauseDutySystems);
        Assert.False(ads.ShouldPauseExitSystem);

        ads.OnDutyCompleted(territory, epoch.AddSeconds(62)); // Duplicate notification is idempotent.
        Frame(63);
        Frame(80, false, (0, 0), ready with { IsBetweenAreas51 = true });
        Frame(90);
        Assert.Equal(epoch, ads.DutySession.CompletedAtUtc);
        Assert.True(ads.DutySession.LeaveIssued);
        Assert.Single(commands);
        Assert.Equal(0, starts);

        // A stable actual exit resets the session, even without a later start event.
        owned = true; // ADS can still report its Leaving ownership at the exit seam.
        Frame(100, false, (999, 0));
        Assert.False(ads.DutySession.IsCompleted);
        Assert.False(ads.DutySession.LeaveIssued);
        Assert.False(ads.HadAdsControlThisDuty);
        Assert.False(ads.ExitTakeoverActive);
        owned = false;
        Frame(101);
        Assert.True(ads.IsHandoffPending);
        Frame(102.999);
        Assert.Equal(0, starts);
        Frame(103);
        Assert.Equal(1, starts);

        owned = true;
        Frame(104);
        ads.OnDutyCompleted(territory, epoch.AddSeconds(105));
        owned = false;
        Frame(110);
        Assert.Equal(new[] { "/ads leave", "/ads leave" }, commands);

        // DutyStarted also clears completion if no outside frame was observed.
        ads.OnDutyStarted(territory, epoch.AddSeconds(120));
        Frame(120);
        Assert.False(ads.DutySession.IsCompleted);
        Assert.False(ads.ExitTakeoverActive);
        Frame(122);
        Assert.Equal(2, starts);

        owned = true;
        Frame(123);
        ads.OnDutyCompleted(territory, epoch.AddSeconds(124));
        Frame(125, false, (0, 0), ready with { IsLoggedIn = false, IsBetweenAreas = true });
        Assert.False(ads.DutySession.IsCompleted);
        Assert.False(ads.ExitTakeoverActive);
        Assert.False(ads.HadAdsControlThisDuty);
        owned = false;
        Frame(130);
        Frame(132);
        Assert.Equal(3, starts);
        Assert.Equal(2, commands.Count);
    }
}
