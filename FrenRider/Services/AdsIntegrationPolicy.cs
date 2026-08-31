using System;

namespace FrenRider.Services;

internal readonly record struct AdsHandoffReadinessConditions(
    bool IsLoggedIn,
    bool HasLocalPlayer,
    bool IsPlayerAlive,
    bool IsUnconscious,
    bool IsBetweenAreas,
    bool IsWatchingCutscene,
    bool IsOccupiedInCutSceneEvent);

internal readonly record struct AdsHandoffCountdownState(
    uint TerritoryTypeId,
    uint ContentFinderConditionId,
    DateTime ReadySinceUtc);

internal readonly record struct AdsHandoffCountdownResult(
    AdsHandoffCountdownState State,
    bool IsReady,
    TimeSpan Remaining,
    string? Blocker);

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

    internal static AdsHandoffCountdownResult EvaluateHandoffCountdown(
        AdsHandoffCountdownState previous,
        uint territoryTypeId,
        uint contentFinderConditionId,
        DateTime nowUtc,
        int delaySeconds,
        AdsHandoffReadinessConditions conditions)
    {
        var blocker = GetHandoffReadinessBlocker(conditions);
        if (blocker is not null)
        {
            return new AdsHandoffCountdownResult(
                new AdsHandoffCountdownState(territoryTypeId, contentFinderConditionId, DateTime.MinValue),
                false,
                TimeSpan.FromSeconds(Math.Clamp(delaySeconds, 2, 300)),
                blocker);
        }

        var identityChanged = previous.TerritoryTypeId != territoryTypeId
                              || previous.ContentFinderConditionId != contentFinderConditionId;
        var readySinceUtc = identityChanged
                            || previous.ReadySinceUtc == DateTime.MinValue
                            || nowUtc < previous.ReadySinceUtc
            ? nowUtc
            : previous.ReadySinceUtc;
        var delay = TimeSpan.FromSeconds(Math.Clamp(delaySeconds, 2, 300));
        var remaining = delay - (nowUtc - readySinceUtc);
        var isReady = remaining <= TimeSpan.Zero;

        return new AdsHandoffCountdownResult(
            new AdsHandoffCountdownState(territoryTypeId, contentFinderConditionId, readySinceUtc),
            isReady,
            isReady ? TimeSpan.Zero : remaining,
            null);
    }

    internal static string? GetHandoffReadinessBlocker(AdsHandoffReadinessConditions conditions)
    {
        if (!conditions.IsLoggedIn)
            return "waiting for login";
        if (!conditions.HasLocalPlayer)
            return "waiting for local player";
        if (conditions.IsUnconscious)
            return "waiting for unconscious state to clear";
        if (!conditions.IsPlayerAlive)
            return "waiting for local player to be alive";
        if (conditions.IsBetweenAreas)
            return "waiting for area transition to finish";
        if (conditions.IsWatchingCutscene)
            return "waiting for cutscene to finish";
        if (conditions.IsOccupiedInCutSceneEvent)
            return "waiting for cutscene event to finish";

        return null;
    }
}
