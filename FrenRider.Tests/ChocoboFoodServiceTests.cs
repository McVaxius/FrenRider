using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class ChocoboFoodServiceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void RequiresExactOneItemDebitAndAnOwnedEffect(int holder)
    {
        var test = new Scenario();
        Assert.True(test.Service.Start());
        Assert.False(test.Service.Start());
        test.State = test.State!.Value with { Stock = 2 };
        test.Service.Tick();
        Assert.True(test.Service.IsActive);
        test.State = test.State!.Value with { EffectHolders = (byte)holder };
        test.Service.Tick();
        Assert.False(test.Service.IsActive);
        Assert.Single(test.Commands);
        Assert.Contains(test.Logs, line => line.StartsWith("observed feeding:"));
    }

    [Theory]
    [InlineData(7894)]
    [InlineData(7895)]
    [InlineData(7897)]
    [InlineData(7898)]
    [InlineData(7900)]
    public void UsesOnlyTheConfiguredSupportedFood(int food)
    {
        var test = new Scenario();
        test.Profile.ChocoboFoodItemId = food;
        Assert.True(test.Service.Start());
        Assert.Equal((uint)food, Assert.Single(test.Commands));
        Assert.Equal((uint)food, Assert.Single(test.Reads));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4868)]
    [InlineData(8166)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void UnsetOrUnsupportedChoiceCannotConsumeAnItem(int food)
    {
        var test = new Scenario();
        test.Profile.ChocoboFoodItemId = food;
        Assert.False(test.Service.Start());
        Assert.Empty(test.Reads);
        Assert.Empty(test.Commands);
    }

    [Fact]
    public void UnsummonedCompanionCannotConsumeFood()
    {
        var test = new Scenario();
        test.State = test.State!.Value with { BuddyEntityId = 0 };
        Assert.False(test.Service.Start());
        Assert.Empty(test.Commands);
        Assert.Equal("Summon the companion before feeding.", test.Service.Status);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 4)]
    public void MissingFoodOrExistingEffectDoesNotDispatch(int stock, int holders)
    {
        var test = new Scenario();
        test.State = test.State!.Value with { Stock = stock, EffectHolders = (byte)holders };
        Assert.False(test.Service.Start());
        Assert.Empty(test.Commands);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    public void PartialOrMissingAcknowledgementStopsWithoutAutomaticReplay(int stock, int holders)
    {
        var test = new Scenario();
        test.Profile.ChocoboAutoFeed = true;
        test.Service.CheckAutomatic();
        test.State = test.State!.Value with { Stock = stock, EffectHolders = (byte)holders };
        test.Now = 5_000;
        test.Service.Tick();
        Assert.False(test.Service.IsActive);
        test.State = test.State!.Value with { Stock = 3, EffectHolders = 0 };
        for (var i = 0; i < 30; i++) test.Service.CheckAutomatic();
        Assert.Single(test.Commands);
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("observed feeding:"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void UnexpectedInventoryChangeCannotAcknowledgeFeeding(int stock)
    {
        var test = new Scenario();
        test.Service.Start();
        test.State = test.State!.Value with { Stock = stock, EffectHolders = 1 };
        test.Service.Tick();
        Assert.False(test.Service.IsActive);
        Assert.Contains("unexpected inventory", test.Service.Status);
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("observed feeding:"));
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("profile")]
    [InlineData("selection")]
    [InlineData("context")]
    [InlineData("companion")]
    [InlineData("unavailable")]
    public void ContextChangesCancelWithoutAnotherDispatch(string change)
    {
        var test = new Scenario();
        test.Service.Start();
        switch (change)
        {
            case "identity": test.Owner++; break;
            case "profile": test.Profile = test.Profile.Clone(); break;
            case "selection": test.Profile.ChocoboFoodItemId = 7895; break;
            case "context": test.Safe = false; break;
            case "companion": test.State = test.State!.Value with { BuddyEntityId = 456 }; break;
            default: test.State = null; break;
        }
        test.Service.Tick();
        Assert.False(test.Service.IsActive);
        Assert.Single(test.Commands);
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("observed feeding:"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AutomaticFeedingRequiresBothProfileAndFoodOptIn(bool enabled, bool feed)
    {
        var test = new Scenario();
        test.Profile.Enabled = enabled;
        test.Profile.ChocoboAutoFeed = feed;
        test.Service.CheckAutomatic();
        Assert.Empty(test.Commands);
    }

    [Fact]
    public void ManualFeedingRemainsAvailableWithFrenRiderDisabled()
    {
        var test = new Scenario();
        test.Profile.Enabled = false;
        Assert.True(test.Service.Start());
        Assert.Single(test.Commands);
    }

    [Fact]
    public void AutomaticOffCancelsItsPendingAttempt()
    {
        var test = new Scenario();
        test.Profile.ChocoboAutoFeed = true;
        test.Service.CheckAutomatic();
        test.Profile.ChocoboAutoFeed = false;
        test.Service.Tick();
        Assert.False(test.Service.IsActive);
        Assert.Single(test.Commands);
    }

    [Fact]
    public void SuccessfulEffectPreventsAnotherFeedUntilItIsAbsent()
    {
        var test = new Scenario();
        test.Profile.ChocoboAutoFeed = true;
        test.Service.CheckAutomatic();
        test.State = test.State!.Value with { Stock = 2, EffectHolders = 4 };
        test.Service.Tick();
        test.Service.CheckAutomatic();
        Assert.Single(test.Commands);
        test.State = test.State!.Value with { EffectHolders = 0 };
        test.Service.CheckAutomatic();
        Assert.Equal(2, test.Commands.Count);
    }

    [Fact]
    public void RejectedDispatchCannotAutomaticallyRepeatButExplicitManualAttemptCan()
    {
        var test = new Scenario();
        test.Profile.ChocoboAutoFeed = true;
        test.Accept = false;
        test.Service.CheckAutomatic();
        test.Service.CheckAutomatic();
        Assert.Single(test.Commands);
        test.Accept = true;
        Assert.True(test.Service.Start());
        Assert.Equal(2, test.Commands.Count);
    }

    [Fact]
    public void DispatchExceptionStopsAndCannotAutomaticallyRepeat()
    {
        var test = new Scenario();
        test.Profile.ChocoboAutoFeed = true;
        test.ThrowDispatch = true;
        test.Service.CheckAutomatic();
        test.Service.CheckAutomatic();
        Assert.False(test.Service.IsActive);
        Assert.Single(test.Commands);
    }

    [Fact]
    public void StopInsideDispatchRemainsConsumed()
    {
        var test = new Scenario();
        test.Profile.ChocoboAutoFeed = true;
        test.StopInsideDispatch = true;
        test.Service.CheckAutomatic();
        test.Service.CheckAutomatic();
        Assert.False(test.Service.IsActive);
        Assert.Single(test.Commands);
    }

    [Fact]
    public void ExplicitOffAndOnAllowsANewAutomaticAttempt()
    {
        var test = new Scenario();
        test.Profile.ChocoboAutoFeed = true;
        test.Accept = false;
        test.Service.CheckAutomatic();
        test.Profile.ChocoboAutoFeed = false;
        test.Service.Tick();
        test.Profile.ChocoboAutoFeed = true;
        test.Accept = true;
        test.Service.CheckAutomatic();
        Assert.Equal(2, test.Commands.Count);
    }

    private sealed class Scenario
    {
        internal bool Safe = true, Accept = true, ThrowDispatch, StopInsideDispatch;
        internal ulong Owner = 42;
        internal long Now;
        internal CharacterConfig Profile = new() { Enabled = true, ChocoboFoodItemId = 7894 };
        internal CompanionFoodSnapshot? State = new(123, 3, 0);
        internal readonly List<uint> Commands = [];
        internal readonly List<uint> Reads = [];
        internal readonly List<string> Logs = [];
        internal readonly ChocoboFoodService Service;

        internal Scenario()
        {
            Service = new(() => Safe, () => Owner, () => Profile,
                item => { Reads.Add(item); return State; },
                (item, before) =>
                {
                    Commands.Add(item);
                    if (ThrowDispatch) throw new InvalidOperationException();
                    if (StopInsideDispatch) Service!.Stop();
                    return Accept;
                }, Logs.Add, () => Now);
        }
    }
}
