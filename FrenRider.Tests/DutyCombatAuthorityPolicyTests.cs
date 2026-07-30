using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class DutyCombatAuthorityPolicyTests
{
    [Fact]
    public void BoundByDuty95WithQuestionableLatchesUntilDutyExit()
    {
        var policy = new DutyCombatAuthorityPolicy();

        var entered = policy.Update(Input(
            boundByDuty95: true,
            dutyCategory: AdsDutyCategory.Solo,
            questionableRunningOrRecent: true));
        var questionableStopped = policy.Update(Input(
            boundByDuty95: true,
            dutyCategory: AdsDutyCategory.Solo,
            questionableRunningOrRecent: false));
        var left = policy.Update(Input(inDuty: false));

        Assert.Equal(DutyCombatAuthority.QuestionableSolo, entered.Authority);
        Assert.True(entered.ShouldForceCombatOff);
        Assert.False(entered.ShouldBootstrapFrenRider);
        Assert.Equal(DutyCombatAuthority.QuestionableSolo, questionableStopped.Authority);
        Assert.False(questionableStopped.ShouldForceCombatOff);
        Assert.False(questionableStopped.ShouldBootstrapFrenRider);
        Assert.Equal(DutyCombatAuthority.None, left.Authority);
        Assert.True(left.AuthorityChanged);
        Assert.False(policy.FrenRiderBootstrapComplete);
    }

    [Fact]
    public void ValidatedAdsSoloCategoryTransfersAuthorityWhenBoundByDuty95IsAbsent()
    {
        var policy = new DutyCombatAuthorityPolicy();

        var decision = policy.Update(Input(
            dutyCategory: AdsDutyCategory.Solo,
            questionableRunningOrRecent: true));

        Assert.True(decision.IsTrueSoloDuty);
        Assert.Equal(DutyCombatAuthority.QuestionableSolo, decision.Authority);
        Assert.True(decision.ShouldForceCombatOff);
    }

    [Fact]
    public void SoloDutyWithoutQuestionableRemainsFrenRiderOwned()
    {
        var policy = new DutyCombatAuthorityPolicy();

        var decision = policy.Update(Input(
            boundByDuty95: true,
            dutyCategory: AdsDutyCategory.Solo,
            questionableRunningOrRecent: false));

        Assert.Equal(DutyCombatAuthority.FrenRider, decision.Authority);
        Assert.True(decision.ShouldBootstrapFrenRider);
        Assert.False(decision.ShouldForceCombatOff);
    }

    [Theory]
    [InlineData(AdsDutyCategory.FourMan)]
    [InlineData(AdsDutyCategory.EightMan)]
    [InlineData(AdsDutyCategory.Alliance)]
    [InlineData(AdsDutyCategory.GuildHest)]
    [InlineData(AdsDutyCategory.DeepDungeon)]
    [InlineData(AdsDutyCategory.TreasureDungeon)]
    [InlineData(AdsDutyCategory.Other)]
    public void NonSoloCategoriesRemainFrenRiderOwnedEvenWhenQuestionableRuns(AdsDutyCategory category)
    {
        var policy = new DutyCombatAuthorityPolicy();

        var first = policy.Update(Input(
            dutyCategory: category,
            questionableRunningOrRecent: true));
        var second = policy.Update(Input(
            dutyCategory: category,
            questionableRunningOrRecent: true));

        Assert.Equal(DutyCombatAuthority.FrenRider, first.Authority);
        Assert.True(first.ShouldBootstrapFrenRider);
        Assert.False(first.ShouldForceCombatOff);
        Assert.False(second.ShouldBootstrapFrenRider);
        Assert.False(second.ShouldForceCombatOff);
    }

    [Fact]
    public void UnknownCategoryDefaultsToFrenRiderAuthority()
    {
        var policy = new DutyCombatAuthorityPolicy();

        var decision = policy.Update(Input(
            dutyCategory: null,
            questionableRunningOrRecent: true));

        Assert.Equal(DutyCombatAuthority.FrenRider, decision.Authority);
        Assert.True(decision.ShouldBootstrapFrenRider);
        Assert.False(decision.ShouldForceCombatOff);
    }

    [Fact]
    public void BoundByDuty95WithoutValidatedAdsCategoryDefaultsToFrenRiderAuthority()
    {
        var policy = new DutyCombatAuthorityPolicy();

        var decision = policy.Update(Input(
            boundByDuty95: true,
            dutyCategory: null,
            questionableRunningOrRecent: true));

        Assert.False(decision.IsTrueSoloDuty);
        Assert.Equal(DutyCombatAuthority.FrenRider, decision.Authority);
        Assert.True(decision.ShouldBootstrapFrenRider);
        Assert.False(decision.ShouldForceCombatOff);
    }

    [Fact]
    public void LateSoloDetectionCanTransferAuthorityToQuestionable()
    {
        var policy = new DutyCombatAuthorityPolicy();

        var unknown = policy.Update(Input(
            dutyCategory: null,
            questionableRunningOrRecent: true));
        var classified = policy.Update(Input(
            dutyCategory: AdsDutyCategory.Solo,
            questionableRunningOrRecent: true));

        Assert.Equal(DutyCombatAuthority.FrenRider, unknown.Authority);
        Assert.True(unknown.ShouldBootstrapFrenRider);
        Assert.Equal(DutyCombatAuthority.QuestionableSolo, classified.Authority);
        Assert.True(classified.AuthorityChanged);
        Assert.True(classified.ShouldForceCombatOff);
        Assert.False(classified.ShouldBootstrapFrenRider);
    }

    [Fact]
    public void CoppeliaOrUtilitySafetyCanDelayBootstrapWithoutConsumingIt()
    {
        var policy = new DutyCombatAuthorityPolicy();

        var blocked = policy.Update(Input(
            dutyCategory: AdsDutyCategory.FourMan,
            frenRiderBootstrapAllowed: false));
        var released = policy.Update(Input(
            dutyCategory: AdsDutyCategory.FourMan,
            frenRiderBootstrapAllowed: true));
        var later = policy.Update(Input(
            dutyCategory: AdsDutyCategory.FourMan,
            frenRiderBootstrapAllowed: true));

        Assert.Equal(DutyCombatAuthority.FrenRider, blocked.Authority);
        Assert.False(blocked.ShouldBootstrapFrenRider);
        Assert.True(released.ShouldBootstrapFrenRider);
        Assert.False(later.ShouldBootstrapFrenRider);
    }

    [Fact]
    public void AdsOwnedFourManDutyStillBootstrapsExactlyOnceWithoutQuestionableShutdown()
    {
        Assert.True(AdsIntegrationPolicy.ShouldPauseDutySystems(
            handoffPending: false,
            runtimeOwned: true,
            exitTakeoverActive: false));

        var policy = new DutyCombatAuthorityPolicy();
        var first = policy.Update(Input(
            dutyCategory: AdsDutyCategory.FourMan,
            questionableRunningOrRecent: true));
        var second = policy.Update(Input(
            dutyCategory: AdsDutyCategory.FourMan,
            questionableRunningOrRecent: true));

        Assert.Equal(DutyCombatAuthority.FrenRider, first.Authority);
        Assert.True(first.ShouldBootstrapFrenRider);
        Assert.False(first.ShouldForceCombatOff);
        Assert.False(second.ShouldBootstrapFrenRider);
        Assert.False(second.ShouldForceCombatOff);
    }

    [Fact]
    public void DisableClearsAuthorityAndAllowsOneBootstrapAfterMidDutyReenable()
    {
        var policy = new DutyCombatAuthorityPolicy();

        var initial = policy.Update(Input(dutyCategory: AdsDutyCategory.FourMan));
        var disabled = policy.Update(Input(enabled: false, dutyCategory: AdsDutyCategory.FourMan));
        var reenabled = policy.Update(Input(dutyCategory: AdsDutyCategory.FourMan));
        var repeated = policy.Update(Input(dutyCategory: AdsDutyCategory.FourMan));

        Assert.True(initial.ShouldBootstrapFrenRider);
        Assert.Equal(DutyCombatAuthority.None, disabled.Authority);
        Assert.False(disabled.ShouldBootstrapFrenRider);
        Assert.True(reenabled.ShouldBootstrapFrenRider);
        Assert.False(repeated.ShouldBootstrapFrenRider);
    }

    [Fact]
    public void QuestionableBackendCanStartAfterInitialShutdownWithoutFrenRiderReclaim()
    {
        var policy = new DutyCombatAuthorityPolicy();

        var initialShutdown = policy.Update(Input(
            dutyCategory: AdsDutyCategory.Solo,
            questionableRunningOrRecent: true));
        var backendStartedAndQuestionableStoppedReporting = policy.Update(Input(
            dutyCategory: AdsDutyCategory.Solo,
            questionableRunningOrRecent: false));

        Assert.True(initialShutdown.ShouldForceCombatOff);
        Assert.False(backendStartedAndQuestionableStoppedReporting.ShouldForceCombatOff);
        Assert.False(backendStartedAndQuestionableStoppedReporting.ShouldBootstrapFrenRider);
        Assert.Equal(DutyCombatAuthority.QuestionableSolo, backendStartedAndQuestionableStoppedReporting.Authority);
    }

    [Fact]
    public void MidDutyReenableReacquiresQuestionableSoloWhenQuestionableIsStillDetected()
    {
        var policy = new DutyCombatAuthorityPolicy();

        policy.Update(Input(
            dutyCategory: AdsDutyCategory.Solo,
            questionableRunningOrRecent: true));
        policy.Update(Input(
            enabled: false,
            dutyCategory: AdsDutyCategory.Solo,
            questionableRunningOrRecent: true));
        var reenabled = policy.Update(Input(
            dutyCategory: AdsDutyCategory.Solo,
            questionableRunningOrRecent: true));

        Assert.Equal(DutyCombatAuthority.QuestionableSolo, reenabled.Authority);
        Assert.True(reenabled.ShouldForceCombatOff);
        Assert.False(reenabled.ShouldBootstrapFrenRider);
    }

    private static DutyCombatAuthorityInput Input(
        bool enabled = true,
        bool inDuty = true,
        bool boundByDuty95 = false,
        AdsDutyCategory? dutyCategory = AdsDutyCategory.FourMan,
        bool questionableRunningOrRecent = false,
        bool frenRiderBootstrapAllowed = true)
    {
        return new DutyCombatAuthorityInput(
            enabled,
            inDuty,
            boundByDuty95,
            dutyCategory,
            questionableRunningOrRecent,
            frenRiderBootstrapAllowed);
    }
}
