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
