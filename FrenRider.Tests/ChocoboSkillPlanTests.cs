using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class ChocoboSkillPlanTests
{
    [Theory]
    [InlineData(0, 0, 1, 0)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(2, 0, 1, 2)]
    [InlineData(0, 4, 5, 12)]
    [InlineData(1, 4, 5, 13)]
    [InlineData(2, 4, 5, 14)]
    [InlineData(0, 8, 9, 24)]
    [InlineData(1, 8, 9, 25)]
    [InlineData(2, 8, 9, 26)]
    [InlineData(0, 9, 10, 27)]
    [InlineData(1, 9, 10, 28)]
    [InlineData(2, 9, 10, 29)]
    public void NextSkillPreservesLearnedPrefixAndUsesIndividualCostAndNativePayload(
        int treeIndex, int currentLevel, int expectedLevel, int expectedPayload)
    {
        var tree = (ChocoboSkillTree)treeIndex;
        var snapshot = Snapshot(points: (byte)expectedLevel) with
        {
            DefenderLevel = tree == ChocoboSkillTree.Defender ? (byte)currentLevel : (byte)0,
            AttackerLevel = tree == ChocoboSkillTree.Attacker ? (byte)currentLevel : (byte)0,
            HealerLevel = tree == ChocoboSkillTree.Healer ? (byte)currentLevel : (byte)0,
        };

        Assert.True(ChocoboSkillPlan.TryGetNext(snapshot, new[] { tree }, out var step));
        Assert.Equal(new ChocoboSkillStep(tree, expectedLevel, expectedLevel, expectedPayload), step);
    }

    [Fact]
    public void FirstUnfinishedConfiguredTreeTakesPriorityOverCheaperTrees()
    {
        var snapshot = Snapshot(points: 9, defender: 0, attacker: 0, healer: 8);
        var priority = new[] { ChocoboSkillTree.Healer, ChocoboSkillTree.Defender, ChocoboSkillTree.Attacker };

        Assert.True(ChocoboSkillPlan.TryGetNext(snapshot, priority, out var step));
        Assert.Equal(new ChocoboSkillStep(ChocoboSkillTree.Healer, 9, 9, 26), step);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void UnaffordableFirstTreeDoesNotSpillPointsIntoCheaperLowerPriorities(byte points)
    {
        var snapshot = Snapshot(points, defender: 0, attacker: 0, healer: 8);
        var priority = new[] { ChocoboSkillTree.Healer, ChocoboSkillTree.Defender, ChocoboSkillTree.Attacker };

        Assert.False(ChocoboSkillPlan.TryGetNext(snapshot, priority, out var step));
        Assert.Equal(default, step);
    }

    [Fact]
    public void CompletedTreeAllowsTheNextExplicitlyConfiguredPriority()
    {
        var snapshot = Snapshot(points: 5, defender: 4, attacker: 0, healer: 10);
        var priority = new[] { ChocoboSkillTree.Healer, ChocoboSkillTree.Defender, ChocoboSkillTree.Attacker };

        Assert.True(ChocoboSkillPlan.TryGetNext(snapshot, priority, out var step));
        Assert.Equal(new ChocoboSkillStep(ChocoboSkillTree.Defender, 5, 5, 12), step);
    }

    [Fact]
    public void CompletedHealerOnlyPriorityDoesNotAddOtherTrees()
    {
        Assert.False(ChocoboSkillPlan.TryGetNext(Snapshot(points: 10, healer: 10),
            new[] { ChocoboSkillTree.Healer }, out var step));
        Assert.Equal(default, step);
    }

    [Fact]
    public void TwoTreePriorityPreservesItsOrder()
    {
        var priority = new[] { ChocoboSkillTree.Attacker, ChocoboSkillTree.Healer };

        Assert.True(ChocoboSkillPlan.TryGetNext(Snapshot(points: 3, attacker: 2, healer: 0), priority,
            out var step));
        Assert.Equal(new ChocoboSkillStep(ChocoboSkillTree.Attacker, 3, 3, 7), step);
    }

    [Fact]
    public void NoPointsOrAllConfiguredTreesCompletedProducesNoStep()
    {
        var priority = new[] { ChocoboSkillTree.Healer, ChocoboSkillTree.Defender, ChocoboSkillTree.Attacker };

        Assert.False(ChocoboSkillPlan.TryGetNext(Snapshot(points: 0), priority, out var noPoints));
        Assert.Equal(default, noPoints);
        Assert.False(ChocoboSkillPlan.TryGetNext(Snapshot(points: 1, defender: 10, attacker: 10, healer: 10),
            priority, out var completed));
        Assert.Equal(default, completed);
    }

    [Fact]
    public void InvalidPrioritiesBlockBeforeAnOtherwiseAffordableFirstEntry()
    {
        IReadOnlyList<ChocoboSkillTree>?[] invalid =
        {
            null,
            Array.Empty<ChocoboSkillTree>(),
            new[] { (ChocoboSkillTree)(-1) },
            new[] { (ChocoboSkillTree)3 },
            new[] { ChocoboSkillTree.Healer, (ChocoboSkillTree)3 },
            new[] { ChocoboSkillTree.Healer, ChocoboSkillTree.Healer },
            new[] { ChocoboSkillTree.Healer, ChocoboSkillTree.Defender, ChocoboSkillTree.Attacker, ChocoboSkillTree.Healer },
        };

        foreach (var priority in invalid)
        {
            Assert.False(ChocoboSkillPlan.TryGetNext(Snapshot(points: 1), priority!, out var step));
            Assert.Equal(default, step);
        }
    }

    [Theory]
    [InlineData(11, 0, 0)]
    [InlineData(0, 11, 0)]
    [InlineData(0, 0, 11)]
    [InlineData(255, 0, 0)]
    public void InvalidLearnedLevelBlocksEvenWhenOutsideConfiguredPriority(byte defender, byte attacker, byte healer)
    {
        Assert.False(ChocoboSkillPlan.TryGetNext(Snapshot(points: 1, defender, attacker, healer),
            new[] { ChocoboSkillTree.Healer }, out var step));
        Assert.Equal(default, step);
    }

    [Theory]
    [InlineData(166)]
    [InlineData(255)]
    public void OutOfRangeAvailablePointsBlockPlanning(byte points)
    {
        Assert.False(ChocoboSkillPlan.TryGetNext(Snapshot(points), new[] { ChocoboSkillTree.Healer },
            out var step));
        Assert.Equal(default, step);
    }

    [Fact]
    public void PlanningDoesNotInferRankCapOrTotalSpentFromSnapshotFields()
    {
        var snapshot = Snapshot(points: 165, defender: 10, attacker: 10, healer: 9) with
        {
            Rank = 0,
            Stars = byte.MaxValue,
            CurrentXp = uint.MaxValue,
        };

        Assert.True(ChocoboSkillPlan.TryGetNext(snapshot, new[] { ChocoboSkillTree.Healer }, out var step));
        Assert.Equal(new ChocoboSkillStep(ChocoboSkillTree.Healer, 10, 10, 29), step);
    }

    private static CompanionSnapshot Snapshot(byte points, byte defender = 0, byte attacker = 0, byte healer = 0)
        => new(9, 0, 0, points, defender, attacker, healer);
}
