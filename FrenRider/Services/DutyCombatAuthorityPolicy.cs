using FrenRider.Models;

namespace FrenRider.Services;

internal enum DutyCombatAuthority
{
    None,
    FrenRider,
    QuestionableSolo,
}

internal sealed class DutyCombatAuthorityPolicy
{
    public DutyCombatAuthority Authority { get; private set; }
    public bool FrenRiderBootstrapComplete { get; private set; }

    private bool questionableShutdownSent;

    public DutyCombatAuthorityDecision Update(DutyCombatAuthorityInput input)
    {
        if (!input.Enabled)
            return Reset("disabled");

        if (!input.InDuty)
            return Reset("left duty");

        var isTrueSoloDuty = IsTrueSoloDuty(input.BoundByDuty95, input.DutyCategory);
        var previousAuthority = Authority;

        // QuestionableSolo is the only sticky authority. Once armed, a later
        // Questionable.IsRunning=false must not let FrenRider reclaim combat.
        if (Authority != DutyCombatAuthority.QuestionableSolo)
        {
            Authority = isTrueSoloDuty && input.QuestionableRunningOrRecent
                ? DutyCombatAuthority.QuestionableSolo
                : DutyCombatAuthority.FrenRider;
        }

        var shouldForceCombatOff = false;
        var shouldBootstrapFrenRider = false;

        if (Authority == DutyCombatAuthority.QuestionableSolo)
        {
            if (!questionableShutdownSent)
            {
                questionableShutdownSent = true;
                shouldForceCombatOff = true;
            }
        }
        else if (input.FrenRiderBootstrapAllowed && !FrenRiderBootstrapComplete)
        {
            FrenRiderBootstrapComplete = true;
            shouldBootstrapFrenRider = true;
        }

        return new DutyCombatAuthorityDecision(
            previousAuthority,
            Authority,
            previousAuthority != Authority,
            shouldForceCombatOff,
            shouldBootstrapFrenRider,
            isTrueSoloDuty,
            BuildReason(Authority, isTrueSoloDuty, input.DutyCategory));
    }

    public DutyCombatAuthorityDecision Reset(string reason)
    {
        var previousAuthority = Authority;
        Authority = DutyCombatAuthority.None;
        FrenRiderBootstrapComplete = false;
        questionableShutdownSent = false;

        return new DutyCombatAuthorityDecision(
            previousAuthority,
            DutyCombatAuthority.None,
            previousAuthority != DutyCombatAuthority.None,
            false,
            false,
            false,
            string.IsNullOrWhiteSpace(reason) ? "authority cleared" : reason);
    }

    internal static bool IsTrueSoloDuty(bool boundByDuty95, AdsDutyCategory? dutyCategory)
        => boundByDuty95 || dutyCategory == AdsDutyCategory.Solo;

    private static string BuildReason(
        DutyCombatAuthority authority,
        bool isTrueSoloDuty,
        AdsDutyCategory? dutyCategory)
    {
        if (authority == DutyCombatAuthority.QuestionableSolo)
            return "true solo duty with Questionable running or recently running";

        if (isTrueSoloDuty)
            return "true solo duty without Questionable ownership";

        return dutyCategory is { } category
            ? $"{AdsDutyCategoryCatalog.GetLabel(category)} duty"
            : "unknown duty category (defaults to FrenRider)";
    }
}

internal readonly record struct DutyCombatAuthorityInput(
    bool Enabled,
    bool InDuty,
    bool BoundByDuty95,
    AdsDutyCategory? DutyCategory,
    bool QuestionableRunningOrRecent,
    bool FrenRiderBootstrapAllowed);

internal readonly record struct DutyCombatAuthorityDecision(
    DutyCombatAuthority PreviousAuthority,
    DutyCombatAuthority Authority,
    bool AuthorityChanged,
    bool ShouldForceCombatOff,
    bool ShouldBootstrapFrenRider,
    bool IsTrueSoloDuty,
    string Reason);
