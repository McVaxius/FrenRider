using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class RespawnNotificationRecoveryPolicyTests
{
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

        policy.RecordPromptClick(accepted: true, now);

        Assert.Equal(
            RespawnNotificationRecoveryAction.None,
            policy.GetNextAction(now + RespawnNotificationRecoveryPolicy.RetryDelayMs - 1, null, true, false));

        now += RespawnNotificationRecoveryPolicy.RetryDelayMs;

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
                respawnEnabled: true,
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
    public void FailedPromptClicksBackOffBeforeRetry()
    {
        var policy = new RespawnNotificationRecoveryPolicy();
        var now = 1000L;

        for (var i = 0; i < RespawnNotificationRecoveryPolicy.MaxFailedCyclesPerBurst; i++)
        {
            Assert.Equal(
                RespawnNotificationRecoveryAction.ClickNo,
                policy.GetNextAction(now, SelectYesnoPromptKind.Teleport, reviveNotificationVisible: true, teleportNotificationVisible: true));

            policy.RecordPromptClick(accepted: false, now);

            if (i < RespawnNotificationRecoveryPolicy.MaxFailedCyclesPerBurst - 1)
                now += RespawnNotificationRecoveryPolicy.RetryDelayMs;
        }

        Assert.Equal(
            RespawnNotificationRecoveryAction.None,
            policy.GetNextAction(now + RespawnNotificationRecoveryPolicy.BurstBackoffMs - 1, SelectYesnoPromptKind.Teleport, true, true));
        Assert.Equal(
            RespawnNotificationRecoveryAction.ClickNo,
            policy.GetNextAction(now + RespawnNotificationRecoveryPolicy.BurstBackoffMs, SelectYesnoPromptKind.Teleport, true, true));
    }
}
