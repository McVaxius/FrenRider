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

    private sealed class Scenario
    {
        internal CharacterConfig Profile = new() { Enabled = true };
        internal CompanionSnapshot? State = new CompanionSnapshot(9, 0, 0, 9, 0, 0, 8);
        internal ulong Owner = 1;
        internal bool Safe = true;
        internal bool Accept = true;
        internal bool ThrowOnDispatch;
        internal bool ThrowOnProfileRead;
        internal long Now;
        internal readonly List<ChocoboSkillStep> Commands = new();
        internal readonly List<string> Logs = new();
        internal readonly ChocoboSkillService Service;

        internal Scenario()
        {
            Service = new(() => Safe, () => Owner,
                () => ThrowOnProfileRead ? throw new InvalidOperationException() : Profile, () => State,
                (_, step) => { Commands.Add(step); if (ThrowOnDispatch) throw new InvalidOperationException(); return Accept; },
                Logs.Add, () => Now);
        }
    }
}
