using System;

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

public enum RespawnPromptAttemptOutcome
{
    None,
    Waiting,
    Confirmed,
    TimedOut,
}

public readonly record struct RespawnPromptAttempt(
    SelectYesnoPromptKind PromptKind,
    string PromptText,
    bool ResponseYes,
    long StartedAtMs);

public readonly record struct RespawnPromptAttemptObservation(
    RespawnPromptAttemptOutcome Outcome,
    RespawnPromptAttempt Attempt);

public sealed class RespawnNotificationRecoveryPolicy
{
    public const long NotificationSwapDelayMs = 250;
    public const long RetryDelayMs = 1000;
    public const long BurstBackoffMs = 2000;
    public const int MaxFailedCyclesPerBurst = 6;

    private long nextActionAtMs;
    private int failedCyclesInBurst;
    private RespawnPromptAttempt? pendingPromptAttempt;

    public long NextActionAtMs => nextActionAtMs;
    public int FailedCyclesInBurst => failedCyclesInBurst;
    public bool HasPendingPromptAttempt => pendingPromptAttempt.HasValue;

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
        if (pendingPromptAttempt.HasValue)
            return RespawnNotificationRecoveryAction.None;

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

    public void RecordPromptAttempt(
        bool callbackDispatched,
        SelectYesnoPromptKind promptKind,
        string promptText,
        bool responseYes,
        long nowMs)
    {
        if (callbackDispatched)
        {
            pendingPromptAttempt = new RespawnPromptAttempt(
                promptKind,
                promptText,
                responseYes,
                nowMs);
            nextActionAtMs = nowMs + RetryDelayMs;
            return;
        }

        RecordFailedCycle(nowMs, retryImmediately: false);
    }

    public RespawnPromptAttemptObservation ObservePrompt(
        long nowMs,
        bool dialogVisible,
        SelectYesnoPromptKind? visiblePromptKind,
        string promptText)
    {
        if (pendingPromptAttempt is not { } attempt)
            return default;

        var readablePromptChanged = visiblePromptKind.HasValue
            && (visiblePromptKind.Value != attempt.PromptKind
                || !string.Equals(promptText, attempt.PromptText, StringComparison.Ordinal));

        if (!dialogVisible || readablePromptChanged)
        {
            pendingPromptAttempt = null;
            failedCyclesInBurst = 0;
            nextActionAtMs = nowMs;
            return new RespawnPromptAttemptObservation(RespawnPromptAttemptOutcome.Confirmed, attempt);
        }

        if (nowMs - attempt.StartedAtMs < RetryDelayMs)
            return new RespawnPromptAttemptObservation(RespawnPromptAttemptOutcome.Waiting, attempt);

        pendingPromptAttempt = null;
        RecordFailedCycle(nowMs, retryImmediately: true);
        return new RespawnPromptAttemptObservation(RespawnPromptAttemptOutcome.TimedOut, attempt);
    }

    private void RecordFailedCycle(long nowMs, bool retryImmediately)
    {
        failedCyclesInBurst++;
        if (failedCyclesInBurst >= MaxFailedCyclesPerBurst)
        {
            failedCyclesInBurst = 0;
            nextActionAtMs = nowMs + BurstBackoffMs;
            return;
        }

        nextActionAtMs = retryImmediately ? nowMs : nowMs + RetryDelayMs;
    }

    public void Reset()
    {
        nextActionAtMs = 0;
        failedCyclesInBurst = 0;
        pendingPromptAttempt = null;
    }
}
