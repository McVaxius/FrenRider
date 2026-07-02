namespace FrenRider.Services;

internal sealed class QuestionableDutyCombatGatePolicy
{
    public const long ReadyHoldMs = 5_000;

    private long readySinceMs = -1;
    private bool forceOffSent;

    public bool IsActive { get; private set; }

    public QuestionableDutyCombatGateDecision Update(QuestionableDutyCombatGateInput input)
    {
        if (!input.Enabled)
            return Reset("disabled");

        var justEnteredDuty = input.InDuty && !input.WasInDuty;
        var shouldArm = !IsActive
            && input.InDuty
            && (justEnteredDuty || input.ZoneChanged)
            && input.QuestionableRunningOrRecent;

        if (shouldArm)
        {
            IsActive = true;
            forceOffSent = false;
            readySinceMs = -1;
        }

        if (!IsActive)
            return QuestionableDutyCombatGateDecision.Inactive;

        if (!input.InDuty)
            return Reset("left duty");

        if (!forceOffSent)
        {
            forceOffSent = true;
            readySinceMs = -1;
            return new QuestionableDutyCombatGateDecision(
                true,
                shouldArm,
                true,
                false,
                false,
                "Questionable duty entry - combat held");
        }

        if (!input.Ready)
        {
            readySinceMs = -1;
            return new QuestionableDutyCombatGateDecision(
                true,
                false,
                false,
                false,
                false,
                string.IsNullOrWhiteSpace(input.NotReadyReason)
                    ? "Questionable duty entry - waiting"
                    : $"Questionable duty entry - waiting: {input.NotReadyReason}");
        }

        if (readySinceMs < 0)
        {
            readySinceMs = input.NowMs;
            return new QuestionableDutyCombatGateDecision(
                true,
                false,
                false,
                false,
                false,
                "Questionable duty entry - ready timer started");
        }

        var readyElapsedMs = input.NowMs - readySinceMs;
        if (readyElapsedMs < ReadyHoldMs)
        {
            var remainingMs = ReadyHoldMs - readyElapsedMs;
            return new QuestionableDutyCombatGateDecision(
                true,
                false,
                false,
                false,
                false,
                $"Questionable duty entry - ready in {(remainingMs + 999) / 1000}s");
        }

        IsActive = false;
        forceOffSent = false;
        readySinceMs = -1;
        return new QuestionableDutyCombatGateDecision(
            false,
            false,
            false,
            true,
            false,
            "Questionable duty entry - released");
    }

    public QuestionableDutyCombatGateDecision Reset(string reason)
    {
        var wasActive = IsActive;
        IsActive = false;
        forceOffSent = false;
        readySinceMs = -1;

        return new QuestionableDutyCombatGateDecision(
            false,
            false,
            false,
            false,
            wasActive,
            string.IsNullOrWhiteSpace(reason)
                ? "Questionable duty entry - cleared"
                : $"Questionable duty entry - cleared: {reason}");
    }
}

internal readonly record struct QuestionableDutyCombatGateInput(
    bool Enabled,
    bool InDuty,
    bool WasInDuty,
    bool ZoneChanged,
    bool QuestionableRunningOrRecent,
    bool Ready,
    string NotReadyReason,
    long NowMs);

internal readonly record struct QuestionableDutyCombatGateDecision(
    bool IsActive,
    bool JustArmed,
    bool ShouldForceCombatOff,
    bool ShouldActivate,
    bool ClearedWithoutActivation,
    string StateDetail)
{
    public static QuestionableDutyCombatGateDecision Inactive { get; } = new(
        false,
        false,
        false,
        false,
        false,
        string.Empty);
}
