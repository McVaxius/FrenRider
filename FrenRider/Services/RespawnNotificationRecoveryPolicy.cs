namespace FrenRider.Services;

public enum RespawnNotificationRecoveryAction
{
    None,
    ExpandTeleportNotification,
    ClickNo,
    SurfaceRevivePrompt,
    ClickYes,
    OpenReturnPrompt,
}

public sealed class RespawnNotificationRecoveryPolicy
{
    public const long NotificationSwapDelayMs = 250;
    public const long RetryDelayMs = 250;
    public const long BurstBackoffMs = 2000;
    public const int MaxFailedCyclesPerBurst = 6;

    private long nextActionAtMs;
    private int failedCyclesInBurst;

    public long NextActionAtMs => nextActionAtMs;
    public int FailedCyclesInBurst => failedCyclesInBurst;

    public static bool ShouldOwnFlow(
        bool loggedIn,
        bool frenRiderEnabled,
        bool respawnEnabled,
        bool utilityGateActive,
        bool inDuty,
        bool areaTransitionActive,
        bool unconscious,
        bool reviveNotificationVisible,
        bool teleportNotificationVisible,
        SelectYesnoPromptKind? visiblePromptKind)
        => loggedIn
            && frenRiderEnabled
            && respawnEnabled
            && !utilityGateActive
            && !inDuty
            && !areaTransitionActive
            && unconscious
            && (reviveNotificationVisible
                || teleportNotificationVisible
                || visiblePromptKind is SelectYesnoPromptKind.Teleport
                    or SelectYesnoPromptKind.DeathReturn
                    or SelectYesnoPromptKind.Raise);

    public RespawnNotificationRecoveryAction GetNextAction(
        long nowMs,
        SelectYesnoPromptKind? visiblePromptKind,
        bool reviveNotificationVisible,
        bool teleportNotificationVisible)
    {
        if (nowMs < nextActionAtMs)
            return RespawnNotificationRecoveryAction.None;

        switch (visiblePromptKind)
        {
            case SelectYesnoPromptKind.Teleport:
                return RespawnNotificationRecoveryAction.ClickNo;
            case SelectYesnoPromptKind.DeathReturn:
            case SelectYesnoPromptKind.Raise:
                return RespawnNotificationRecoveryAction.ClickYes;
            case SelectYesnoPromptKind.Unknown:
            case SelectYesnoPromptKind.Party:
            case SelectYesnoPromptKind.Misc:
                return RespawnNotificationRecoveryAction.None;
        }

        if (teleportNotificationVisible)
            return RespawnNotificationRecoveryAction.ExpandTeleportNotification;

        if (reviveNotificationVisible)
            return RespawnNotificationRecoveryAction.SurfaceRevivePrompt;

        Reset();
        return RespawnNotificationRecoveryAction.OpenReturnPrompt;
    }

    public void RecordNotificationAction(long nowMs)
    {
        nextActionAtMs = nowMs + NotificationSwapDelayMs;
    }

    public void RecordPromptClick(bool accepted, long nowMs)
    {
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
        failedCyclesInBurst = 0;
    }
}
