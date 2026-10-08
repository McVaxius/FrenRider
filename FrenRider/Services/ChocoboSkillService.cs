using System;
using System.Linq;
using FrenRider.Models;

namespace FrenRider.Services;

internal enum ChocoboSkillConfirmationResult
{
    Waiting,
    Accepted,
    Rejected,
}

/// <summary>Allocate an ordered skill tree without respec or replaying unacknowledged commands.</summary>
internal sealed class ChocoboSkillService
{
    private const long PreparationMs = 15_000;
    private const long AcknowledgementMs = 3_000;
    private readonly Func<bool> canRun;
    private readonly Func<ulong> identity;
    private readonly Func<CharacterConfig?> currentProfile;
    private readonly Func<CompanionSnapshot?> read;
    private readonly Func<CompanionSnapshot, ChocoboSkillStep, bool> dispatch;
    private readonly Action<string> log;
    private readonly Func<long> clock;
    private readonly Func<bool>? prepare;
    private readonly Func<bool>? prepared;
    private readonly Action? release;
    private readonly Func<CompanionSnapshot, ChocoboSkillStep, ChocoboSkillConfirmationResult>? confirm;
    private CharacterConfig? profile;
    private ulong owner;
    private ChocoboSkillTree[] priority = Array.Empty<ChocoboSkillTree>();
    private CompanionSnapshot before;
    private ChocoboSkillStep? pending;
    private long deadline;
    private bool automatic;
    private bool awaitingPreparation;
    private bool preparationOwned;
    private bool confirmationAccepted;
    private int learned;
    private CompanionSnapshot? lastAutomaticState;
    private CharacterConfig? lastAutomaticProfile;
    private int[]? lastAutomaticPriority;
    private bool automaticSuspended;

    internal bool IsActive { get; private set; }
    internal string Status { get; private set; } = "No skill allocation is pending.";

    internal ChocoboSkillService(Func<bool> canRun, Func<ulong> identity,
        Func<CharacterConfig?> currentProfile, Func<CompanionSnapshot?> read,
        Func<CompanionSnapshot, ChocoboSkillStep, bool> dispatch, Action<string> log, Func<long> clock,
        Func<bool>? prepare = null, Func<bool>? prepared = null, Action? release = null,
        Func<CompanionSnapshot, ChocoboSkillStep, ChocoboSkillConfirmationResult>? confirm = null)
    {
        this.canRun = canRun;
        this.identity = identity;
        this.currentProfile = currentProfile;
        this.read = read;
        this.dispatch = dispatch;
        this.log = log;
        this.clock = clock;
        this.prepare = prepare;
        this.prepared = prepared;
        this.release = release;
        this.confirm = confirm;
    }

    internal bool Start(bool automatically = false)
    {
        if (IsActive) return false;
        try
        {
            var active = currentProfile();
            var snapshot = read();
            if (!canRun() || identity() == 0 || active == null || !snapshot.HasValue
                || (automatically && (!active.Enabled || !active.ChocoboAutoAllocateSkills)))
            { Status = "Skill allocation is unavailable in the current context."; return false; }
            var selected = active.ChocoboSkillPriority;
            priority = selected?.Select(value => (ChocoboSkillTree)value).ToArray()
                ?? Array.Empty<ChocoboSkillTree>();
            if (!ChocoboSkillPlan.TryGetNext(snapshot.Value, priority, out var step))
            { Status = "No affordable next skill in the configured priority."; return false; }
            profile = active;
            owner = identity();
            automatic = automatically;
            learned = 0;
            automaticSuspended = false; // An explicit eligible start permits a new attempt.
            if (active.ChocoboAutoAllocateSkills)
            {
                lastAutomaticProfile = active;
                lastAutomaticState = snapshot;
                lastAutomaticPriority = selected?.ToArray();
            }
            IsActive = true;
            if (prepare != null)
            {
                before = snapshot.Value;
                pending = step;
                awaitingPreparation = true;
                preparationOwned = true; // Own cleanup before preparation can reject or throw.
                deadline = clock() + PreparationMs;
                Status = "Waiting for Companion readiness.";
                if (!prepare()) Stop("Skill allocation is unavailable in the current context.");
            }
            else RequestNext(snapshot.Value);
            return IsActive;
        }
        catch (Exception ex) { Stop("Skill allocation failed."); log($"allocation failed: {ex.GetType().Name}"); return false; }
    }

    internal void CheckAutomatic()
    {
        var active = currentProfile();
        if (active?.Enabled != true || !active.ChocoboAutoAllocateSkills)
        {
            ResetAutomaticState();
            return;
        }
        if (IsActive || !canRun()) return;
        var snapshot = read();
        if (!snapshot.HasValue) return;
        var selected = active.ChocoboSkillPriority;
        if (automaticSuspended && ReferenceEquals(active, lastAutomaticProfile)
            && selected != null && lastAutomaticPriority != null
            && selected.SequenceEqual(lastAutomaticPriority)) return;
        if (ReferenceEquals(active, lastAutomaticProfile) && lastAutomaticState.HasValue
            && SameSkills(snapshot.Value, lastAutomaticState.Value)
            && selected != null && lastAutomaticPriority != null
            && selected.SequenceEqual(lastAutomaticPriority)) return;
        // Record before dispatch: rejection or missing acknowledgement must not replay each tick.
        lastAutomaticProfile = active;
        lastAutomaticState = snapshot;
        lastAutomaticPriority = selected?.ToArray();
        Start(automatically: true);
    }

    internal void Tick()
    {
        try
        {
            var active = currentProfile();
            if (active?.Enabled != true || !active.ChocoboAutoAllocateSkills)
                ResetAutomaticState();
            if (!IsActive) return;
            if (!canRun() || identity() != owner || !ReferenceEquals(active, profile)
                || active?.ChocoboSkillPriority == null
                || !active.ChocoboSkillPriority.SequenceEqual(priority.Select(tree => (int)tree))
                || (automatic && (!active.Enabled || !active.ChocoboAutoAllocateSkills)))
            { Stop("Skill allocation cancelled: context or profile changed."); return; }
            var snapshot = read();
            if (!snapshot.HasValue || !pending.HasValue)
            { Stop("Skill allocation cancelled: companion data unavailable."); return; }
            var step = pending.Value;
            if (awaitingPreparation)
            {
                if (snapshot.Value != before)
                { Stop("Skill allocation stopped: unexpected progression change."); return; }
                if (clock() >= deadline)
                { Stop("Skill allocation stopped: Companion readiness was not observed; no retry."); return; }
                if (prepared?.Invoke() == false) return;
                if (!IsActive) return;
                snapshot = read();
                if (!snapshot.HasValue || snapshot.Value != before
                    || !ChocoboSkillPlan.TryGetNext(snapshot.Value, priority, out var readyStep) || readyStep != step)
                { Stop("Skill allocation stopped: unexpected progression change."); return; }
                awaitingPreparation = false; // Consume readiness before native dispatch.
                RequestNext(snapshot.Value);
                return;
            }
            if (prepared?.Invoke() == false)
            { Stop("Skill allocation cancelled: companion data unavailable."); return; }
            if (!IsActive) return;
            if (IsAcknowledged(before, snapshot.Value, step))
            {
                learned++;
                log($"observed learning: tree={(int)step.Tree}; level={step.Level}; cost={step.Cost}; points={snapshot.Value.SkillPoints}");
                pending = null;
                if (profile?.ChocoboAutoAllocateSkills == true) lastAutomaticState = snapshot;
                RequestNext(snapshot.Value);
            }
            else if (!SameSkills(before, snapshot.Value))
                Stop("Skill allocation stopped: unexpected progression change.");
            else if (clock() >= deadline)
                Stop("Skill allocation stopped: learning was not observed; no retry.");
            else if (confirm != null && !confirmationAccepted)
            {
                var result = confirm(before, step);
                if (!IsActive) return;
                switch (result)
                {
                    case ChocoboSkillConfirmationResult.Waiting:
                        break;
                    case ChocoboSkillConfirmationResult.Accepted:
                        confirmationAccepted = true;
                        deadline = clock() + AcknowledgementMs;
                        break;
                    default:
                        Stop("Skill learning request was rejected; no retry.");
                        break;
                }
            }
        }
        catch (Exception ex) { Stop("Skill allocation failed."); log($"allocation failed: {ex.GetType().Name}"); }
    }

    private void RequestNext(CompanionSnapshot snapshot)
    {
        if (learned >= 30 || !ChocoboSkillPlan.TryGetNext(snapshot, priority, out var step))
        { Stop("Skill allocation completed; progression was observed."); return; }
        before = snapshot;
        pending = step;
        confirmationAccepted = false;
        deadline = clock() + AcknowledgementMs;
        Status = "Waiting for observed skill learning.";
        log($"dispatch learning: tree={(int)step.Tree}; level={step.Level}; cost={step.Cost}");
        if (!dispatch(snapshot, step)) Stop("Skill learning request was rejected; no retry.");
    }

    internal void Stop(string reason = "Skill allocation stopped.")
    {
        var wasActive = IsActive;
        if (pending.HasValue) automaticSuspended = true;
        IsActive = false;
        pending = null;
        awaitingPreparation = false;
        confirmationAccepted = false;
        profile = null;
        Status = reason;
        var releasePreparation = preparationOwned;
        preparationOwned = false; // Cleanup is consumed before callbacks and cannot repeat.
        if (releasePreparation)
        {
            try { release?.Invoke(); }
            catch (Exception ex) { log($"allocation cleanup failed: {ex.GetType().Name}"); }
        }
        if (wasActive) log(reason);
    }

    private void ResetAutomaticState()
    {
        lastAutomaticState = null;
        lastAutomaticProfile = null;
        lastAutomaticPriority = null;
        automaticSuspended = false;
    }

    private static bool SameSkills(CompanionSnapshot left, CompanionSnapshot right)
        => left.Rank == right.Rank && left.SkillPoints == right.SkillPoints
            && left.DefenderLevel == right.DefenderLevel && left.AttackerLevel == right.AttackerLevel
            && left.HealerLevel == right.HealerLevel;

    internal static bool IsAcknowledged(CompanionSnapshot before, CompanionSnapshot after, ChocoboSkillStep step)
    {
        if (before.SkillPoints < step.Cost || after.SkillPoints != before.SkillPoints - step.Cost
            || after.Rank != before.Rank) return false;
        return after.DefenderLevel == (step.Tree == ChocoboSkillTree.Defender ? step.Level : before.DefenderLevel)
            && after.AttackerLevel == (step.Tree == ChocoboSkillTree.Attacker ? step.Level : before.AttackerLevel)
            && after.HealerLevel == (step.Tree == ChocoboSkillTree.Healer ? step.Level : before.HealerLevel);
    }
}
