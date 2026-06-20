using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class FrenRiderMountPolicyTests
{
    [Fact]
    public void FarChaseStartsBeyondChaseDistanceAndRetiresOnlyAtClingRange()
    {
        Assert.True(FrenRiderMountPolicy.ShouldRequestFarChase(
            currentlyRequested: false,
            eligible: true,
            xzDistance: 120f,
            chaseDistance: 100f,
            clingDistance: 5f));

        Assert.True(FrenRiderMountPolicy.ShouldRequestFarChase(
            currentlyRequested: true,
            eligible: true,
            xzDistance: 50f,
            chaseDistance: 100f,
            clingDistance: 5f));

        Assert.False(FrenRiderMountPolicy.ShouldRequestFarChase(
            currentlyRequested: true,
            eligible: true,
            xzDistance: 5f,
            chaseDistance: 100f,
            clingDistance: 5f));

        Assert.False(FrenRiderMountPolicy.ShouldRequestFarChase(
            currentlyRequested: false,
            eligible: true,
            xzDistance: 4f,
            chaseDistance: 2f,
            clingDistance: 5f));
    }

    [Fact]
    public void DefaultFarChaseDelayBlocksImmediateStart()
    {
        var config = new CharacterConfig();
        var pendingSince = FrenRiderMountPolicy.FarChaseDelayNotPendingMs;

        Assert.False(FrenRiderMountPolicy.ShouldRequestFarChase(
            currentlyRequested: false,
            eligible: true,
            xzDistance: 120f,
            chaseDistance: 100f,
            clingDistance: 5f,
            delaySeconds: config.MountUpToChaseFrenDelaySeconds,
            nowMs: 1000,
            ref pendingSince));
        Assert.Equal(30, config.MountUpToChaseFrenDelaySeconds);
        Assert.Equal(1000, pendingSince);
    }

    [Fact]
    public void ElapsedFarChaseDelayStartsChase()
    {
        var pendingSince = FrenRiderMountPolicy.FarChaseDelayNotPendingMs;

        Assert.False(FrenRiderMountPolicy.ShouldRequestFarChase(
            currentlyRequested: false,
            eligible: true,
            xzDistance: 120f,
            chaseDistance: 100f,
            clingDistance: 5f,
            delaySeconds: 30,
            nowMs: 1000,
            ref pendingSince));

        Assert.True(FrenRiderMountPolicy.ShouldRequestFarChase(
            currentlyRequested: false,
            eligible: true,
            xzDistance: 120f,
            chaseDistance: 100f,
            clingDistance: 5f,
            delaySeconds: 30,
            nowMs: 31000,
            ref pendingSince));
    }

    [Fact]
    public void LostFarChaseEligibilityResetsDelay()
    {
        var pendingSince = FrenRiderMountPolicy.FarChaseDelayNotPendingMs;

        Assert.False(FrenRiderMountPolicy.ShouldRequestFarChase(
            currentlyRequested: false,
            eligible: true,
            xzDistance: 120f,
            chaseDistance: 100f,
            clingDistance: 5f,
            delaySeconds: 30,
            nowMs: 1000,
            ref pendingSince));

        Assert.False(FrenRiderMountPolicy.ShouldRequestFarChase(
            currentlyRequested: false,
            eligible: true,
            xzDistance: 99f,
            chaseDistance: 100f,
            clingDistance: 5f,
            delaySeconds: 30,
            nowMs: 5000,
            ref pendingSince));
        Assert.Equal(FrenRiderMountPolicy.FarChaseDelayNotPendingMs, pendingSince);

        Assert.False(FrenRiderMountPolicy.ShouldRequestFarChase(
            currentlyRequested: false,
            eligible: true,
            xzDistance: 120f,
            chaseDistance: 100f,
            clingDistance: 5f,
            delaySeconds: 30,
            nowMs: 31000,
            ref pendingSince));
        Assert.Equal(31000, pendingSince);
    }

    [Fact]
    public void ZeroFarChaseDelayPreservesImmediateStart()
    {
        var pendingSince = FrenRiderMountPolicy.FarChaseDelayNotPendingMs;

        Assert.True(FrenRiderMountPolicy.ShouldRequestFarChase(
            currentlyRequested: false,
            eligible: true,
            xzDistance: 120f,
            chaseDistance: 100f,
            clingDistance: 5f,
            delaySeconds: 0,
            nowMs: 1000,
            ref pendingSince));
        Assert.Equal(FrenRiderMountPolicy.FarChaseDelayNotPendingMs, pendingSince);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(301, 300)]
    public void CharacterConfigClampsFarChaseDelay(int input, int expected)
    {
        var config = new CharacterConfig
        {
            MountUpToChaseFrenDelaySeconds = input,
        };

        Assert.Equal(expected, config.MountUpToChaseFrenDelaySeconds);
        Assert.Equal(expected, config.Clone().MountUpToChaseFrenDelaySeconds);
    }

    [Fact]
    public void ActiveFarChaseWithDelayStillRetiresOnlyAtClingRange()
    {
        var pendingSince = FrenRiderMountPolicy.FarChaseDelayNotPendingMs;

        Assert.True(FrenRiderMountPolicy.ShouldRequestFarChase(
            currentlyRequested: true,
            eligible: true,
            xzDistance: 50f,
            chaseDistance: 100f,
            clingDistance: 5f,
            delaySeconds: 30,
            nowMs: 1000,
            ref pendingSince));

        Assert.False(FrenRiderMountPolicy.ShouldRequestFarChase(
            currentlyRequested: true,
            eligible: true,
            xzDistance: 5f,
            chaseDistance: 100f,
            clingDistance: 5f,
            delaySeconds: 30,
            nowMs: 1000,
            ref pendingSince));
    }

    [Fact]
    public void SafetyPreemptionRetiresFarChaseImmediately()
    {
        Assert.False(FrenRiderMountPolicy.ShouldRequestFarChase(
            currentlyRequested: true,
            eligible: false,
            xzDistance: 250f,
            chaseDistance: 100f,
            clingDistance: 5f));
    }

    [Theory]
    [InlineData(true, 50f, 5f, false, false, FrenMountPolicy.OwnMount)]
    [InlineData(false, 50f, 5f, false, false, FrenMountPolicy.PreserveCurrent)]
    [InlineData(false, 5f, 5f, false, false, FrenMountPolicy.OnFoot)]
    [InlineData(false, 5f, 5f, true, false, FrenMountPolicy.Pillion)]
    [InlineData(false, 5f, 5f, true, true, FrenMountPolicy.OwnMount)]
    public void NormalMountPolicyCoversFarChaseAndEveryClingOutcome(
        bool farChaseRequested,
        float xzDistance,
        float clingDistance,
        bool frenMounted,
        bool flyYouFools,
        FrenMountPolicy expected)
    {
        Assert.Equal(expected, FrenRiderMountPolicy.GetNormalPolicy(
            farChaseRequested,
            localPlayerAvailable: true,
            xzDistance,
            clingDistance,
            frenMounted,
            flyYouFools));
    }

    [Theory]
    [InlineData(FrenMountPolicy.OnFoot, true, FrenMountCorrectionAction.Land)]
    [InlineData(FrenMountPolicy.Pillion, true, FrenMountCorrectionAction.Land)]
    [InlineData(FrenMountPolicy.OnFoot, false, FrenMountCorrectionAction.Dismount)]
    [InlineData(FrenMountPolicy.Pillion, false, FrenMountCorrectionAction.Dismount)]
    [InlineData(FrenMountPolicy.OwnMount, false, FrenMountCorrectionAction.None)]
    [InlineData(FrenMountPolicy.PreserveCurrent, false, FrenMountCorrectionAction.None)]
    public void CorrectionPolicyLandsBeforeRequiredDismount(
        FrenMountPolicy desiredPolicy,
        bool airborne,
        FrenMountCorrectionAction expected)
    {
        Assert.Equal(expected, FrenRiderMountPolicy.GetCorrectionAction(
            selfOnOwnMount: true,
            desiredPolicy,
            correctionAllowed: true,
            airborne));
    }

    [Fact]
    public void SafetyPreemptionBlocksLandingAndDismount()
    {
        Assert.Equal(FrenMountCorrectionAction.None, FrenRiderMountPolicy.GetCorrectionAction(
            selfOnOwnMount: true,
            FrenMountPolicy.Pillion,
            correctionAllowed: false,
            airborne: true));
    }

    [Fact]
    public void RetiredFarChaseRestoresLandingDismountAndPillionHandoff()
    {
        var policy = FrenRiderMountPolicy.GetNormalPolicy(
            farChaseRequested: false,
            localPlayerAvailable: true,
            xzDistance: 5f,
            clingDistance: 5f,
            frenMounted: true,
            flyYouFools: false);

        Assert.Equal(FrenMountPolicy.Pillion, policy);
        Assert.Equal(FrenMountCorrectionAction.Land, FrenRiderMountPolicy.GetCorrectionAction(
            selfOnOwnMount: true,
            policy,
            correctionAllowed: true,
            airborne: true));
        Assert.Equal(FrenMountCorrectionAction.Dismount, FrenRiderMountPolicy.GetCorrectionAction(
            selfOnOwnMount: true,
            policy,
            correctionAllowed: true,
            airborne: false));
    }
}
