using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class RespawnNotificationRecoveryPolicyTests
{
    [Fact]
    public void RetryTimingsKeepNotificationDelayAndUseOneSecondDialogRetry()
    {
        Assert.Equal(250, RespawnNotificationRecoveryPolicy.NotificationSwapDelayMs);
        Assert.Equal(1000, RespawnNotificationRecoveryPolicy.RetryDelayMs);
        Assert.Equal(2000, RespawnNotificationRecoveryPolicy.BurstBackoffMs);
    }

    [Fact]
    public void NotificationHandlingWaitsForConfiguredRespawnDelay()
    {
        const long unconsciousStartedMs = 1000;
        const long delayMs = 15000;

        Assert.False(RespawnService.HasRespawnDelayElapsed(
            unconsciousStartedMs,
            unconsciousStartedMs + delayMs - 1,
            delayMs));
        Assert.True(RespawnService.HasRespawnDelayElapsed(
            unconsciousStartedMs,
            unconsciousStartedMs + delayMs,
            delayMs));
    }

    [Fact]
    public void TeleportNotificationThenReviveNotificationDeclinesTeleportBeforeAcceptingReturn()
    {
        var policy = new RespawnNotificationRecoveryPolicy();
        var now = 1000L;

        Assert.Equal(
            RespawnNotificationRecoveryAction.ExpandTeleportNotification,
            policy.GetNextAction(now, visiblePromptKind: null, reviveNotificationVisible: true, teleportNotificationVisible: true));

        policy.RecordNotificationAction(now);

        Assert.Equal(
            RespawnNotificationRecoveryAction.None,
            policy.GetNextAction(now + RespawnNotificationRecoveryPolicy.NotificationSwapDelayMs - 1, null, true, true));

        now += RespawnNotificationRecoveryPolicy.NotificationSwapDelayMs;

        Assert.Equal(
            RespawnNotificationRecoveryAction.ClickNo,
            policy.GetNextAction(now, SelectYesnoPromptKind.Teleport, reviveNotificationVisible: true, teleportNotificationVisible: true));

        const string teleportPrompt = "Teleport to your party member?";
        policy.RecordPromptAttempt(
            callbackDispatched: true,
            SelectYesnoPromptKind.Teleport,
            teleportPrompt,
            responseYes: false,
            now);

        Assert.Equal(
            RespawnPromptAttemptOutcome.Waiting,
            policy.ObservePrompt(
                now + 100,
                dialogVisible: true,
                SelectYesnoPromptKind.Teleport,
                teleportPrompt).Outcome);

        Assert.Equal(
            RespawnNotificationRecoveryAction.None,
            policy.GetNextAction(now + 100, SelectYesnoPromptKind.Teleport, true, true));

        now += 200;

        Assert.Equal(
            RespawnPromptAttemptOutcome.Confirmed,
            policy.ObservePrompt(
                now,
                dialogVisible: false,
                visiblePromptKind: null,
                promptText: string.Empty).Outcome);

        Assert.Equal(
            RespawnNotificationRecoveryAction.SurfaceRevivePrompt,
            policy.GetNextAction(now, visiblePromptKind: null, reviveNotificationVisible: true, teleportNotificationVisible: false));

        policy.RecordNotificationAction(now);
        now += RespawnNotificationRecoveryPolicy.NotificationSwapDelayMs;

        Assert.Equal(
            RespawnNotificationRecoveryAction.ClickYes,
            policy.GetNextAction(now, SelectYesnoPromptKind.DeathReturn, reviveNotificationVisible: true, teleportNotificationVisible: false));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void VisibleTeleportPromptAlwaysClicksNo(bool reviveNotificationVisible, bool teleportNotificationVisible)
    {
        var policy = new RespawnNotificationRecoveryPolicy();

        Assert.Equal(
            RespawnNotificationRecoveryAction.ClickNo,
            policy.GetNextAction(
                nowMs: 1000,
                visiblePromptKind: SelectYesnoPromptKind.Teleport,
                reviveNotificationVisible,
                teleportNotificationVisible));
    }

    [Fact]
    public void VisibleDeathReturnPromptWithoutNotificationsClicksYes()
    {
        var policy = new RespawnNotificationRecoveryPolicy();

        Assert.True(
            RespawnNotificationRecoveryPolicy.ShouldOwnFlow(
                loggedIn: true,
                frenRiderEnabled: true,
                respawnOutsideDuties: true,
                respawnInsideDuties: false,
                utilityGateActive: false,
                inDuty: false,
                areaTransitionActive: false,
                unconscious: true,
                reviveNotificationVisible: false,
                teleportNotificationVisible: false,
                visiblePromptKind: SelectYesnoPromptKind.DeathReturn));

        Assert.Equal(
            RespawnNotificationRecoveryAction.ClickYes,
            policy.GetNextAction(
                nowMs: 1000,
                visiblePromptKind: SelectYesnoPromptKind.DeathReturn,
                reviveNotificationVisible: false,
                teleportNotificationVisible: false));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void RespawnScopeSelectionControlsOwnershipAndPromptClicks(
        bool respawnOutsideDuties,
        bool respawnInsideDuties)
    {
        var outsideOwns = RespawnNotificationRecoveryPolicy.ShouldOwnFlow(
            loggedIn: true,
            frenRiderEnabled: true,
            respawnOutsideDuties: respawnOutsideDuties,
            respawnInsideDuties: respawnInsideDuties,
            utilityGateActive: false,
            inDuty: false,
            areaTransitionActive: false,
            unconscious: true,
            reviveNotificationVisible: false,
            teleportNotificationVisible: false,
            visiblePromptKind: SelectYesnoPromptKind.DeathReturn);
        var insideOwns = RespawnNotificationRecoveryPolicy.ShouldOwnFlow(
            loggedIn: true,
            frenRiderEnabled: true,
            respawnOutsideDuties: respawnOutsideDuties,
            respawnInsideDuties: respawnInsideDuties,
            utilityGateActive: false,
            inDuty: true,
            areaTransitionActive: false,
            unconscious: true,
            reviveNotificationVisible: false,
            teleportNotificationVisible: false,
            visiblePromptKind: SelectYesnoPromptKind.DeathReturn);

        Assert.Equal(respawnOutsideDuties, outsideOwns);
        Assert.Equal(respawnInsideDuties, insideOwns);

        var policy = new RespawnNotificationRecoveryPolicy();
        Assert.Equal(
            respawnOutsideDuties
                ? RespawnNotificationRecoveryAction.ClickYes
                : RespawnNotificationRecoveryAction.None,
            policy.GetNextAction(
                nowMs: 1000,
                visiblePromptKind: SelectYesnoPromptKind.DeathReturn,
                reviveNotificationVisible: false,
                teleportNotificationVisible: false,
                respawnEnabled: respawnOutsideDuties));
        Assert.Equal(
            respawnInsideDuties
                ? RespawnNotificationRecoveryAction.ClickYes
                : RespawnNotificationRecoveryAction.None,
            policy.GetNextAction(
                nowMs: 1000,
                visiblePromptKind: SelectYesnoPromptKind.DeathReturn,
                reviveNotificationVisible: false,
                teleportNotificationVisible: false,
                respawnEnabled: respawnInsideDuties));
    }

    [Theory]
    [InlineData(SelectYesnoPromptKind.Unknown)]
    [InlineData(SelectYesnoPromptKind.Party)]
    [InlineData(SelectYesnoPromptKind.Misc)]
    public void UnknownPartyAndMiscPromptsProduceNoRespawnClick(SelectYesnoPromptKind promptKind)
    {
        var policy = new RespawnNotificationRecoveryPolicy();

        Assert.Equal(
            RespawnNotificationRecoveryAction.None,
            policy.GetNextAction(
                nowMs: 1000,
                visiblePromptKind: promptKind,
                reviveNotificationVisible: true,
                teleportNotificationVisible: true));
    }

    [Fact]
    public void ReviveWithoutTeleportSurfacesRevivePrompt()
    {
        var policy = new RespawnNotificationRecoveryPolicy();

        Assert.Equal(
            RespawnNotificationRecoveryAction.SurfaceRevivePrompt,
            policy.GetNextAction(1000, visiblePromptKind: null, reviveNotificationVisible: true, teleportNotificationVisible: false));
    }

    [Fact]
    public void NoReviveFallsBackToReturnPrompt()
    {
        var policy = new RespawnNotificationRecoveryPolicy();

        Assert.Equal(
            RespawnNotificationRecoveryAction.OpenReturnPrompt,
            policy.GetNextAction(1000, visiblePromptKind: null, reviveNotificationVisible: false, teleportNotificationVisible: false));
    }

    [Fact]
    public void FailedCallbackRetriesAfterOneSecond()
    {
        var policy = new RespawnNotificationRecoveryPolicy();
        const long now = 1000;

        policy.RecordPromptAttempt(
            callbackDispatched: false,
            SelectYesnoPromptKind.Teleport,
            "Teleport?",
            responseYes: false,
            now);

        Assert.Equal(
            RespawnNotificationRecoveryAction.None,
            policy.GetNextAction(
                now + RespawnNotificationRecoveryPolicy.RetryDelayMs - 1,
                SelectYesnoPromptKind.Teleport,
                reviveNotificationVisible: true,
                teleportNotificationVisible: true));
        Assert.Equal(
            RespawnNotificationRecoveryAction.ClickNo,
            policy.GetNextAction(
                now + RespawnNotificationRecoveryPolicy.RetryDelayMs,
                SelectYesnoPromptKind.Teleport,
                reviveNotificationVisible: true,
                teleportNotificationVisible: true));
    }

    [Fact]
    public void PersistentPromptBacksOffAfterSixOneSecondTimeouts()
    {
        var policy = new RespawnNotificationRecoveryPolicy();
        var now = 1000L;
        const string prompt = "Teleport?";

        for (var i = 0; i < RespawnNotificationRecoveryPolicy.MaxFailedCyclesPerBurst; i++)
        {
            Assert.Equal(
                RespawnNotificationRecoveryAction.ClickNo,
                policy.GetNextAction(now, SelectYesnoPromptKind.Teleport, reviveNotificationVisible: true, teleportNotificationVisible: true));

            policy.RecordPromptAttempt(
                callbackDispatched: true,
                SelectYesnoPromptKind.Teleport,
                prompt,
                responseYes: false,
                now);

            Assert.Equal(
                RespawnPromptAttemptOutcome.Waiting,
                policy.ObservePrompt(
                    now + RespawnNotificationRecoveryPolicy.RetryDelayMs - 1,
                    dialogVisible: true,
                    SelectYesnoPromptKind.Teleport,
                    prompt).Outcome);

            now += RespawnNotificationRecoveryPolicy.RetryDelayMs;

            Assert.Equal(
                RespawnPromptAttemptOutcome.TimedOut,
                policy.ObservePrompt(
                    now,
                    dialogVisible: true,
                    SelectYesnoPromptKind.Teleport,
                    prompt).Outcome);
        }

        Assert.Equal(
            RespawnNotificationRecoveryAction.None,
            policy.GetNextAction(now + RespawnNotificationRecoveryPolicy.BurstBackoffMs - 1, SelectYesnoPromptKind.Teleport, true, true));
        Assert.Equal(
            RespawnNotificationRecoveryAction.ClickNo,
            policy.GetNextAction(now + RespawnNotificationRecoveryPolicy.BurstBackoffMs, SelectYesnoPromptKind.Teleport, true, true));
    }

    [Fact]
    public void ReturnCanBeAcceptedOnlyAfterTeleportDialogChanges()
    {
        var policy = new RespawnNotificationRecoveryPolicy();
        const long now = 1000;
        const string teleportPrompt = "Teleport?";
        const string returnPrompt = "Return to your home point?";

        policy.RecordPromptAttempt(
            callbackDispatched: true,
            SelectYesnoPromptKind.Teleport,
            teleportPrompt,
            responseYes: false,
            now);

        Assert.Equal(
            RespawnNotificationRecoveryAction.None,
            policy.GetNextAction(
                now + 100,
                SelectYesnoPromptKind.Teleport,
                reviveNotificationVisible: true,
                teleportNotificationVisible: false));

        var confirmation = policy.ObservePrompt(
            now + 100,
            dialogVisible: true,
            SelectYesnoPromptKind.DeathReturn,
            returnPrompt);

        Assert.Equal(RespawnPromptAttemptOutcome.Confirmed, confirmation.Outcome);
        Assert.False(confirmation.Attempt.ResponseYes);
        Assert.Equal(
            RespawnNotificationRecoveryAction.ClickYes,
            policy.GetNextAction(
                now + 100,
                SelectYesnoPromptKind.DeathReturn,
                reviveNotificationVisible: true,
                teleportNotificationVisible: false));

        policy.RecordPromptAttempt(
            callbackDispatched: true,
            SelectYesnoPromptKind.DeathReturn,
            returnPrompt,
            responseYes: true,
            now + 100);

        var accepted = policy.ObservePrompt(
            now + 200,
            dialogVisible: false,
            visiblePromptKind: null,
            promptText: string.Empty);

        Assert.Equal(RespawnPromptAttemptOutcome.Confirmed, accepted.Outcome);
        Assert.True(accepted.Attempt.ResponseYes);
    }
}
