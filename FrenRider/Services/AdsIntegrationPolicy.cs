using System;

namespace FrenRider.Services;

public static class AdsIntegrationPolicy
{
    public static readonly TimeSpan HandoffConfirmationTimeout = TimeSpan.FromSeconds(5);

    public static bool ShouldPauseDutySystems(bool handoffPending, bool runtimeOwned, bool exitTakeoverActive)
        => handoffPending || runtimeOwned || exitTakeoverActive;

    public static bool ShouldPauseExitSystem(bool handoffPending, bool runtimeOwned, bool exitTakeoverActive)
        => ShouldPauseDutySystems(handoffPending, runtimeOwned, exitTakeoverActive) && !exitTakeoverActive;

    public static bool IsHandoffConfirmationPending(DateTime requestedAtUtc, DateTime nowUtc)
        => requestedAtUtc != DateTime.MinValue
           && nowUtc - requestedAtUtc < HandoffConfirmationTimeout;

    public static bool CanAttemptHandoff(DateTime requestedAtUtc, DateTime nextAttemptUtc, DateTime nowUtc)
        => !IsHandoffConfirmationPending(requestedAtUtc, nowUtc)
           && nowUtc >= nextAttemptUtc;
}
