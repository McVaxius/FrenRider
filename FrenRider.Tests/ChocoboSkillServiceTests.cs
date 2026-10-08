using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class ChocoboSkillServiceTests
{
    [Fact]
    public void HealerNineWaitsForBothLearnedLevelAndPointDebit()
    {
        var test = new Scenario();
        Assert.True(test.Service.Start());
        Assert.Single(test.Commands);
        Assert.Equal(new ChocoboSkillStep(ChocoboSkillTree.Healer, 9, 9, 26), test.Commands[0]);
        Assert.False(test.Service.Start());
        test.Service.Tick();
        Assert.True(test.Service.IsActive);
        test.State = test.State!.Value with { HealerLevel = 9, SkillPoints = 0 };
        test.Service.Tick();
        Assert.False(test.Service.IsActive);
        Assert.Contains(test.Logs, line => line.StartsWith("observed learning:"));
        Assert.Single(test.Commands);
    }

    [Theory]
    [InlineData(9, 9, 0)]
    [InlineData(8, 0, 0)]
    [InlineData(9, 0, 1)]
    public void PartialOrUnrelatedChangeNeverAcknowledgesOrDispatchesAnotherSkill(int healer, int points, int defender)
    {
        var test = new Scenario();
        test.Service.Start();
        test.State = test.State!.Value with { HealerLevel = (byte)healer, SkillPoints = (byte)points, DefenderLevel = (byte)defender };
        test.Service.Tick();
        Assert.False(test.Service.IsActive);
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("observed learning:"));
        Assert.Single(test.Commands);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 6)]
    public void PartialAcknowledgementCannotReplayTheSameEncodedSkill(int healer, int points)
    {
        var test = new Scenario();
        test.Profile.ChocoboAutoAllocateSkills = true;
        test.State = test.State!.Value with { HealerLevel = 0, SkillPoints = 6 };
        test.Service.CheckAutomatic();
        test.State = test.State!.Value with { HealerLevel = (byte)healer, SkillPoints = (byte)points };
        test.Service.Tick();
        for (var i = 0; i < 50; i++) test.Service.CheckAutomatic();
        Assert.Single(test.Commands);
        Assert.False(test.Service.IsActive);
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("observed learning:"));
    }

    [Fact]
    public void AcknowledgedStepsContinueInPriorityUntilUnaffordable()
    {
        var test = new Scenario();
        test.State = test.State!.Value with { HealerLevel = 0, SkillPoints = 6 };
        Assert.True(test.Service.Start());
        for (byte level = 1; level <= 3; level++)
        {
            test.State = test.State!.Value with { HealerLevel = level, SkillPoints = (byte)(test.State.Value.SkillPoints - level) };
            test.Service.Tick();
        }
        Assert.False(test.Service.IsActive);
        Assert.Equal(new[] { 1, 2, 3 }, test.Commands.Select(step => step.Level));
        Assert.Equal(3, test.Logs.Count(line => line.StartsWith("observed learning:")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticRejectionOrTimeoutNeverReplaysUnchangedState(bool accept)
    {
        var test = new Scenario { Accept = accept };
        test.Profile.ChocoboAutoAllocateSkills = true;
        test.Service.CheckAutomatic();
        test.Now = 3_000;
        test.Service.Tick();
        for (var i = 0; i < 50; i++) test.Service.CheckAutomatic();
        Assert.Single(test.Commands);
        Assert.False(test.Service.IsActive);
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("observed learning:"));
    }

    [Fact]
    public void AutoDisabledByDefaultAndExplicitManualStartDoesNotRequireFrenRiderEnabled()
    {
        var test = new Scenario();
        test.Profile.Enabled = false;
        test.Service.CheckAutomatic();
        Assert.Empty(test.Commands);
        Assert.True(test.Service.Start());
        Assert.Single(test.Commands);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("profile")]
    [InlineData("priority")]
    [InlineData("unsafe")]
    [InlineData("missing")]
    [InlineData("stop")]
    public void DepartureAndStopNeverAdvanceEvenWithAnAcknowledgement(string change)
    {
        var test = new Scenario();
        test.Service.Start();
        test.State = test.State!.Value with { HealerLevel = 9, SkillPoints = 0 };
        switch (change)
        {
            case "owner": test.Owner = 2; break;
            case "profile": test.Profile = test.Profile.Clone(); break;
            case "priority": test.Profile.ChocoboSkillPriority = new() { 0 }; break;
            case "unsafe": test.Safe = false; break;
            case "missing": test.State = null; break;
            case "stop": test.Service.Stop(); break;
        }
        test.Service.Tick();
        Assert.False(test.Service.IsActive);
        Assert.Single(test.Commands);
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("observed learning:"));
    }

    [Fact]
    public void DisablingAutomaticAllocationCancelsItsPendingCommand()
    {
        var test = new Scenario();
        test.Profile.ChocoboAutoAllocateSkills = true;
        test.Service.CheckAutomatic();
        test.Profile.ChocoboAutoAllocateSkills = false;
        test.Service.Tick();
        Assert.False(test.Service.IsActive);
        Assert.Single(test.Commands);
    }

    [Fact]
    public void StoppingAManualAttemptDoesNotLetEnabledAutomaticAllocationReplayIt()
    {
        var test = new Scenario();
        test.Profile.ChocoboAutoAllocateSkills = true;
        test.Service.Start();
        test.Service.Stop();
        test.Service.CheckAutomatic();
        Assert.False(test.Service.IsActive);
        Assert.Single(test.Commands);
    }

    [Fact]
    public void ExperienceChangeAloneDoesNotCauseDuplicateAutomaticAttempt()
    {
        var test = new Scenario { Accept = false };
        test.Profile.ChocoboAutoAllocateSkills = true;
        test.Service.CheckAutomatic();
        test.State = test.State!.Value with { CurrentXp = 17 };
        test.Service.CheckAutomatic();
        Assert.Single(test.Commands);
    }

    [Fact]
    public void ExplicitNewPriorityAllowsNewAutomaticAttempt()
    {
        var test = new Scenario { Accept = false };
        test.Profile.ChocoboAutoAllocateSkills = true;
        test.Service.CheckAutomatic();
        test.Profile.ChocoboSkillPriority = new() { 0 };
        test.Service.CheckAutomatic();
        Assert.Equal(2, test.Commands.Count);
        Assert.Equal(ChocoboSkillTree.Defender, test.Commands[1].Tree);
    }

    [Fact]
    public void ExplicitOffOnSelectionAllowsANewAutomaticAttemptAfterRejection()
    {
        var test = new Scenario { Accept = false };
        test.Profile.ChocoboAutoAllocateSkills = true;
        test.Service.CheckAutomatic();
        test.Profile.ChocoboAutoAllocateSkills = false;
        test.Service.Tick();
        test.Profile.ChocoboAutoAllocateSkills = true;
        test.Service.CheckAutomatic();
        Assert.Equal(2, test.Commands.Count);
    }

    [Fact]
    public void DispatchExceptionEndsAttemptWithoutReplay()
    {
        var test = new Scenario { ThrowOnDispatch = true };
        test.Profile.ChocoboAutoAllocateSkills = true;
        test.Service.CheckAutomatic();
        test.Service.Tick();
        test.Service.CheckAutomatic();
        Assert.False(test.Service.IsActive);
        Assert.Single(test.Commands);
    }

    [Fact]
    public void ProfileLookupExceptionCancelsPendingLearning()
    {
        var test = new Scenario();
        test.Service.Start();
        test.ThrowOnProfileRead = true;
        test.Service.Tick();
        test.ThrowOnProfileRead = false;
        test.State = test.State!.Value with { HealerLevel = 9, SkillPoints = 0 };
        test.Service.Tick();
        Assert.False(test.Service.IsActive);
        Assert.Single(test.Commands);
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("observed learning:"));
    }

    [Fact]
    public void PreparationOpensOnceAndWaitsForReadinessBeforeLearning()
    {
        var test = new Scenario(withPreparation: true);
        Assert.True(test.Service.Start());
        Assert.False(test.Service.Start());
        for (var i = 0; i < 5; i++) test.Service.Tick();
        test.Now = 14_999;
        test.Service.Tick();
        Assert.Empty(test.Commands);
        Assert.Equal(1, test.Preparations);
        Assert.Equal(0, test.Releases);

        test.Prepared = true;
        test.Service.Tick();
        Assert.Single(test.Commands);
        Assert.Equal(0, test.Releases);
        test.State = test.State!.Value with { HealerLevel = 9, SkillPoints = 0 };
        test.Service.Tick();
        test.Service.Stop();
        Assert.False(test.Service.IsActive);
        Assert.Equal(1, test.Releases);
        Assert.False(test.ReleasedWhileActive);
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("exception")]
    [InlineData("timeout")]
    public void PreparationFailureReleasesOnceAndCannotAutomaticallyReplay(string failure)
    {
        var test = new Scenario(withPreparation: true);
        test.Profile.ChocoboAutoAllocateSkills = true;
        test.PrepareAccept = failure != "rejected";
        test.ThrowOnPrepare = failure == "exception";
        test.Service.CheckAutomatic();
        if (failure == "timeout")
        {
            test.Prepared = true;
            test.Now = 15_000;
            test.Service.Tick();
        }
        test.ThrowOnPrepare = false;
        test.PrepareAccept = true;
        test.State = test.State!.Value with { SkillPoints = 10 };
        for (var i = 0; i < 50; i++) test.Service.CheckAutomatic();
        test.Service.Stop();
        Assert.False(test.Service.IsActive);
        Assert.Empty(test.Commands);
        Assert.Equal(1, test.Preparations);
        Assert.Equal(1, test.Releases);
        Assert.False(test.ReleasedWhileActive);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("profile")]
    [InlineData("priority")]
    [InlineData("unsafe")]
    [InlineData("missing")]
    [InlineData("stop")]
    [InlineData("automatic")]
    public void PreparationCancellationCannotClickAndReleasesItsWindow(string change)
    {
        var test = new Scenario(withPreparation: true);
        test.Profile.ChocoboAutoAllocateSkills = true;
        test.Service.CheckAutomatic();
        switch (change)
        {
            case "owner": test.Owner = 2; break;
            case "profile": test.Profile = test.Profile.Clone(); break;
            case "priority": test.Profile.ChocoboSkillPriority = new() { 0 }; break;
            case "unsafe": test.Safe = false; break;
            case "missing": test.State = null; break;
            case "stop": test.Service.Stop(); break;
            case "automatic": test.Profile.ChocoboAutoAllocateSkills = false; break;
        }
        test.Prepared = true;
        test.Service.Tick();
        test.Service.Stop();
        Assert.False(test.Service.IsActive);
        Assert.Empty(test.Commands);
        Assert.Equal(1, test.Releases);
        Assert.False(test.ReleasedWhileActive);
    }

    [Theory]
    [InlineData("points")]
    [InlineData("level")]
    [InlineData("experience")]
    public void PreparationRequiresTheExactOriginalSnapshot(string change)
    {
        var test = new Scenario(withPreparation: true);
        test.Profile.ChocoboAutoAllocateSkills = true;
        test.Service.CheckAutomatic();
        test.State = change switch
        {
            "points" => test.State!.Value with { SkillPoints = 10 },
            "level" => test.State!.Value with { HealerLevel = 9, SkillPoints = 0 },
            _ => test.State!.Value with { CurrentXp = 1 },
        };
        test.Prepared = true;
        test.Service.Tick();
        for (var i = 0; i < 50; i++) test.Service.CheckAutomatic();
        Assert.False(test.Service.IsActive);
        Assert.Empty(test.Commands);
        Assert.Equal(1, test.Preparations);
        Assert.Equal(1, test.Releases);
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("observed learning:"));
    }

    [Fact]
    public void ReadinessCannotChangeTheSnapshotBeforeDispatch()
    {
        var test = new Scenario(withPreparation: true) { Prepared = true };
        test.Service.Start();
        test.OnPrepared = () => test.State = test.State!.Value with { SkillPoints = 10 };
        test.Service.Tick();
        Assert.False(test.Service.IsActive);
        Assert.Empty(test.Commands);
        Assert.Equal(1, test.Releases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreparedDispatchFailureReleasesWithoutReplay(bool throws)
    {
        var test = new Scenario(withPreparation: true) { Prepared = true, Accept = false, ThrowOnDispatch = throws };
        test.Profile.ChocoboAutoAllocateSkills = true;
        test.Service.CheckAutomatic();
        test.Service.Tick();
        for (var i = 0; i < 50; i++) test.Service.CheckAutomatic();
        Assert.False(test.Service.IsActive);
        Assert.Single(test.Commands);
        Assert.Equal(1, test.Preparations);
        Assert.Equal(1, test.Releases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadinessExceptionCancelsPreparationOrPendingConfirmation(bool clicked)
    {
        var test = new Scenario(withPreparation: true, withConfirmation: true) { Prepared = true };
        test.Profile.ChocoboAutoAllocateSkills = true;
        test.Service.CheckAutomatic();
        if (clicked) test.Service.Tick();
        test.ThrowOnPrepared = true;
        test.Service.Tick();
        test.ThrowOnPrepared = false;
        test.State = test.State!.Value with { SkillPoints = 10 };
        for (var i = 0; i < 50; i++) test.Service.CheckAutomatic();
        Assert.False(test.Service.IsActive);
        Assert.Equal(clicked ? 1 : 0, test.Commands.Count);
        Assert.Equal(0, test.ConfirmationCalls);
        Assert.Equal(1, test.Preparations);
        Assert.Equal(1, test.Releases);
    }

    [Fact]
    public void AcceptedConfirmationExtendsAcknowledgementOnceWithoutRepeatingYes()
    {
        var test = new Scenario(withPreparation: true, withConfirmation: true) { Prepared = true };
        test.Service.Start();
        test.Service.Tick();
        test.Now = 2_900;
        test.Service.Tick();
        Assert.True(test.Service.IsActive);
        Assert.Equal(1, test.ConfirmationCalls);
        test.Confirmation = ChocoboSkillConfirmationResult.Accepted;
        test.Service.Tick();
        Assert.Equal(2, test.ConfirmationCalls);
        test.Now = 3_001;
        test.Service.Tick();
        test.Now = 5_899;
        test.Service.Tick();
        Assert.True(test.Service.IsActive);
        Assert.Equal(2, test.ConfirmationCalls);
        Assert.Equal(test.State, test.ConfirmationSnapshot);
        Assert.Equal(test.Commands[0], test.ConfirmationStep);
        test.Now = 5_900;
        test.Service.Tick();
        Assert.False(test.Service.IsActive);
        Assert.Single(test.Commands);
        Assert.Equal(1, test.Releases);
    }

    [Fact]
    public void WaitingConfirmationCannotExtendTheDeadlineOrRepeatTheClick()
    {
        var test = new Scenario(withPreparation: true, withConfirmation: true) { Prepared = true };
        test.Profile.ChocoboAutoAllocateSkills = true;
        test.Service.CheckAutomatic();
        test.Service.Tick();
        test.Now = 2_999;
        test.Service.Tick();
        test.Now = 3_000;
        test.Service.Tick();
        test.State = test.State!.Value with { SkillPoints = 10 };
        for (var i = 0; i < 50; i++) test.Service.CheckAutomatic();
        Assert.False(test.Service.IsActive);
        Assert.Single(test.Commands);
        Assert.Equal(1, test.ConfirmationCalls);
        Assert.Equal(1, test.Preparations);
        Assert.Equal(1, test.Releases);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("profile")]
    [InlineData("priority")]
    [InlineData("unsafe")]
    [InlineData("window")]
    [InlineData("partial")]
    [InlineData("exception")]
    [InlineData("rejected")]
    [InlineData("unknown")]
    [InlineData("stop")]
    public void ConfirmationCannotProceedAfterCancellationOrMismatch(string change)
    {
        var test = new Scenario(withPreparation: true, withConfirmation: true) { Prepared = true };
        test.Service.Start();
        test.Service.Tick();
        test.Confirmation = ChocoboSkillConfirmationResult.Accepted;
        switch (change)
        {
            case "owner": test.Owner = 2; break;
            case "profile": test.Profile = test.Profile.Clone(); break;
            case "priority": test.Profile.ChocoboSkillPriority = new() { 0 }; break;
            case "unsafe": test.Safe = false; break;
            case "window": test.Prepared = false; break;
            case "partial": test.State = test.State!.Value with { HealerLevel = 9 }; break;
            case "exception": test.ThrowOnConfirm = true; break;
            case "rejected": test.Confirmation = ChocoboSkillConfirmationResult.Rejected; break;
            case "unknown": test.Confirmation = (ChocoboSkillConfirmationResult)99; break;
            case "stop": test.Service.Stop(); break;
        }
        test.Service.Tick();
        test.Service.Tick();
        Assert.False(test.Service.IsActive);
        Assert.Single(test.Commands);
        Assert.Equal(change is "exception" or "rejected" or "unknown" ? 1 : 0, test.ConfirmationCalls);
        Assert.Equal(1, test.Releases);
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("observed learning:"));
    }

    [Theory]
    [InlineData("exception")]
    [InlineData("rejected")]
    [InlineData("unknown")]
    [InlineData("partial")]
    public void ConfirmationFailureCannotAutomaticallyReplayAfterStateChanges(string failure)
    {
        var test = new Scenario(withPreparation: true, withConfirmation: true) { Prepared = true };
        test.Profile.ChocoboAutoAllocateSkills = true;
        test.Service.CheckAutomatic();
        test.Service.Tick();
        test.ThrowOnConfirm = failure == "exception";
        test.Confirmation = failure == "unknown"
            ? (ChocoboSkillConfirmationResult)99 : ChocoboSkillConfirmationResult.Rejected;
        if (failure == "partial") test.State = test.State!.Value with { HealerLevel = 9 };
        test.Service.Tick();
        test.ThrowOnConfirm = false;
        test.State = test.State!.Value with { SkillPoints = 10 };
        for (var i = 0; i < 50; i++) test.Service.CheckAutomatic();
        Assert.False(test.Service.IsActive);
        Assert.Single(test.Commands);
        Assert.Equal(1, test.Preparations);
        Assert.Equal(1, test.Releases);
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("observed learning:"));
    }

    [Fact]
    public void EachAcknowledgedStepGetsItsOwnSingleConfirmation()
    {
        var test = new Scenario(withPreparation: true, withConfirmation: true)
        {
            Prepared = true,
            Confirmation = ChocoboSkillConfirmationResult.Accepted,
        };
        test.State = test.State!.Value with { HealerLevel = 0, SkillPoints = 3 };
        test.Service.Start();
        test.Service.Tick();
        test.Service.Tick();
        test.Service.Tick();
        Assert.Equal(1, test.ConfirmationCalls);
        test.State = test.State!.Value with { HealerLevel = 1, SkillPoints = 2 };
        test.Service.Tick();
        test.Service.Tick();
        test.Service.Tick();
        Assert.Equal(2, test.ConfirmationCalls);
        test.State = test.State!.Value with { HealerLevel = 2, SkillPoints = 0 };
        test.Service.Tick();
        Assert.False(test.Service.IsActive);
        Assert.Equal(2, test.Commands.Count);
        Assert.Equal(1, test.Preparations);
        Assert.Equal(1, test.Releases);
    }

    [Fact]
    public void ExactReadbackCompletesWithoutTouchingAnotherConfirmation()
    {
        var test = new Scenario(withPreparation: true, withConfirmation: true) { Prepared = true };
        test.Service.Start();
        test.Service.Tick();
        test.State = test.State!.Value with { HealerLevel = 9, SkillPoints = 0 };
        test.Service.Tick();
        Assert.False(test.Service.IsActive);
        Assert.Equal(0, test.ConfirmationCalls);
        Assert.Equal(1, test.Releases);
        Assert.Single(test.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanupRunsAfterDeactivationAndCannotRepeat(bool throws)
    {
        var test = new Scenario(withPreparation: true) { ThrowOnRelease = throws };
        test.Service.Start();
        test.OnRelease = () => test.Service.Stop();
        test.Service.Stop();
        test.Service.Stop();
        Assert.False(test.Service.IsActive);
        Assert.False(test.ReleasedWhileActive);
        Assert.Equal(1, test.Releases);
        Assert.Empty(test.Commands);
    }

    private sealed class Scenario
    {
        internal CharacterConfig Profile = new() { Enabled = true };
        internal CompanionSnapshot? State = new CompanionSnapshot(9, 0, 0, 9, 0, 0, 8);
        internal ulong Owner = 1;
        internal bool Safe = true;
        internal bool Accept = true;
        internal bool ThrowOnDispatch;
        internal bool ThrowOnProfileRead;
        internal bool PrepareAccept = true;
        internal bool Prepared;
        internal bool ThrowOnPrepare;
        internal bool ThrowOnPrepared;
        internal bool ThrowOnRelease;
        internal bool ThrowOnConfirm;
        internal bool ReleasedWhileActive;
        internal int Preparations;
        internal int Releases;
        internal int ConfirmationCalls;
        internal ChocoboSkillConfirmationResult Confirmation = ChocoboSkillConfirmationResult.Waiting;
        internal CompanionSnapshot? ConfirmationSnapshot;
        internal ChocoboSkillStep? ConfirmationStep;
        internal Action? OnPrepared;
        internal Action? OnRelease;
        internal long Now;
        internal readonly List<ChocoboSkillStep> Commands = new();
        internal readonly List<string> Logs = new();
        internal readonly ChocoboSkillService Service;

        internal Scenario(bool withPreparation = false, bool withConfirmation = false)
        {
            Service = new(() => Safe, () => Owner,
                () => ThrowOnProfileRead ? throw new InvalidOperationException() : Profile, () => State,
                (_, step) => { Commands.Add(step); if (ThrowOnDispatch) throw new InvalidOperationException(); return Accept; },
                Logs.Add, () => Now,
                prepare: withPreparation ? () =>
                {
                    Preparations++;
                    if (ThrowOnPrepare) throw new InvalidOperationException();
                    return PrepareAccept;
                } : null,
                prepared: withPreparation ? () =>
                {
                    if (ThrowOnPrepared) throw new InvalidOperationException();
                    OnPrepared?.Invoke();
                    return Prepared;
                } : null,
                release: withPreparation ? () =>
                {
                    Releases++;
                    ReleasedWhileActive |= Service!.IsActive;
                    OnRelease?.Invoke();
                    if (ThrowOnRelease) throw new InvalidOperationException();
                } : null,
                confirm: withConfirmation ? (snapshot, step) =>
                {
                    ConfirmationCalls++;
                    ConfirmationSnapshot = snapshot;
                    ConfirmationStep = step;
                    if (ThrowOnConfirm) throw new InvalidOperationException();
                    return Confirmation;
                } : null);
        }
    }
}
