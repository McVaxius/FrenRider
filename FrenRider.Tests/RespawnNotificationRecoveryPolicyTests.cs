using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class RespawnNotificationRecoveryPolicyTests
{
    [Fact]
    public void ReviveAndTelepoStartsToggleThenSchedulesYesAfterSwapDelay()
    {
        var policy = new RespawnNotificationRecoveryPolicy();
        const long now = 1000;

        Assert.Equal(
            RespawnNotificationRecoveryAction.ToggleNotification,
            policy.GetNextAction(now, reviveNotificationVisible: true, teleportNotificationVisible: true));

        policy.RecordToggle(now);

        Assert.Equal(
            RespawnNotificationRecoveryAction.None,
            policy.GetNextAction(now + RespawnNotificationRecoveryPolicy.NotificationSwapDelayMs - 1, true, true));
        Assert.Equal(
            RespawnNotificationRecoveryAction.TryYes,
            policy.GetNextAction(now + RespawnNotificationRecoveryPolicy.NotificationSwapDelayMs, true, true));
    }

    [Fact]
    public void FailedYesSchedulesNextToggleAfterRetryDelay()
    {
        var policy = new RespawnNotificationRecoveryPolicy();
        const long toggleAt = 1000;
        const long yesAt = toggleAt + RespawnNotificationRecoveryPolicy.NotificationSwapDelayMs;

        policy.RecordToggle(toggleAt);
        policy.RecordYesAttempt(accepted: false, yesAt);

        Assert.Equal(
            RespawnNotificationRecoveryAction.None,
            policy.GetNextAction(yesAt + RespawnNotificationRecoveryPolicy.RetryDelayMs - 1, true, true));
        Assert.Equal(
            RespawnNotificationRecoveryAction.ToggleNotification,
            policy.GetNextAction(yesAt + RespawnNotificationRecoveryPolicy.RetryDelayMs, true, true));
    }

    [Fact]
    public void SixFailedCyclesSchedulesBackoffBeforeRetry()
    {
        var policy = new RespawnNotificationRecoveryPolicy();
        var now = 1000L;

        for (var i = 0; i < RespawnNotificationRecoveryPolicy.MaxFailedCyclesPerBurst; i++)
        {
            Assert.Equal(
                RespawnNotificationRecoveryAction.ToggleNotification,
                policy.GetNextAction(now, true, true));

            policy.RecordToggle(now);
            now += RespawnNotificationRecoveryPolicy.NotificationSwapDelayMs;

            Assert.Equal(
                RespawnNotificationRecoveryAction.TryYes,
                policy.GetNextAction(now, true, true));

            policy.RecordYesAttempt(accepted: false, now);

            if (i < RespawnNotificationRecoveryPolicy.MaxFailedCyclesPerBurst - 1)
                now += RespawnNotificationRecoveryPolicy.RetryDelayMs;
        }

        Assert.Equal(
            RespawnNotificationRecoveryAction.None,
            policy.GetNextAction(now + RespawnNotificationRecoveryPolicy.BurstBackoffMs - 1, true, true));
        Assert.Equal(
            RespawnNotificationRecoveryAction.ToggleNotification,
            policy.GetNextAction(now + RespawnNotificationRecoveryPolicy.BurstBackoffMs, true, true));
    }

    [Fact]
    public void ReviveWithoutTelepoTriesYesImmediately()
    {
        var policy = new RespawnNotificationRecoveryPolicy();

        Assert.Equal(
            RespawnNotificationRecoveryAction.TryYes,
            policy.GetNextAction(1000, reviveNotificationVisible: true, teleportNotificationVisible: false));
    }

    [Fact]
    public void NoReviveFallsBackToReturnPrompt()
    {
        var policy = new RespawnNotificationRecoveryPolicy();

        Assert.Equal(
            RespawnNotificationRecoveryAction.OpenReturnPrompt,
            policy.GetNextAction(1000, reviveNotificationVisible: false, teleportNotificationVisible: true));
    }
}
