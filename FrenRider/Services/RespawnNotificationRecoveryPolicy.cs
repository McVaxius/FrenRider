namespace FrenRider.Services;

public enum RespawnNotificationRecoveryAction
{
    None,
    ToggleNotification,
    TryYes,
    OpenReturnPrompt,
}

public sealed class RespawnNotificationRecoveryPolicy
{
    public const long NotificationSwapDelayMs = 250;
    public const long RetryDelayMs = 250;
    public const long BurstBackoffMs = 2000;
    public const int MaxFailedCyclesPerBurst = 6;

    private long nextActionAtMs;
    private bool waitingForYes;
    private int failedCyclesInBurst;

    public long NextActionAtMs => nextActionAtMs;
    public bool WaitingForYes => waitingForYes;
    public int FailedCyclesInBurst => failedCyclesInBurst;

    public static bool ShouldOwnFlow(
        bool loggedIn,
        bool frenRiderEnabled,
        bool respawnEnabled,
        bool utilityGateActive,
        bool inDuty,
        bool areaTransitionActive,
        bool unconscious,
        bool reviveNotificationVisible)
        => loggedIn
            && frenRiderEnabled
            && respawnEnabled
            && !utilityGateActive
            && !inDuty
            && !areaTransitionActive
            && unconscious
            && reviveNotificationVisible;

    public RespawnNotificationRecoveryAction GetNextAction(
        long nowMs,
        bool reviveNotificationVisible,
        bool teleportNotificationVisible)
    {
        if (!reviveNotificationVisible)
        {
            Reset();
            return RespawnNotificationRecoveryAction.OpenReturnPrompt;
        }

        if (nowMs < nextActionAtMs)
            return RespawnNotificationRecoveryAction.None;

        if (waitingForYes)
            return RespawnNotificationRecoveryAction.TryYes;

        return teleportNotificationVisible
            ? RespawnNotificationRecoveryAction.ToggleNotification
            : RespawnNotificationRecoveryAction.TryYes;
    }

    public void RecordToggle(long nowMs)
    {
        waitingForYes = true;
        nextActionAtMs = nowMs + NotificationSwapDelayMs;
    }

    public void RecordYesAttempt(bool accepted, long nowMs)
    {
        waitingForYes = false;

        if (accepted)
        {
            failedCyclesInBurst = 0;
            nextActionAtMs = nowMs + RetryDelayMs;
            return;
        }

        failedCyclesInBurst++;
        if (failedCyclesInBurst >= MaxFailedCyclesPerBurst)
        {
            failedCyclesInBurst = 0;
            nextActionAtMs = nowMs + BurstBackoffMs;
            return;
        }

        nextActionAtMs = nowMs + RetryDelayMs;
    }

    public void Reset()
    {
        nextActionAtMs = 0;
        waitingForYes = false;
        failedCyclesInBurst = 0;
    }
}
