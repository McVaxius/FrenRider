using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class QuestionableDutyCombatGatePolicyTests
{
    [Fact]
    public void DoesNotGateWhenQuestionableWasNotRunning()
    {
        var policy = new QuestionableDutyCombatGatePolicy();

        var decision = policy.Update(Input(inDuty: true, wasInDuty: false, questionableRunningOrRecent: false, ready: true));

        Assert.False(policy.IsActive);
        Assert.False(decision.ShouldForceCombatOff);
        Assert.False(decision.ShouldActivate);
    }

    [Fact]
    public void ArmsOnDutyEntryWhenQuestionableIsRunning()
    {
        var policy = new QuestionableDutyCombatGatePolicy();

        var decision = policy.Update(Input(inDuty: true, wasInDuty: false, questionableRunningOrRecent: true, ready: false));

        Assert.True(policy.IsActive);
        Assert.True(decision.JustArmed);
        Assert.True(decision.ShouldForceCombatOff);
        Assert.False(decision.ShouldActivate);
    }

    [Fact]
    public void ArmsOnDutyEntryWhenQuestionableWasRecentlyRunning()
    {
        var policy = new QuestionableDutyCombatGatePolicy();

        var decision = policy.Update(Input(inDuty: true, wasInDuty: false, questionableRunningOrRecent: true, ready: false));

        Assert.True(policy.IsActive);
        Assert.True(decision.ShouldForceCombatOff);
    }

    [Fact]
    public void ReadyTimerResetsWhenReadinessDrops()
    {
        var policy = new QuestionableDutyCombatGatePolicy();

        policy.Update(Input(inDuty: true, wasInDuty: false, questionableRunningOrRecent: true, ready: false, nowMs: 0));
        policy.Update(Input(inDuty: true, wasInDuty: true, ready: true, nowMs: 1_000));
        policy.Update(Input(inDuty: true, wasInDuty: true, ready: false, notReadyReason: "cutscene", nowMs: 4_000));
        policy.Update(Input(inDuty: true, wasInDuty: true, ready: true, nowMs: 8_000));
        var tooEarly = policy.Update(Input(inDuty: true, wasInDuty: true, ready: true, nowMs: 12_999));
        var released = policy.Update(Input(inDuty: true, wasInDuty: true, ready: true, nowMs: 13_000));

        Assert.False(tooEarly.ShouldActivate);
        Assert.True(released.ShouldActivate);
    }

    [Fact]
    public void ActivationRequiresFiveContinuousReadySeconds()
    {
        var policy = new QuestionableDutyCombatGatePolicy();

        policy.Update(Input(inDuty: true, wasInDuty: false, questionableRunningOrRecent: true, ready: false, nowMs: 0));
        policy.Update(Input(inDuty: true, wasInDuty: true, ready: true, nowMs: 100));
        var tooEarly = policy.Update(Input(inDuty: true, wasInDuty: true, ready: true, nowMs: 5_099));
        var released = policy.Update(Input(inDuty: true, wasInDuty: true, ready: true, nowMs: 5_100));

        Assert.False(tooEarly.ShouldActivate);
        Assert.True(released.ShouldActivate);
        Assert.False(policy.IsActive);
    }

    [Fact]
    public void ClearsWithoutActivationWhenLeavingDuty()
    {
        var policy = new QuestionableDutyCombatGatePolicy();

        policy.Update(Input(inDuty: true, wasInDuty: false, questionableRunningOrRecent: true, ready: false));
        var cleared = policy.Update(Input(inDuty: false, wasInDuty: true, ready: false, nowMs: 1_000));

        Assert.True(cleared.ClearedWithoutActivation);
        Assert.False(cleared.ShouldActivate);
        Assert.False(policy.IsActive);
    }

    private static QuestionableDutyCombatGateInput Input(
        bool enabled = true,
        bool inDuty = false,
        bool wasInDuty = false,
        bool zoneChanged = false,
        bool questionableRunningOrRecent = false,
        bool ready = false,
        string notReadyReason = "",
        long nowMs = 0)
    {
        return new QuestionableDutyCombatGateInput(
            enabled,
            inDuty,
            wasInDuty,
            zoneChanged,
            questionableRunningOrRecent,
            ready,
            notReadyReason,
            nowMs);
    }
}
