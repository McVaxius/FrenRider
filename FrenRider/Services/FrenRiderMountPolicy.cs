using System;

namespace FrenRider.Services;

public enum FrenMountPolicy
{
    PreserveCurrent,
    OnFoot,
    OwnMount,
    Pillion,
}

public enum FrenMountCorrectionAction
{
    None,
    Land,
    Dismount,
}

public static class FrenRiderMountPolicy
{
    public const long FarChaseDelayNotPendingMs = -1;

    public static bool ResolveOwnMountState(
        bool nativeAccessAvailable,
        bool nativeMounted,
        bool ridingPillion,
        bool conditionMounted)
    {
        var hasNativeMountedState = nativeAccessAvailable && nativeMounted;
        return !ridingPillion && (hasNativeMountedState || conditionMounted);
    }

    public static bool ShouldRequestFarChase(
        bool currentlyRequested,
        bool eligible,
        float xzDistance,
        float chaseDistance,
        float clingDistance)
    {
        if (!eligible)
            return false;

        var retirementDistance = currentlyRequested
            ? clingDistance
            : MathF.Max(chaseDistance, clingDistance);
        return xzDistance > retirementDistance;
    }

    public static bool ShouldRequestFarChase(
        bool currentlyRequested,
        bool eligible,
        float xzDistance,
        float chaseDistance,
        float clingDistance,
        int delaySeconds,
        long nowMs,
        ref long delayEligibleSinceMs)
    {
        if (currentlyRequested)
        {
            delayEligibleSinceMs = FarChaseDelayNotPendingMs;
            return ShouldRequestFarChase(
                currentlyRequested,
                eligible,
                xzDistance,
                chaseDistance,
                clingDistance);
        }

        var initialStartEligible = eligible
            && xzDistance > MathF.Max(chaseDistance, clingDistance);
        if (!initialStartEligible)
        {
            delayEligibleSinceMs = FarChaseDelayNotPendingMs;
            return false;
        }

        var clampedDelaySeconds = Math.Clamp(delaySeconds, 0, 300);
        if (clampedDelaySeconds == 0)
        {
            delayEligibleSinceMs = FarChaseDelayNotPendingMs;
            return true;
        }

        if (delayEligibleSinceMs == FarChaseDelayNotPendingMs)
            delayEligibleSinceMs = nowMs;

        return nowMs - delayEligibleSinceMs >= clampedDelaySeconds * 1000L;
    }

    public static FrenMountPolicy GetNormalPolicy(
        bool farChaseRequested,
        bool localPlayerAvailable,
        float xzDistance,
        float clingDistance,
        bool frenMounted,
        bool flyYouFools)
    {
        if (farChaseRequested)
            return FrenMountPolicy.OwnMount;

        if (!localPlayerAvailable || xzDistance > clingDistance)
            return FrenMountPolicy.PreserveCurrent;

        if (!frenMounted)
            return FrenMountPolicy.OnFoot;

        return flyYouFools
            ? FrenMountPolicy.OwnMount
            : FrenMountPolicy.Pillion;
    }

    public static FrenMountCorrectionAction GetCorrectionAction(
        bool selfOnOwnMount,
        FrenMountPolicy desiredPolicy,
        bool correctionAllowed,
        bool airborne)
    {
        if (!selfOnOwnMount
            || !correctionAllowed
            || desiredPolicy is not (FrenMountPolicy.OnFoot or FrenMountPolicy.Pillion))
        {
            return FrenMountCorrectionAction.None;
        }

        return airborne
            ? FrenMountCorrectionAction.Land
            : FrenMountCorrectionAction.Dismount;
    }
}
