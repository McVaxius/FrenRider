using FrenRider.Services;
using WindowSnapshot = FrenRider.Services.ChocoboExplorationService.WindowSnapshot;

namespace FrenRider.Tests;

public sealed class ChocoboExplorationServiceTests
{
    [Fact]
    public void ProbeOpensOnceObservesAllThreeTabsAndClosesItsWindow()
    {
        var probe = new Probe();
        Assert.True(probe.Service.Start());
        Assert.False(probe.Service.Start());
        Assert.Equal(1, probe.OpenCalls);

        probe.Tick();
        probe.Tick(299);
        Assert.Equal(new[] { 2 }, probe.CapturedTabs);
        Assert.Equal(new[] { 0 }, probe.SelectedTabs);
        probe.Tick(1);
        probe.Tick(300);
        probe.Tick(300);

        Assert.Equal(new[] { 2, 0, 1, 2 }, probe.CapturedTabs);
        Assert.Equal(new[] { 0, 1, 2 }, probe.SelectedTabs);
        Assert.Equal(1, probe.CloseCalls);
        Assert.Null(probe.Window);
        Assert.Contains(probe.Logs, line => line.Contains("completed: all three tab selections observed"));
        probe.Tick(60_000);
        probe.Service.Stop("repeated stop");
        Assert.Equal(1, probe.OpenCalls);
        Assert.Equal(1, probe.CloseCalls);
        Assert.Equal(3, probe.SelectedTabs.Count);
        Assert.Equal(4, probe.CaptureCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ExistingWindowIsKeptOpenAndItsOriginalTabIsRestored(int originalTab)
    {
        var original = Snapshot(originalTab);
        var probe = new Probe(original);
        Assert.True(probe.Service.Start());
        probe.Tick();
        probe.Tick(300);
        probe.Tick(300);
        probe.Tick(300);

        Assert.Equal(new[] { originalTab, 0, 1, 2 }, probe.CapturedTabs);
        Assert.Equal(originalTab == 2 ? new[] { 0, 1, 2 } : new[] { 0, 1, 2, originalTab }, probe.SelectedTabs);
        Assert.Equal(original, probe.Window);
        Assert.Equal(0, probe.OpenCalls);
        Assert.Equal(0, probe.CloseCalls);
        var selections = probe.SelectedTabs.Count;
        probe.Service.Stop("repeated stop");
        probe.Tick(60_000);
        Assert.Equal(selections, probe.SelectedTabs.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WaitingForNativeReadinessNeverRepeatsTheOpenRequest(bool windowPresent)
    {
        var probe = new Probe { AutoOpen = false };
        Assert.True(probe.Service.Start());
        if (windowPresent) probe.Window = Snapshot(2) with { Ready = false };
        probe.Tick();
        probe.Tick(1_000);
        Assert.False(probe.Service.Start());
        Assert.Equal(1, probe.OpenCalls);
        Assert.Empty(probe.SelectedTabs);
        Assert.Equal(0, probe.CaptureCalls);

        probe.Window = Snapshot(2);
        probe.Tick();
        Assert.Equal(1, probe.OpenCalls);
        Assert.Equal(new[] { 0 }, probe.SelectedTabs);
        Assert.Equal(new[] { 2 }, probe.CapturedTabs);
        probe.Service.Stop("test finished");
    }

    [Fact]
    public void MissingWindowTimesOutWithoutReopeningOrClaimingALateWindow()
    {
        var probe = new Probe { AutoOpen = false };
        Assert.True(probe.Service.Start());
        probe.Tick(14_999);
        Assert.DoesNotContain("timeout", probe.Logs);
        probe.Tick(1);
        Assert.Contains("timeout", probe.Logs);
        probe.Window = Snapshot(1);
        probe.Tick(60_000);
        Assert.Equal(Snapshot(1), probe.Window);
        Assert.Equal(1, probe.OpenCalls);
        Assert.Equal(0, probe.CloseCalls);
        Assert.Empty(probe.SelectedTabs);
        Assert.Equal(0, probe.CaptureCalls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StopOrTimeoutCleansOnlyTheWindowAlreadyOwnedByThisProbe(bool existingWindow, bool timeout)
    {
        var probe = new Probe(existingWindow ? Snapshot(2) : null);
        Assert.True(probe.Service.Start());
        probe.Tick();
        if (timeout) probe.Tick(15_000);
        else probe.Service.Stop("explicit stop");

        Assert.Equal(new[] { 2 }, probe.CapturedTabs);
        Assert.Equal(existingWindow ? new[] { 0, 2 } : new[] { 0 }, probe.SelectedTabs);
        Assert.Equal(existingWindow ? 0 : 1, probe.CloseCalls);
        Assert.Equal(existingWindow ? Snapshot(2) : (WindowSnapshot?)null, probe.Window);
        probe.Service.Stop("repeated stop");
        probe.Tick(60_000);
        Assert.Equal(existingWindow ? 0 : 1, probe.CloseCalls);
        Assert.Equal(existingWindow ? 2 : 1, probe.SelectedTabs.Count);
        Assert.Equal(1, probe.CaptureCalls);
    }

    [Fact]
    public void StopBeforeAcceptanceDoesNotCloseAnUnclaimedLateWindow()
    {
        var probe = new Probe { AutoOpen = false };
        Assert.True(probe.Service.Start());
        probe.Service.Stop("explicit stop");
        probe.Window = Snapshot(1);
        probe.Tick(60_000);
        Assert.Equal(Snapshot(1), probe.Window);
        Assert.Equal(1, probe.OpenCalls);
        Assert.Equal(0, probe.CloseCalls);
        Assert.Empty(probe.SelectedTabs);
    }

    [Theory]
    [InlineData("before-start")]
    [InlineData("awaiting-window")]
    [InlineData("inspecting-probe")]
    [InlineData("inspecting-existing")]
    public void DisposalStopsWithoutNativeCleanupAndBlocksQueuedStarts(string phase)
    {
        var probe = new Probe(phase == "inspecting-existing" ? Snapshot(2) : null)
        {
            AutoOpen = phase != "awaiting-window",
        };
        if (phase != "before-start") Assert.True(probe.Service.Start());
        if (phase.StartsWith("inspecting", StringComparison.Ordinal)) probe.Tick();
        var actions = (probe.ReadCalls, probe.OpenCalls, probe.CloseCalls, probe.SelectedTabs.Count, probe.CaptureCalls);
        var window = probe.Window;

        probe.Service.Dispose();
        probe.Service.Dispose();
        Assert.False(probe.Service.Start());
        probe.Tick(60_000);
        probe.Service.Stop("repeated stop");

        Assert.Equal(actions, (probe.ReadCalls, probe.OpenCalls, probe.CloseCalls, probe.SelectedTabs.Count, probe.CaptureCalls));
        Assert.Equal(window, probe.Window);
        Assert.Equal(0, probe.CloseCalls);
    }

    [Theory]
    [InlineData("context")]
    [InlineData("identity")]
    [InlineData("logout")]
    public void DepartureStopsWithoutTouchingTheDepartedCharactersWindow(string departure)
    {
        var probe = new Probe();
        Assert.True(probe.Service.Start());
        probe.Tick();
        if (departure == "context") probe.CanRun = false;
        else probe.Identity = departure == "logout" ? 0UL : 2UL;
        probe.Tick();

        Assert.Contains("context/character departed", probe.Logs);
        Assert.Equal(Snapshot(0), probe.Window);
        Assert.Equal(0, probe.CloseCalls);
        Assert.Equal(new[] { 0 }, probe.SelectedTabs);
        probe.CanRun = true;
        probe.Identity = 1;
        probe.Tick(60_000);
        Assert.Equal(1, probe.OpenCalls);
        Assert.Equal(1, probe.CaptureCalls);
        Assert.Single(probe.SelectedTabs);
        Assert.Equal(0, probe.CloseCalls);
    }

    [Theory]
    [InlineData("tab")]
    [InlineData("selected-tab")]
    [InlineData("address")]
    [InlineData("id")]
    [InlineData("closed")]
    [InlineData("not-ready")]
    public void ExternalWindowChangesArePreservedWithoutCleanupOrReplay(string change)
    {
        var probe = new Probe();
        Assert.True(probe.Service.Start());
        probe.Tick();
        var changed = ChangedWindow(Snapshot(0), change);
        probe.Window = changed;
        probe.Tick(300);
        probe.Tick(60_000);
        probe.Service.Stop("repeated stop");
        Assert.Equal(changed, probe.Window);
        Assert.Equal(new[] { 0 }, probe.SelectedTabs);
        Assert.Equal(new[] { 2 }, probe.CapturedTabs);
        Assert.Equal(1, probe.OpenCalls);
        Assert.Equal(0, probe.CloseCalls);
    }

    [Theory]
    [InlineData("tab")]
    [InlineData("selected-tab")]
    [InlineData("address")]
    [InlineData("id")]
    [InlineData("closed")]
    [InlineData("not-ready")]
    public void ExistingWindowChangesBeforeFirstObservationArePreserved(string change)
    {
        var original = Snapshot(2);
        var probe = new Probe(original);
        Assert.True(probe.Service.Start());
        var changed = ChangedWindow(original, change);
        probe.Window = changed;
        probe.Tick();
        probe.Tick(15_000);
        Assert.Equal(changed, probe.Window);
        Assert.Empty(probe.SelectedTabs);
        Assert.Equal(0, probe.CaptureCalls);
        Assert.Equal(0, probe.OpenCalls);
        Assert.Equal(0, probe.CloseCalls);
    }

    [Fact]
    public void RejectedTabDispatchIsNotRetriedOrTreatedAsAccepted()
    {
        var probe = new Probe { AcceptTabs = false };
        Assert.True(probe.Service.Start());
        probe.Tick();
        probe.Tick(300);
        probe.Tick(60_000);
        Assert.Equal(Snapshot(2), probe.Window);
        Assert.Equal(new[] { 0 }, probe.SelectedTabs);
        Assert.Equal(new[] { 2 }, probe.CapturedTabs);
        Assert.Equal(1, probe.OpenCalls);
        Assert.Equal(0, probe.CloseCalls);
        Assert.Contains(probe.Logs, line => line.Contains("dispatch was rejected"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RadioHighlightWithoutNativeTabAcceptanceStopsWithoutFurtherActions(bool existingWindow)
    {
        var probe = new Probe(existingWindow ? Snapshot(2) : null) { UpdateNativeTab = false };
        Assert.True(probe.Service.Start());
        probe.Tick();
        var rejected = Snapshot(2) with { SelectedTab = 0 };
        Assert.Equal(rejected, probe.Window);

        probe.Tick(300);
        probe.Tick(60_000);
        probe.Service.Stop("repeated stop");

        Assert.Equal(rejected, probe.Window);
        Assert.Equal(existingWindow ? 0 : 1, probe.OpenCalls);
        Assert.Equal(0, probe.CloseCalls);
        Assert.Equal(new[] { 0 }, probe.SelectedTabs);
        Assert.Equal(new[] { 2 }, probe.CapturedTabs);
        Assert.Contains(probe.Logs, line => line.Contains("native-tab=2; selected-radio=0"));
        Assert.DoesNotContain(probe.Logs, line => line.Contains("completed:") || line.Contains("cleanup:"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TabIndexChangeWithoutSelectedRadioAcceptanceStopsWithoutFurtherActions(bool existingWindow, bool explicitStop)
    {
        var probe = new Probe(existingWindow ? Snapshot(2) : null) { UpdateSelectedTab = false };
        Assert.True(probe.Service.Start());
        probe.Tick();
        var rejected = Snapshot(0) with { SelectedTab = 2 };
        Assert.Equal(rejected, probe.Window);
        Assert.Equal(new[] { 2 }, probe.CapturedTabs);
        Assert.Equal(new[] { 0 }, probe.SelectedTabs);

        probe.Tick(299);
        Assert.Equal(1, probe.CaptureCalls);
        if (explicitStop) probe.Service.Stop("explicit stop");
        else probe.Tick(1);
        probe.Tick(60_000);
        probe.Service.Stop("repeated stop");

        Assert.Equal(rejected, probe.Window);
        Assert.Equal(existingWindow ? 0 : 1, probe.OpenCalls);
        Assert.Equal(0, probe.CloseCalls);
        Assert.Equal(new[] { 0 }, probe.SelectedTabs);
        Assert.Equal(new[] { 2 }, probe.CapturedTabs);
        Assert.Equal(1, probe.CaptureCalls);
        Assert.DoesNotContain(probe.Logs, line => line.Contains("completed:") || line.Contains("cleanup:"));
        if (!explicitStop) Assert.Contains(probe.Logs, line => line.Contains("dispatch was rejected"));
    }

    [Theory]
    [InlineData(2, -1)]
    [InlineData(2, 3)]
    public void ExistingInvalidRadioSelectionBlocksBeforeAnyNativeRequest(int tab, int selectedTab)
    {
        var original = Snapshot(tab) with { SelectedTab = selectedTab };
        var probe = new Probe(original);
        Assert.False(probe.Service.Start());
        probe.Tick(60_000);
        probe.Service.Stop("repeated stop");

        Assert.Equal(original, probe.Window);
        Assert.Equal(1, probe.ReadCalls);
        Assert.Equal(0, probe.OpenCalls);
        Assert.Equal(0, probe.CloseCalls);
        Assert.Empty(probe.SelectedTabs);
        Assert.Equal(0, probe.CaptureCalls);
        Assert.Contains(probe.Logs, line => line.StartsWith("blocked:"));
        Assert.DoesNotContain(probe.Logs, line => line.Contains("dispatch:") || line.Contains("completed:") || line.Contains("cleanup:"));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    public void ExistingValidMismatchIsCapturedOnlyAfterOwnDispatchAndRestoresOriginalNativeTab(int tab, int selectedTab)
    {
        var probe = new Probe(Snapshot(tab) with { SelectedTab = selectedTab });
        Assert.True(probe.Service.Start());
        probe.Tick();
        Assert.Empty(probe.CapturedTabs);
        Assert.Equal(new[] { 0 }, probe.SelectedTabs);
        probe.Tick(300);
        probe.Tick(300);
        probe.Tick(300);

        Assert.Equal(new[] { 0, 1, 2 }, probe.CapturedTabs);
        Assert.Equal(Snapshot(tab), probe.Window);
        Assert.Equal(tab == 2 ? new[] { 0, 1, 2 } : new[] { 0, 1, 2, tab }, probe.SelectedTabs);
        Assert.Equal(0, probe.OpenCalls);
        Assert.Equal(0, probe.CloseCalls);
        Assert.Contains(probe.Logs, line => line.Contains("capture deferred"));
        Assert.Contains(probe.Logs, line => line.Contains("completed:"));
        var actions = (probe.SelectedTabs.Count, probe.CaptureCalls);
        probe.Tick(60_000);
        probe.Service.Stop("repeated stop");
        Assert.Equal(actions, (probe.SelectedTabs.Count, probe.CaptureCalls));
    }

    [Fact]
    public void FirstObservationWithDisagreeingSelectedRadioDoesNotClaimTheOpenedWindow()
    {
        var probe = new Probe();
        Assert.True(probe.Service.Start());
        var changed = Snapshot(2) with { SelectedTab = 0 };
        probe.Window = changed;
        probe.Tick();
        probe.Tick(60_000);
        probe.Service.Stop("repeated stop");

        Assert.Equal(changed, probe.Window);
        Assert.Equal(1, probe.OpenCalls);
        Assert.Equal(0, probe.CloseCalls);
        Assert.Empty(probe.SelectedTabs);
        Assert.Equal(0, probe.CaptureCalls);
        Assert.Contains("native tab and selected radio disagree", probe.Logs);
        Assert.DoesNotContain(probe.Logs, line => line.Contains("accepted:") || line.Contains("completed:") || line.Contains("cleanup:"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SelectedRadioOnlyExternalChangeIsPreservedWithoutCaptureOrCleanup(bool existingWindow, bool explicitStop)
    {
        var probe = new Probe(existingWindow ? Snapshot(2) : null);
        Assert.True(probe.Service.Start());
        probe.Tick();
        Assert.Equal(Snapshot(0), probe.Window);
        var changed = Snapshot(0) with { SelectedTab = 1 };
        probe.Window = changed;
        if (explicitStop) probe.Service.Stop("explicit stop");
        else probe.Tick(300);
        probe.Tick(60_000);
        probe.Service.Stop("repeated stop");

        Assert.Equal(changed, probe.Window);
        Assert.Equal(existingWindow ? 0 : 1, probe.OpenCalls);
        Assert.Equal(0, probe.CloseCalls);
        Assert.Equal(new[] { 0 }, probe.SelectedTabs);
        Assert.Equal(new[] { 2 }, probe.CapturedTabs);
        Assert.Equal(1, probe.CaptureCalls);
        Assert.DoesNotContain(probe.Logs, line => line.Contains("completed:") || line.Contains("cleanup:"));
    }

    [Fact]
    public void ReloadSelectionDefaultsToNoPendingOrActiveProbe()
    {
        var probe = new Probe();
        Assert.False(probe.Service.PendingReload);
        Assert.False(probe.Service.IsActive);
        probe.Tick(60_000);
        Assert.Equal(0, probe.OpenCalls);
        Assert.Equal(0, probe.ReadCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReloadInitializationCapturesSelectionAndOwnerOnlyOnce(bool selectedAtLoad)
    {
        var probe = new Probe { Registered = false };
        probe.Service.InitializeReload(selectedAtLoad, ownerAtLoad: 1);
        probe.Service.InitializeReload(!selectedAtLoad, ownerAtLoad: 2);
        Assert.Equal(selectedAtLoad, probe.Service.PendingReload);
        probe.Registered = true;
        probe.Tick();
        Assert.Equal(selectedAtLoad ? 1 : 0, probe.OpenCalls);
        Assert.Equal(selectedAtLoad, probe.Service.IsActive);
        Assert.False(probe.Service.PendingReload);

        probe.Service.InitializeReload(selectedAtLoad: true, ownerAtLoad: 1);
        Assert.False(probe.Service.PendingReload);
        Assert.Equal(selectedAtLoad ? 1 : 0, probe.OpenCalls);
    }

    [Fact]
    public void SelectedReadyReloadConsumesBeforeDispatchAndRunsOnlyOnce()
    {
        var probe = new Probe();
        bool? pendingAtOpen = null;
        probe.Opening = () =>
        {
            pendingAtOpen = probe.Service.PendingReload;
            probe.Service.TickReload(characterRegistered: true);
        };
        probe.Service.InitializeReload(selectedAtLoad: true, ownerAtLoad: 1);
        Assert.True(probe.Service.PendingReload);
        Assert.False(probe.Service.IsActive);

        probe.Tick();
        Assert.Equal(false, pendingAtOpen);
        Assert.False(probe.Service.PendingReload);
        Assert.True(probe.Service.IsActive);
        Assert.Equal(1, probe.OpenCalls);
        probe.Tick(300);
        probe.Tick(300);
        probe.Tick(300);
        Assert.False(probe.Service.IsActive);
        Assert.Equal(new[] { 2, 0, 1, 2 }, probe.CapturedTabs);
        Assert.Equal(1, probe.CloseCalls);

        probe.Service.SelectionChanged(selected: true);
        probe.Tick(60_000);
        Assert.False(probe.Service.PendingReload);
        Assert.Equal(1, probe.OpenCalls);
        Assert.Equal(1, probe.CloseCalls);
        Assert.Equal(4, probe.CaptureCalls);
    }

    [Fact]
    public void PendingReloadWaitsForRegistrationAndNonzeroReadyIdentity()
    {
        var probe = new Probe { Registered = false, Identity = 0 };
        probe.Service.InitializeReload(selectedAtLoad: true, ownerAtLoad: 0);
        probe.Tick(1_000);
        probe.Registered = true;
        probe.Tick(1_000);
        Assert.True(probe.Service.PendingReload);
        Assert.False(probe.Service.IsActive);
        Assert.Equal(0, probe.ReadCalls);
        Assert.Equal(0, probe.OpenCalls);

        probe.Identity = 1;
        probe.Tick();
        Assert.False(probe.Service.PendingReload);
        Assert.True(probe.Service.IsActive);
        Assert.Equal(1, probe.OpenCalls);
        Assert.Equal(new[] { 2 }, probe.CapturedTabs);
    }

    [Fact]
    public void DeselectionBeforeRegistrationCancelsWithoutOffOnOrLoginRearming()
    {
        var probe = new Probe { Registered = false };
        probe.Service.InitializeReload(selectedAtLoad: true, ownerAtLoad: 1);
        probe.Service.SelectionChanged(selected: false);
        Assert.False(probe.Service.PendingReload);
        probe.Service.SelectionChanged(selected: true);
        probe.Identity = 0;
        probe.Tick();
        probe.Identity = 1;
        probe.Registered = true;
        probe.Tick(60_000);

        Assert.False(probe.Service.PendingReload);
        Assert.False(probe.Service.IsActive);
        Assert.Equal(0, probe.OpenCalls);
        Assert.Equal(0, probe.ReadCalls);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ReplacingReloadSelectionAffectsOnlyTheNextLoad(bool selectedAtLoad, bool replacement)
    {
        var current = new Probe { Registered = false };
        current.Service.InitializeReload(selectedAtLoad, ownerAtLoad: 1);
        current.Service.SelectionChanged(replacement);
        current.Registered = true;
        current.Tick();
        Assert.False(current.Service.PendingReload);
        Assert.False(current.Service.IsActive);
        Assert.Equal(0, current.OpenCalls);

        var next = new Probe();
        next.Service.InitializeReload(replacement, ownerAtLoad: 1);
        next.Tick();
        Assert.Equal(replacement ? 1 : 0, next.OpenCalls);
        Assert.Equal(replacement, next.Service.IsActive);
        Assert.False(next.Service.PendingReload);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullStopCancelsCurrentLoadWithoutDisablingNextSelectedLoad(bool accepted)
    {
        var configuration = new Configuration { ChocoboProbeAfterReload = true, UiLanguage = "ja" };
        var probe = new Probe { Registered = accepted };
        probe.Service.InitializeReload(configuration.ChocoboProbeAfterReload, ownerAtLoad: 1);
        if (accepted) probe.Tick();
        probe.Service.Stop("full stop");
        Assert.False(probe.Service.PendingReload);
        Assert.False(probe.Service.IsActive);
        var actions = (probe.OpenCalls, probe.CloseCalls, probe.SelectedTabs.Count, probe.CaptureCalls);
        probe.Service.SelectionChanged(selected: true);
        probe.Registered = true;
        probe.Tick(60_000);
        probe.Service.Stop("repeated stop");
        Assert.Equal(actions, (probe.OpenCalls, probe.CloseCalls, probe.SelectedTabs.Count, probe.CaptureCalls));
        Assert.True(configuration.ChocoboProbeAfterReload);
        Assert.Equal("ja", configuration.UiLanguage);

        var next = new Probe();
        next.Service.InitializeReload(configuration.ChocoboProbeAfterReload, ownerAtLoad: 1);
        next.Tick();
        Assert.Equal(1, next.OpenCalls);
        Assert.True(next.Service.IsActive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeselectionStopsAnAcceptedReloadOnceAndDoesNotRearm(bool existingWindow)
    {
        var probe = new Probe(existingWindow ? Snapshot(2) : null);
        probe.Service.InitializeReload(selectedAtLoad: true, ownerAtLoad: 1);
        probe.Tick();
        Assert.True(probe.Service.IsActive);
        probe.Service.SelectionChanged(selected: false);
        Assert.False(probe.Service.IsActive);
        Assert.False(probe.Service.PendingReload);
        Assert.Equal(existingWindow ? Snapshot(2) : (WindowSnapshot?)null, probe.Window);
        Assert.Equal(existingWindow ? 0 : 1, probe.CloseCalls);
        Assert.Equal(existingWindow ? new[] { 0, 2 } : new[] { 0 }, probe.SelectedTabs);
        var actions = (probe.OpenCalls, probe.CloseCalls, probe.SelectedTabs.Count, probe.CaptureCalls);

        probe.Service.SelectionChanged(selected: false);
        probe.Service.SelectionChanged(selected: true);
        probe.Tick(60_000);
        Assert.Equal(actions, (probe.OpenCalls, probe.CloseCalls, probe.SelectedTabs.Count, probe.CaptureCalls));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullStopWithoutRestorationPreservesPendingOrAcceptedNativeState(bool accepted)
    {
        var probe = new Probe { Registered = accepted };
        probe.Service.InitializeReload(selectedAtLoad: true, ownerAtLoad: 1);
        if (accepted) probe.Tick();
        var window = probe.Window;
        var actions = (probe.ReadCalls, probe.OpenCalls, probe.CloseCalls, probe.SelectedTabs.Count, probe.CaptureCalls);
        probe.Service.Stop("context departure", restore: false);
        probe.Service.Stop("repeated cancellation", restore: false);
        probe.Service.SelectionChanged(selected: true);
        probe.Registered = true;
        probe.Tick(60_000);

        Assert.False(probe.Service.PendingReload);
        Assert.False(probe.Service.IsActive);
        Assert.Equal(window, probe.Window);
        Assert.Equal(actions, (probe.ReadCalls, probe.OpenCalls, probe.CloseCalls, probe.SelectedTabs.Count, probe.CaptureCalls));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnloadCancelsPendingAndAcceptedReloadWithoutNativeCleanup(bool accepted)
    {
        var probe = new Probe { Registered = accepted };
        probe.Service.InitializeReload(selectedAtLoad: true, ownerAtLoad: 1);
        if (accepted) probe.Tick();
        var actions = (probe.ReadCalls, probe.OpenCalls, probe.CloseCalls, probe.SelectedTabs.Count, probe.CaptureCalls);
        var window = probe.Window;
        probe.Service.Dispose();
        probe.Service.Dispose();
        probe.Service.SelectionChanged(selected: true);
        probe.Registered = true;
        probe.Tick(60_000);

        Assert.False(probe.Service.PendingReload);
        Assert.False(probe.Service.IsActive);
        Assert.False(probe.Service.Start());
        Assert.Equal(actions, (probe.ReadCalls, probe.OpenCalls, probe.CloseCalls, probe.SelectedTabs.Count, probe.CaptureCalls));
        Assert.Equal(window, probe.Window);
        Assert.Equal(0, probe.CloseCalls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ReloadIdentityDepartureConsumesTheAttemptWithoutTouchingEitherWindow(bool accepted, bool registeredAtDeparture)
    {
        var probe = new Probe { Registered = accepted };
        probe.Service.InitializeReload(selectedAtLoad: true, ownerAtLoad: 1);
        if (accepted) probe.Tick();
        var window = probe.Window;
        probe.Identity = 2;
        probe.Registered = registeredAtDeparture;
        probe.Tick();
        Assert.False(probe.Service.PendingReload);
        Assert.False(probe.Service.IsActive);
        Assert.Equal(accepted ? 1 : 0, probe.OpenCalls);
        Assert.Equal(0, probe.CloseCalls);
        Assert.Equal(window, probe.Window);

        probe.Identity = 1;
        probe.Registered = true;
        probe.Service.SelectionChanged(selected: true);
        probe.Tick(60_000);
        Assert.Equal(accepted ? 1 : 0, probe.OpenCalls);
        Assert.Equal(accepted ? 1 : 0, probe.CaptureCalls);
    }

    [Fact]
    public void OwnerlessLoadBindsOnlyTheFirstRegisteredReadyCharacter()
    {
        var probe = new Probe { Registered = false };
        probe.Service.InitializeReload(selectedAtLoad: true, ownerAtLoad: 0);
        probe.Tick();
        Assert.True(probe.Service.PendingReload);
        probe.Identity = 2;
        probe.Registered = true;
        probe.Tick();
        Assert.True(probe.Service.IsActive);
        Assert.Equal(1, probe.OpenCalls);

        probe.Identity = 3;
        probe.Tick();
        Assert.False(probe.Service.IsActive);
        Assert.False(probe.Service.PendingReload);
        Assert.Equal(0, probe.CloseCalls);
        Assert.Equal(1, probe.CaptureCalls);
    }

    [Theory]
    [InlineData("context")]
    [InlineData("identity")]
    public void ReloadReadinessExceptionConsumesWithoutRetryOrPrivateExceptionText(string fault)
    {
        var probe = new Probe();
        probe.Service.InitializeReload(selectedAtLoad: true, ownerAtLoad: 1);
        if (fault == "context") probe.CanRunFailure = Failure();
        else probe.IdentityFailure = Failure();
        Assert.Null(Record.Exception(() => probe.Tick()));
        Assert.False(probe.Service.PendingReload);
        Assert.False(probe.Service.IsActive);
        probe.CanRunFailure = null;
        probe.IdentityFailure = null;
        probe.Tick(60_000);

        Assert.Equal(0, probe.OpenCalls);
        Assert.Equal(0, probe.CloseCalls);
        Assert.Empty(probe.SelectedTabs);
        Assert.Equal(0, probe.CaptureCalls);
        Assert.Contains(probe.Logs, line => line.Contains(nameof(InvalidOperationException)));
        Assert.All(probe.Logs, line => Assert.DoesNotContain("Synthetic private payload", line));
    }

    [Fact]
    public void RegisteredUnsafeReloadIsConsumedWithoutWaitingForTheContextToBecomeSafe()
    {
        var probe = new Probe { CanRun = false };
        probe.Service.InitializeReload(selectedAtLoad: true, ownerAtLoad: 1);
        probe.Tick();
        Assert.False(probe.Service.PendingReload);
        Assert.False(probe.Service.IsActive);
        probe.CanRun = true;
        probe.Tick(60_000);
        Assert.Equal(0, probe.OpenCalls);
        Assert.Equal(0, probe.ReadCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableOrThrowingReloadOpenRequestIsConsumedWithoutRetry(bool throws)
    {
        var probe = new Probe { OpenResult = false };
        if (throws) probe.OpenFailure = Failure();
        probe.Service.InitializeReload(selectedAtLoad: true, ownerAtLoad: 1);
        Assert.Null(Record.Exception(() => probe.Tick()));
        Assert.False(probe.Service.PendingReload);
        Assert.False(probe.Service.IsActive);
        Assert.Equal(1, probe.OpenCalls);

        probe.OpenFailure = null;
        probe.OpenResult = true;
        probe.Tick(60_000);
        Assert.Equal(1, probe.OpenCalls);
        Assert.Equal(0, probe.CloseCalls);
        Assert.Empty(probe.SelectedTabs);
        Assert.Equal(0, probe.CaptureCalls);
        Assert.All(probe.Logs, line => Assert.DoesNotContain("Synthetic private payload", line));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualStartConsumesPendingReloadEvenWhenTheManualAttemptIsBlocked(bool canRun)
    {
        var probe = new Probe { Registered = false, CanRun = canRun };
        probe.Service.InitializeReload(selectedAtLoad: true, ownerAtLoad: 1);
        Assert.Equal(canRun, probe.Service.Start());
        Assert.False(probe.Service.PendingReload);
        probe.Registered = true;
        probe.CanRun = true;
        probe.Tick();
        Assert.Equal(canRun ? 1 : 0, probe.OpenCalls);
        Assert.Equal(canRun, probe.Service.IsActive);
    }

    [Theory]
    [InlineData(false, 1UL)]
    [InlineData(true, 0UL)]
    public void UnsafeOrMissingIdentityBlocksBeforeAnyNativeRequest(bool canRun, ulong identity)
    {
        var probe = new Probe { CanRun = canRun, Identity = identity };
        Assert.False(probe.Service.Start());
        probe.Tick(60_000);
        Assert.Equal(0, probe.OpenCalls);
        Assert.Equal(0, probe.CloseCalls);
        Assert.Empty(probe.SelectedTabs);
        Assert.Equal(0, probe.CaptureCalls);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(3, true)]
    [InlineData(2, false)]
    public void ExistingInvalidOrUnreadyWindowBlocksWithoutChangingIt(int tab, bool ready)
    {
        var original = Snapshot(tab) with { Ready = ready };
        var probe = new Probe(original);
        Assert.False(probe.Service.Start());
        Assert.Equal(original, probe.Window);
        Assert.Equal(0, probe.OpenCalls);
        Assert.Equal(0, probe.CloseCalls);
        Assert.Empty(probe.SelectedTabs);
    }

    [Fact]
    public void UnavailableOpenRequestIsConsumedWithoutRetry()
    {
        var probe = new Probe { OpenResult = false };
        Assert.False(probe.Service.Start());
        probe.Tick(60_000);
        Assert.Equal(1, probe.OpenCalls);
        Assert.Equal(0, probe.CloseCalls);
        Assert.Empty(probe.SelectedTabs);
        Assert.Equal(0, probe.CaptureCalls);
    }

    [Fact]
    public void PreflightReadExceptionIsContainedWithoutNativeActions()
    {
        var probe = new Probe { ReadFailure = Failure() };
        var started = false;
        Assert.Null(Record.Exception(() => { started = probe.Service.Start(); }));
        Assert.False(started);
        probe.Tick(60_000);
        Assert.Equal(0, probe.OpenCalls);
        Assert.Equal(0, probe.CloseCalls);
        Assert.Empty(probe.SelectedTabs);
        Assert.Equal(0, probe.CaptureCalls);
        Assert.All(probe.Logs, line => Assert.DoesNotContain("Synthetic private payload", line));
    }

    [Theory]
    [InlineData("open")]
    [InlineData("capture")]
    [InlineData("read")]
    [InlineData("select")]
    [InlineData("close")]
    public void NativeAndCleanupExceptionsDoNotReplayActionsOrLogPrivateMessages(string fault)
    {
        var probe = new Probe();
        Assert.Null(Record.Exception(() =>
        {
            if (fault == "open")
            {
                probe.OpenFailure = Failure();
                Assert.False(probe.Service.Start());
                return;
            }
            Assert.True(probe.Service.Start());
            if (fault == "select") probe.SelectFailure = Failure();
            probe.Tick();
            if (fault == "capture") { probe.CaptureFailure = Failure(); probe.Tick(300); }
            if (fault == "read") { probe.ReadFailure = Failure(); probe.Tick(300); }
            if (fault == "close") { probe.CloseFailure = Failure(); probe.Service.Stop("explicit stop"); }
        }));

        var actions = (probe.OpenCalls, probe.CloseCalls, probe.SelectedTabs.Count, probe.CaptureCalls);
        probe.Tick(60_000);
        probe.Service.Stop("repeated stop");
        Assert.Equal(actions, (probe.OpenCalls, probe.CloseCalls, probe.SelectedTabs.Count, probe.CaptureCalls));
        Assert.Equal(1, probe.OpenCalls);
        Assert.Equal(fault is "open" or "read" ? 0 : 1, probe.CloseCalls);
        Assert.Contains(probe.Logs, line => line.Contains(nameof(InvalidOperationException)));
        Assert.All(probe.Logs, line => Assert.DoesNotContain("Synthetic private payload", line));
    }

    [Fact]
    public void SanitizationRedactsSyntheticNamesIgnoringCaseAndRemovesControlCharacters()
    {
        var names = new[] { "", " ", "Synthetic Player", "Synthetic Companion", "Synthetic", "Player" };
        var text = "SYNTHETIC PLAYER\r\nSynthetic Companion\tSynthetic Player\0Rank: 3";
        var sanitized = ChocoboExplorationService.Sanitize(text, names);
        Assert.Equal("[redacted]  [redacted] [redacted] Rank: 3", sanitized);
        Assert.DoesNotContain('\r', sanitized);
        Assert.DoesNotContain('\n', sanitized);
        Assert.DoesNotContain('\t', sanitized);
        Assert.DoesNotContain('\0', sanitized);
    }

    [Fact]
    public void SanitizationRedactsBeforeBoundingTheDiagnosticText()
    {
        var sanitized = ChocoboExplorationService.Sanitize("Synthetic Companion" + new string('x', 300),
            new[] { "Synthetic Companion" });
        Assert.StartsWith("[redacted]", sanitized);
        Assert.Equal(241, sanitized.Length);
        Assert.EndsWith("\u2026", sanitized);
        Assert.False(sanitized.Contains("Synthetic Companion", StringComparison.OrdinalIgnoreCase));
    }

    private static WindowSnapshot Snapshot(int tab) => new((nint)123, 7, tab, true, tab);

    private static WindowSnapshot? ChangedWindow(WindowSnapshot window, string change) => change switch
    {
        "tab" => window with { Tab = window.Tab == 0 ? 2 : 1, SelectedTab = window.Tab == 0 ? 2 : 1 },
        "selected-tab" => window with { SelectedTab = window.SelectedTab == 0 ? 2 : 1 },
        "address" => window with { Address = (nint)456 },
        "id" => window with { Id = 8 },
        "closed" => null,
        "not-ready" => window with { Ready = false },
        _ => throw new ArgumentOutOfRangeException(nameof(change)),
    };

    private static InvalidOperationException Failure() => new("Synthetic private payload");

    private sealed class Probe
    {
        public bool CanRun = true;
        public bool Registered = true;
        public ulong Identity = 1;
        public WindowSnapshot? Window;
        public bool AutoOpen = true;
        public bool OpenResult = true;
        public bool AcceptTabs = true;
        public bool UpdateNativeTab = true;
        public bool UpdateSelectedTab = true;
        public Exception? ReadFailure;
        public Exception? CanRunFailure;
        public Exception? IdentityFailure;
        public Exception? OpenFailure;
        public Exception? SelectFailure;
        public Exception? CaptureFailure;
        public Exception? CloseFailure;
        public Action? Opening;
        public int ReadCalls;
        public int OpenCalls;
        public int CloseCalls;
        public int CaptureCalls;
        public readonly List<int> SelectedTabs = new();
        public readonly List<int> CapturedTabs = new();
        public readonly List<string> Logs = new();
        public ChocoboExplorationService Service { get; }
        private long now;

        public Probe(WindowSnapshot? window = null)
        {
            Window = window;
            Service = new ChocoboExplorationService(
                () => { if (CanRunFailure != null) throw CanRunFailure; return CanRun; },
                () => { if (IdentityFailure != null) throw IdentityFailure; return Identity; },
                () => { ReadCalls++; if (ReadFailure != null) throw ReadFailure; return Window; },
                () =>
                {
                    OpenCalls++;
                    Opening?.Invoke();
                    if (OpenFailure != null) throw OpenFailure;
                    if (OpenResult && AutoOpen) Window = Snapshot(2);
                    return OpenResult;
                },
                tab =>
                {
                    SelectedTabs.Add(tab);
                    if (AcceptTabs && Window.HasValue) Window = Window.Value with
                    {
                        Tab = UpdateNativeTab ? tab : Window.Value.Tab,
                        SelectedTab = UpdateSelectedTab ? tab : Window.Value.SelectedTab,
                    };
                    if (SelectFailure != null) throw SelectFailure;
                },
                () =>
                {
                    CaptureCalls++;
                    if (CaptureFailure != null) throw CaptureFailure;
                    CapturedTabs.Add(Window!.Value.Tab);
                },
                () =>
                {
                    CloseCalls++;
                    if (CloseFailure != null) throw CloseFailure;
                    Window = null;
                }, Logs.Add, () => now);
        }

        public void Tick(long elapsed = 0)
        {
            now += elapsed;
            Service.TickReload(Registered);
            Service.Tick();
        }
    }
}
