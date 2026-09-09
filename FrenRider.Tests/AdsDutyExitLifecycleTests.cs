using System.Text.Json;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class AdsDutyExitLifecycleTests
{
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
