using System.Text.Json;
using System.Reflection;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FrenRider.IPC;
using FrenRider.Models;
using FrenRider.Services;
using Lumina;
using Lumina.Data;
using Lumina.Excel;

namespace FrenRider.Tests;

public sealed class BeastCaptureServiceTests
{
    private static readonly BeastCaptureTarget EligibleTarget = new(100, true, true, 400, 1000, 84, new uint[] { 1 });

    [Theory]
    [InlineData(84, 900, true)]
    [InlineData(84, 901, false)]
    [InlineData(85, 400, true)]
    [InlineData(85, 401, false)]
    [InlineData(86, 400, true)]
    [InlineData(90, 400, true)]
    [InlineData(90, 401, false)]
    [InlineData(91, 1, false)]
    public void DefaultThresholdsUseEffectiveLevelAndIncludeHpEquality(int level, uint hp, bool expected)
        => Assert.Equal(expected, BeastCaptureService.IsEligible(
            EligibleTarget with { Level = level, CurrentHp = hp }, 90, new CharacterConfig()));

    [Theory]
    [InlineData(84, 170, true)]
    [InlineData(84, 171, false)]
    [InlineData(85, 830, true)]
    [InlineData(85, 831, false)]
    public void CustomThresholdsRemainIndependent(int level, uint hp, bool expected)
        => Assert.Equal(expected, BeastCaptureService.IsEligible(
            EligibleTarget with { Level = level, CurrentHp = hp }, 90,
            new CharacterConfig { CaptureHpFarBelow = 17, CaptureHpNearOrEqual = 83 }));

    [Theory]
    [InlineData(84, 17, 83, 170, true)]
    [InlineData(84, 17, 83, 171, false)]
    [InlineData(85, 17, 83, 830, true)]
    [InlineData(85, 17, 83, 831, false)]
    [InlineData(90, 17, 83, 830, true)]
    [InlineData(90, 17, 83, 831, false)]
    [InlineData(84, 100, 40, 1000, true)]
    [InlineData(85, 100, 40, 1000, false)]
    [InlineData(84, 17, 100, 1000, false)]
    [InlineData(85, 17, 100, 1000, true)]
    [InlineData(90, 17, 100, 1000, true)]
    [InlineData(84, 99, 99, 1000, false)]
    [InlineData(85, 99, 99, 1000, false)]
    public void OutOfCombatCaptureUsesConfiguredThresholdsIncludingFullHealth(
        int level, int farThreshold, int nearThreshold, uint hp, bool expected)
    {
        var runtime = new FakeRuntime
        {
            Target = EligibleTarget with { Level = level, CurrentHp = hp },
        };
        runtime.Frame = runtime.Frame with { InCombat = false };
        runtime.Frame.Config.CaptureHpFarBelow = farThreshold;
        runtime.Frame.Config.CaptureHpNearOrEqual = nearThreshold;

        new BeastCaptureService(runtime).Update(1000);

        Assert.Equal(expected ? new ulong[] { 100 } : Array.Empty<ulong>(), runtime.CapturedTargets);
        Assert.Equal(expected ? 1 : 0, runtime.ActionReads);
        Assert.Empty(runtime.Account.UnlockedBeasts["First"]);
    }

    [Fact]
    public void PassiveCombatantCanOpenWithCaptureAtFullHealth()
    {
        var target = DispatchProxy.Create<IBattleNpc, QuestionableTestProxy>();
        ((QuestionableTestProxy)(object)target).Handler = (method, _) => method.Name switch
        {
            "get_GameObjectId" => 100UL,
            "get_CurrentDistance" => (byte)8,
            "get_BaseId" => 37u,
            "get_BattleNpcKind" => BattleNpcSubKind.Combatant,
            "get_StatusFlags" => StatusFlags.None,
            "get_IsTargetable" => true,
            "get_CurrentHp" => 51u,
            "get_MaxHp" => 51u,
            "get_Level" => (byte)2,
            _ => throw new InvalidOperationException(method.Name),
        };
        var targets = DispatchProxy.Create<ITargetManager, QuestionableTestProxy>();
        ((QuestionableTestProxy)(object)targets).Handler = (method, _) =>
            method.Name == "get_Target" ? target : throw new InvalidOperationException(method.Name);
        var properties = new[] { "TargetManager", "DataManager" }.ToDictionary(name => name,
            name => typeof(Plugin).GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic)!);
        var previous = properties.ToDictionary(pair => pair.Key, pair => pair.Value.GetValue(null));
        try
        {
            properties["TargetManager"].SetValue(null, targets);
            properties["DataManager"].SetValue(null, new EmptyDataManager());
            Assert.Equal(StatusFlags.None, target.StatusFlags);
            var classified = Assert.IsType<BeastCaptureTarget>(new NativeBeastCaptureRuntime(null!).ReadTarget());
            Assert.Equal(new BeastCaptureTarget(100, true, true, 51, 51, 2, Array.Empty<uint>()), classified);

            // Resolve the beast through the existing roster seam without game data.
            var runtime = new FakeRuntime();
            var beastIds = BeastCaptureService.ResolveBeasts(5, 10,
                BeastCaptureService.BuildModelLookup(runtime.Roster));
            Assert.Equal(new uint[] { 1 }, beastIds);
            runtime.Target = classified with { CandidateBeastIds = beastIds };
            runtime.Frame = runtime.Frame with { EffectiveLevel = 11, InCombat = false };
            runtime.Frame.Config.CaptureHpFarBelow = 100;
            runtime.Frame.Config.CaptureHpNearOrEqual = 40;
            var service = new BeastCaptureService(runtime);

            service.Update(1000);
            service.Update(1001);

            Assert.Equal(new ulong[] { 100 }, runtime.CapturedTargets);
            Assert.Equal(1, runtime.ActionReads);
            Assert.Empty(runtime.Account.UnlockedBeasts["First"]);
        }
        finally
        {
            foreach (var pair in properties)
                pair.Value.SetValue(null, previous[pair.Key]);
        }
    }

    [Fact]
    public void UntargetedNearbyPassiveSprigganCanBeCapturedAtFullHealthBeforeCombat()
    {
        var spriggan = DispatchProxy.Create<IBattleNpc, QuestionableTestProxy>();
        ((QuestionableTestProxy)(object)spriggan).Handler = (method, _) => method.Name switch
        {
            "get_GameObjectId" => 100UL,
            "get_CurrentDistance" => (byte)8,
            "get_BaseId" => 37u,
            "get_BattleNpcKind" => BattleNpcSubKind.Combatant,
            "get_StatusFlags" => StatusFlags.None,
            "get_IsTargetable" => true,
            "get_CurrentHp" => 126u,
            "get_MaxHp" => 126u,
            "get_Level" => (byte)7,
            _ => throw new InvalidOperationException(method.Name),
        };
        IGameObject? selected = null;
        var targetChanges = new List<ulong>();
        var targets = DispatchProxy.Create<ITargetManager, QuestionableTestProxy>();
        ((QuestionableTestProxy)(object)targets).Handler = (method, args) =>
        {
            if (method.Name == "get_Target") return selected;
            if (method.Name == "set_Target")
            {
                selected = (IGameObject)args![0]!;
                targetChanges.Add(selected.GameObjectId);
                return null;
            }
            throw new InvalidOperationException(method.Name);
        };
        var table = DispatchProxy.Create<IObjectTable, QuestionableTestProxy>();
        ((QuestionableTestProxy)(object)table).Handler = (method, args) => method.Name switch
        {
            "GetEnumerator" => ((IEnumerable<IGameObject>)new[] { spriggan }).GetEnumerator(),
            "SearchById" => (ulong)args![0]! == spriggan.GameObjectId ? spriggan : null,
            _ => throw new InvalidOperationException(method.Name),
        };
        var properties = new[] { "TargetManager", "ObjectTable", "DataManager" }.ToDictionary(name => name,
            name => typeof(Plugin).GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic)!);
        var previous = properties.ToDictionary(pair => pair.Key, pair => pair.Value.GetValue(null));
        try
        {
            properties["TargetManager"].SetValue(null, targets);
            properties["ObjectTable"].SetValue(null, table);
            properties["DataManager"].SetValue(null, new EmptyDataManager());
            var native = new NativeBeastCaptureRuntime(null!);
            Assert.Null(native.ReadTarget());
            Assert.Equal(StatusFlags.None, spriggan.StatusFlags);
            var nearby = Assert.Single(native.ReadNearbyTargets());
            Assert.Empty(nearby.CandidateBeastIds);

            var runtime = new FakeRuntime
            {
                Target = null,
                TargetSelector = native.TargetBeast,
                Roster = new[] { new BeastRosterEntry(29, "Spriggan", 28) },
            };
            // Supply the model at the existing offline boundary without loading game data.
            var beastIds = BeastCaptureService.ResolveBeasts(5, 28,
                BeastCaptureService.BuildModelLookup(runtime.Roster));
            Assert.Equal(new uint[] { 29 }, beastIds);
            runtime.NearbyTargets.Add(nearby with { CandidateBeastIds = beastIds });
            runtime.Frame = runtime.Frame with { EffectiveLevel = 15, InCombat = false };
            runtime.Frame.Config.CaptureHpFarBelow = 100;
            runtime.Frame.Config.CaptureHpNearOrEqual = 90;
            var service = new BeastCaptureService(runtime);

            service.Update(1000);
            service.Update(1001);

            Assert.Equal(new ulong[] { 100 }, targetChanges);
            Assert.Equal(new ulong[] { 100 }, runtime.SelectedTargets);
            Assert.Equal(new ulong[] { 100 }, runtime.CapturedTargets);
            Assert.Equal(1, runtime.ActionReads);
            Assert.Equal(100UL, selected!.GameObjectId);
            Assert.False(runtime.Frame.InCombat);
            Assert.Empty(runtime.Account.UnlockedBeasts["First"]);
        }
        finally
        {
            foreach (var pair in properties)
                pair.Value.SetValue(null, previous[pair.Key]);
        }
    }

    [Fact]
    public void NearbyCaptureSelectsNearestEligibleBeastOnlyWhenActionIsReady()
    {
        IBattleNpc Npc(ulong id, byte distance, byte level = 2)
        {
            var npc = DispatchProxy.Create<IBattleNpc, QuestionableTestProxy>();
            ((QuestionableTestProxy)(object)npc).Handler = (method, _) => method.Name switch
            {
                "get_GameObjectId" => id,
                "get_CurrentDistance" => distance,
                "get_BaseId" => distance < 10 ? 37u : throw new InvalidOperationException("Out-of-range lookup"),
                "get_BattleNpcKind" => BattleNpcSubKind.Combatant,
                "get_StatusFlags" => StatusFlags.None,
                "get_IsTargetable" => true,
                "get_CurrentHp" => 51u,
                "get_MaxHp" => 51u,
                "get_Level" => level,
                _ => throw new InvalidOperationException(method.Name),
            };
            return npc;
        }
        var objects = new IGameObject[]
        {
            Npc(800, 9), Npc(1000, 10), Npc(100, 8), Npc(700, 6),
            Npc(500, 4), Npc(200, 1, 12), Npc(600, 5, 8), Npc(400, 3), Npc(300, 2),
        };
        IGameObject? selected = null;
        var targetChanges = new List<ulong>();
        var targets = DispatchProxy.Create<ITargetManager, QuestionableTestProxy>();
        ((QuestionableTestProxy)(object)targets).Handler = (method, args) =>
        {
            if (method.Name == "get_Target") return selected;
            if (method.Name == "set_Target")
            {
                selected = (IGameObject)args![0]!;
                targetChanges.Add(selected.GameObjectId);
                return null;
            }
            throw new InvalidOperationException(method.Name);
        };
        var table = DispatchProxy.Create<IObjectTable, QuestionableTestProxy>();
        ((QuestionableTestProxy)(object)table).Handler = (method, args) => method.Name switch
        {
            "GetEnumerator" => ((IEnumerable<IGameObject>)objects).GetEnumerator(),
            "SearchById" => objects.SingleOrDefault(obj => obj.GameObjectId == (ulong)args![0]!),
            _ => throw new InvalidOperationException(method.Name),
        };
        var properties = new[] { "TargetManager", "ObjectTable", "DataManager" }.ToDictionary(name => name,
            name => typeof(Plugin).GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic)!);
        var previous = properties.ToDictionary(pair => pair.Key, pair => pair.Value.GetValue(null));
        try
        {
            properties["TargetManager"].SetValue(null, targets);
            properties["ObjectTable"].SetValue(null, table);
            properties["DataManager"].SetValue(null, new EmptyDataManager());
            var native = new NativeBeastCaptureRuntime(null!);
            Assert.Null(native.ReadTarget());
            var nearby = native.ReadNearbyTargets().ToArray();
            Assert.Equal(new ulong[] { 200, 300, 400, 500, 600, 700, 100, 800 }, nearby.Select(beast => beast.Id));
            Assert.Empty(targetChanges);

            var runtime = new FakeRuntime { Target = null, TargetSelector = native.TargetBeast };
            // Supply resolved beast IDs at the existing offline boundary; 300 remains unresolved.
            runtime.NearbyTargets.AddRange(nearby.Select(beast => beast with
            {
                CandidateBeastIds = beast.Id == 300 ? Array.Empty<uint>() : new[] { (uint)beast.Id },
            }));
            runtime.BeastOwnership[400] = true;
            runtime.BeastOwnership[500] = null;
            runtime.TargetActions[700] = new(false, false, 7.5f);
            runtime.Frame = runtime.Frame with { EffectiveLevel = 11, InCombat = false, CanAct = false };
            runtime.Frame.Config.CaptureHpFarBelow = 100;
            runtime.Frame.Config.CaptureHpNearOrEqual = 40;
            var service = new BeastCaptureService(runtime);
            service.Update(1000);
            Assert.Equal(0, runtime.ActionReads);

            runtime.Frame = runtime.Frame with { CanAct = true };
            runtime.Action = runtime.Action with { RecastActive = true };
            service.Update(1001);
            Assert.Empty(targetChanges);
            Assert.Empty(runtime.CapturedTargets);

            runtime.Action = runtime.Action with { RecastActive = false };
            service.Update(1002);
            service.Update(1003);

            Assert.Equal(new ulong[] { 100 }, targetChanges);
            Assert.Equal(100UL, selected!.GameObjectId);
            Assert.Equal(new ulong[] { 100 }, runtime.CapturedTargets);
            Assert.Empty(runtime.Account.UnlockedBeasts["First"]);
        }
        finally
        {
            foreach (var pair in properties)
                pair.Value.SetValue(null, previous[pair.Key]);
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(255)]
    public void DistantTargetsReturnBeforeBeastLookup(byte distance)
    {
        var target = DispatchProxy.Create<IBattleNpc, QuestionableTestProxy>();
        ((QuestionableTestProxy)(object)target).Handler = (method, _) =>
            method.Name == "get_CurrentDistance" ? distance : throw new InvalidOperationException(method.Name);
        var targets = DispatchProxy.Create<ITargetManager, QuestionableTestProxy>();
        ((QuestionableTestProxy)(object)targets).Handler = (method, _) =>
            method.Name == "get_Target" ? target : throw new InvalidOperationException(method.Name);
        var property = typeof(Plugin).GetProperty("TargetManager", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = property.GetValue(null);
        try
        {
            property.SetValue(null, targets);
            Assert.Null(new NativeBeastCaptureRuntime(null!).ReadTarget());
        }
        finally
        {
            property.SetValue(null, previous);
        }
    }

    [Fact]
    public void InvalidTargetsAndUnknownEffectiveLevelsAreExcluded()
    {
        var invalid = new[]
        {
            EligibleTarget with { Id = 0 },
            EligibleTarget with { Id = 0xE0000000 },
            EligibleTarget with { IsEnemy = false },
            EligibleTarget with { IsTargetable = false },
            EligibleTarget with { CurrentHp = 0 },
            EligibleTarget with { MaxHp = 0 },
            EligibleTarget with { CurrentHp = 1001 },
            EligibleTarget with { Level = 0 },
            EligibleTarget with { CandidateBeastIds = Array.Empty<uint>() },
        };
        Assert.All(invalid, target => Assert.False(BeastCaptureService.IsEligible(target, 90, new())));
        Assert.False(BeastCaptureService.IsEligible(EligibleTarget, 0, new()));
        Assert.False(BeastCaptureService.IsEligible(EligibleTarget with { Level = 86 }, 85, new()));
    }

    [Fact]
    public void ModelOnlyResolutionReturnsDistinctNonzeroCandidatesForCapturableBeasts()
    {
        var lookup = BeastCaptureService.BuildModelLookup(new[]
        {
            new BeastRosterEntry(1, "First", 10),
            new BeastRosterEntry(1, "Same entry", 10),
            new BeastRosterEntry(2, "Shared model", 10),
            new BeastRosterEntry(0, "Invalid shared entry", 10),
            new BeastRosterEntry(3, "Shared first", 20),
            new BeastRosterEntry(4, "Shared second", 20),
            new BeastRosterEntry(5, "Missing primary model", 0),
            new BeastRosterEntry(0, "Placeholder", 30),
            new BeastRosterEntry(6, "Unique model", 40),
        });
        Assert.Equal(new uint[] { 1, 2 }, BeastCaptureService.ResolveBeasts(5, 10, lookup));
        Assert.Equal(new uint[] { 3, 4 }, BeastCaptureService.ResolveBeasts(5, 20, lookup));
        Assert.Equal(new uint[] { 6 }, BeastCaptureService.ResolveBeasts(5, 40, lookup));
        Assert.Empty(BeastCaptureService.ResolveBeasts(0, 10, lookup));
        Assert.Empty(BeastCaptureService.ResolveBeasts(4, 10, lookup));
        Assert.Empty(BeastCaptureService.ResolveBeasts(5, 99, lookup));
        Assert.Empty(BeastCaptureService.ResolveBeasts(5, 0, lookup));
        Assert.Empty(BeastCaptureService.ResolveBeasts(5, 30, lookup));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AcceptedAndRejectedCastsShareCooldownAcrossTargets(bool accepted)
    {
        var runtime = new FakeRuntime { CaptureAccepted = accepted };
        runtime.NearbyTargets.Add(EligibleTarget with { Id = 300 });
        var service = new BeastCaptureService(runtime);
        service.Update(1000);
        service.Update(1000);
        service.Update(1001);
        runtime.Target = EligibleTarget with { Id = 200 };
        service.Update(8499);
        Assert.Equal(new ulong[] { 100 }, runtime.CapturedTargets);

        runtime.Action = runtime.Action with { RecastActive = true };
        service.Update(8500);
        Assert.Single(runtime.CapturedTargets);
        runtime.Action = runtime.Action with { RecastActive = false };
        service.Update(8500);
        service.Update(8501);
        Assert.Equal(new ulong[] { 100, 200 }, runtime.CapturedTargets);

        // Returning to the same target (even if already marked) is eligible on the next cooldown.
        runtime.Target = EligibleTarget;
        service.Update(16000);
        Assert.Equal(new ulong[] { 100, 200, 100 }, runtime.CapturedTargets);
        Assert.Equal(runtime.CapturedTargets, runtime.SelectedTargets);
    }

    [Fact]
    public void SuspendAndCharacterOrJobChangesDoNotResetReservedAttempt()
    {
        var runtime = new FakeRuntime { ThrowCapture = true };
        var service = new BeastCaptureService(runtime);
        service.Update(1000);
        service.Suspend();
        runtime.ThrowCapture = false;
        runtime.Frame = runtime.Frame with { CharacterKey = "Second", IsBeastmaster = false };
        service.Update(1001);
        runtime.Frame = runtime.Frame with { IsBeastmaster = true };
        service.Update(1002);
        Assert.Single(runtime.CapturedTargets);
        service.Update(8500);
        Assert.Equal(2, runtime.CapturedTargets.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void OwnedOrUnknownBeastsAreSkipped(bool? owned)
    {
        var runtime = new FakeRuntime { Owned = owned };
        runtime.NearbyTargets.Add(EligibleTarget with { Id = 200 });
        new BeastCaptureService(runtime).Update(1000);
        Assert.Empty(runtime.CapturedTargets);
        Assert.Equal(0, runtime.ActionReads);
        Assert.Empty(runtime.SelectedTargets);
    }

    [Fact]
    public void SharedModelRemainsEligibleUntilAllCandidatesAreConfirmedOwned()
    {
        var runtime = new FakeRuntime { Owned = true };
        var candidates = BeastCaptureService.ResolveBeasts(5, 10, BeastCaptureService.BuildModelLookup(new[]
        {
            new BeastRosterEntry(1, "First", 10),
            new BeastRosterEntry(2, "Second", 10),
        }));
        runtime.Target = EligibleTarget with { CandidateBeastIds = candidates };
        runtime.BeastOwnership[2] = false;
        var service = new BeastCaptureService(runtime);

        service.Update(1000);
        Assert.Equal(new ulong[] { 100 }, runtime.CapturedTargets);
        Assert.Empty(runtime.Account.UnlockedBeasts["First"]);

        runtime.BeastOwnership[2] = true;
        service.Update(8500);
        Assert.Equal(new ulong[] { 100 }, runtime.CapturedTargets);
        Assert.Equal(1, runtime.ActionReads);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(null, false, true)]
    [InlineData(false, null, true)]
    [InlineData(true, true, false)]
    [InlineData(true, null, false)]
    [InlineData(null, true, false)]
    [InlineData(null, null, false)]
    public void SharedModelAttemptsRequireAtLeastOneConfirmedUnownedCandidate(bool? first, bool? second, bool expected)
    {
        var runtime = new FakeRuntime
        {
            Target = EligibleTarget with { CandidateBeastIds = new uint[] { 1, 2 } },
        };
        runtime.NearbyTargets.Add(runtime.Target.Value with { Id = 200 });
        runtime.BeastOwnership[1] = first;
        runtime.BeastOwnership[2] = second;

        new BeastCaptureService(runtime).Update(1000);

        var attempted = expected ? new ulong[] { 100 } : Array.Empty<ulong>();
        Assert.Equal(attempted, runtime.CapturedTargets);
        Assert.Equal(attempted, runtime.SelectedTargets);
        Assert.Equal(expected ? 1 : 0, runtime.ActionReads);
        Assert.Empty(runtime.Account.UnlockedBeasts["First"]);
    }

    [Fact]
    public void MissingTargetsAndAboveThresholdTargetsNeverReachActionCheck()
    {
        var runtime = new FakeRuntime { Target = null };
        var service = new BeastCaptureService(runtime);
        service.Update(1000);
        runtime.Target = EligibleTarget with { CurrentHp = 901 };
        service.Update(1001);
        runtime.Target = EligibleTarget with { CandidateBeastIds = Array.Empty<uint>() };
        service.Update(1002);
        Assert.Empty(runtime.CapturedTargets);
        Assert.Equal(0, runtime.ActionReads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnavailableOwnershipOrBlockedContextNeverAttempts(bool inCombat)
    {
        var runtime = new FakeRuntime();
        runtime.NearbyTargets.Add(EligibleTarget with { Id = 200 });
        var original = runtime.Frame with { InCombat = inCombat };
        var blocked = new[]
        {
            original with { Config = new CharacterConfig { Enabled = false } },
            original with { Config = new CharacterConfig { Enabled = true, TryToCatchBeasts = false } },
            original with { IsBeastmaster = false },
            original with { OwnershipLoaded = false },
            original with { CanAct = false },
            original with { AccountId = "" },
            original with { CharacterKey = "" },
        };
        foreach (var frame in blocked)
        {
            runtime.Frame = frame;
            new BeastCaptureService(runtime).Update(1000);
        }
        runtime.Frame = original;
        runtime.UnlockedAvailable = false;
        new BeastCaptureService(runtime).Update(1000);
        Assert.Empty(runtime.CapturedTargets);
        Assert.Equal(0, runtime.ActionReads);
        Assert.Empty(runtime.SelectedTargets);
    }

    [Fact]
    public void UnavailableActionsOrInvalidRecastNeverAttempts()
    {
        var runtime = new FakeRuntime();
        runtime.NearbyTargets.Add(EligibleTarget with { Id = 200 });
        foreach (var action in new[]
        {
            new BeastCaptureAction(false, false, 7.5f),
            new BeastCaptureAction(true, true, 7.5f),
            new BeastCaptureAction(true, false, 0),
            new BeastCaptureAction(true, false, -1),
            new BeastCaptureAction(true, false, float.NaN),
            new BeastCaptureAction(true, false, float.PositiveInfinity),
        })
        {
            runtime.Action = action;
            new BeastCaptureService(runtime).Update(1000);
        }
        Assert.Empty(runtime.CapturedTargets);
        Assert.Empty(runtime.SelectedTargets);
    }

    [Fact]
    public void RefreshOccursOnAvailabilityCombatExitAreaAndJobChangeEvenWhenDisabled()
    {
        var runtime = new FakeRuntime();
        runtime.Frame.Config.Enabled = false;
        runtime.Frame.Config.TryToCatchBeasts = false;
        var service = new BeastCaptureService(runtime);
        service.Update(0);
        service.Update(1);
        Assert.Equal(1, runtime.UnlockReads);
        runtime.Frame = runtime.Frame with { InCombat = false };
        service.Update(2);
        Assert.Equal(2, runtime.UnlockReads);
        runtime.Frame = runtime.Frame with { AreaChanged = true };
        service.Update(3);
        Assert.Equal(3, runtime.UnlockReads);
        runtime.Frame = runtime.Frame with { AreaChanged = false, OwnershipLoaded = false };
        runtime.Unlocked.Add(2);
        service.Update(4);
        Assert.Equal(3, runtime.UnlockReads);
        runtime.Frame = runtime.Frame with { OwnershipLoaded = true };
        service.Update(5);
        Assert.Equal(4, runtime.UnlockReads);
        Assert.Equal(new uint[] { 2 }, runtime.Account.UnlockedBeasts["First"]);

        runtime.Frame = runtime.Frame with { IsBeastmaster = false, AreaChanged = true };
        runtime.Unlocked.Add(3);
        service.Update(6);
        service.Update(7);
        Assert.Equal(4, runtime.UnlockReads);
        Assert.Equal(new uint[] { 2 }, runtime.Account.UnlockedBeasts["First"]);
        runtime.Frame = runtime.Frame with { IsBeastmaster = true, AreaChanged = false };
        service.Update(8);
        Assert.Equal(5, runtime.UnlockReads);
        Assert.Equal(new uint[] { 2, 3 }, runtime.Account.UnlockedBeasts["First"]);
        Assert.Empty(runtime.CapturedTargets);
    }

    [Fact]
    public void CastSuccessDoesNotConfirmCatchAndSnapshotsStayPerCharacter()
    {
        var runtime = new FakeRuntime();
        var service = new BeastCaptureService(runtime);
        service.Update(0);
        Assert.Single(runtime.CapturedTargets);
        Assert.Empty(runtime.Account.UnlockedBeasts["First"]);
        runtime.Unlocked.Add(1);
        service.Update(1);
        Assert.Empty(runtime.Account.UnlockedBeasts["First"]);
        runtime.Frame = runtime.Frame with { InCombat = false };
        service.Update(2);
        Assert.Equal(new uint[] { 1 }, runtime.Account.UnlockedBeasts["First"]);

        runtime.Frame = runtime.Frame with { CharacterKey = "Second" };
        runtime.Unlocked.Clear();
        runtime.Unlocked.Add(2);
        service.Update(3);
        Assert.Equal(new uint[] { 1 }, runtime.Account.UnlockedBeasts["First"]);
        Assert.Equal(new uint[] { 2 }, runtime.Account.UnlockedBeasts["Second"]);
        runtime.Frame = runtime.Frame with { CharacterKey = "", IsBeastmaster = false };
        service.Update(4);
        runtime.Frame = runtime.Frame with { CharacterKey = "First", IsBeastmaster = true };
        runtime.Unlocked.Clear();
        runtime.Unlocked.Add(1);
        service.Update(5);
        Assert.Equal(4, runtime.UnlockReads);
        Assert.Equal(3, runtime.SaveCount);
    }

    [Fact]
    public void UnavailableFullListPreservesSnapshotUntilConfirmationReturns()
    {
        var runtime = new FakeRuntime { UnlockedAvailable = false };
        runtime.Account.UnlockedBeasts["First"] = new() { 9 };
        var service = new BeastCaptureService(runtime);
        service.Update(0);
        service.Update(1);
        Assert.Equal(new uint[] { 9 }, runtime.Account.UnlockedBeasts["First"]);
        Assert.Equal(0, runtime.SaveCount);
        Assert.Empty(runtime.CapturedTargets);
        runtime.UnlockedAvailable = true;
        runtime.Unlocked.Add(9);
        runtime.Unlocked.Add(12); // Confirmed IDs are saved independently of the matching roster.
        service.Update(2);
        Assert.Equal(new uint[] { 9, 12 }, runtime.Account.UnlockedBeasts["First"]);
        Assert.Equal(1, runtime.SaveCount);
    }

    [Fact]
    public void ExistingAccountFilePathPersistsBothCharactersAndSurvivesResetAndReload()
    {
        var directory = Directory.CreateTempSubdirectory("FrenRiderCaptureTests-");
        try
        {
            var pi = DispatchProxy.Create<IDalamudPluginInterface, QuestionableTestProxy>();
            ((QuestionableTestProxy)(object)pi).Handler = (method, _) =>
                method.Name == "GetPluginConfigDirectory" ? directory.FullName : throw new InvalidOperationException(method.Name);
            var log = DispatchProxy.Create<IPluginLog, QuestionableTestProxy>();
            var saves = 0;
            ((QuestionableTestProxy)(object)log).Handler = (method, _) =>
            {
                if (method.Name == "Debug") saves++;
                return null;
            };
            var account = new AccountConfig { AccountId = "test-account", Characters = new() { ["First"] = new(), ["Second"] = new() } };
            var file = Path.Combine(directory.FullName, "test-account_FrenRider.json");
            File.WriteAllText(file, JsonSerializer.Serialize(account));
            var manager = new ConfigManager(pi, log) { CurrentAccountId = account.AccountId };
            var activeKey = typeof(ConfigManager).GetProperty(nameof(ConfigManager.ActiveCharacterKey))!;
            activeKey.SetValue(manager, "First");
            manager.SaveUnlockedBeasts(new HashSet<uint> { 8, 2 });
            manager.SaveUnlockedBeasts(new HashSet<uint> { 2, 8 });
            Assert.Equal(1, saves);
            manager.ResetCharacterToDefault("First");
            activeKey.SetValue(manager, "Second");
            manager.SaveUnlockedBeasts(new HashSet<uint> { 3 });
            var disk = JsonSerializer.Deserialize<AccountConfig>(File.ReadAllText(file))!;
            Assert.Equal(new uint[] { 2, 8 }, disk.UnlockedBeasts["First"]);
            Assert.Equal(new uint[] { 3 }, disk.UnlockedBeasts["Second"]);
            var reloaded = new ConfigManager(pi, log) { CurrentAccountId = account.AccountId };
            Assert.Equal(new uint[] { 2, 8 }, reloaded.GetCurrentAccount()!.UnlockedBeasts["First"]);
            Assert.Equal(new uint[] { 3 }, reloaded.GetCurrentAccount()!.UnlockedBeasts["Second"]);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void PersistenceAndSettingsTransportKeepCatchesSeparate()
    {
        var account = new AccountConfig
        {
            DefaultConfig = new CharacterConfig { TryToCatchBeasts = false, CaptureHpFarBelow = 17, CaptureHpNearOrEqual = 83 },
            Characters = new() { ["First"] = new(), ["Second"] = new() },
        };
        Assert.True(ConfigManager.UpdateUnlockedBeasts(account, "First", new HashSet<uint> { 8, 2 }));
        Assert.False(ConfigManager.UpdateUnlockedBeasts(account, "First", new HashSet<uint> { 2, 8 }));
        Assert.False(ConfigManager.UpdateUnlockedBeasts(account, "remote", new HashSet<uint> { 1 }));
        Assert.Equal(new uint[] { 2, 8 }, account.UnlockedBeasts["First"]);
        var roundTrip = JsonSerializer.Deserialize<AccountConfig>(JsonSerializer.Serialize(account))!;
        Assert.Equal(new uint[] { 2, 8 }, roundTrip.UnlockedBeasts["First"]);

        ConfigManager.ApplyDefaultTabToAllCharacters(roundTrip, "Combat");
        Assert.All(roundTrip.Characters.Values, config =>
        {
            Assert.False(config.TryToCatchBeasts);
            Assert.Equal(17, config.CaptureHpFarBelow);
            Assert.Equal(83, config.CaptureHpNearOrEqual);
        });
        roundTrip.Characters["First"] = roundTrip.DefaultConfig.Clone(); // Existing full-reset path.
        ConfigManager.ApplyDefaultToAllCharacters(roundTrip);
        Assert.Equal(new uint[] { 2, 8 }, roundTrip.UnlockedBeasts["First"]);
        Assert.False(roundTrip.UnlockedBeasts.ContainsKey("Second"));

        Assert.True(DadProfileTransferService.TrySerializeProfile(roundTrip.Characters["First"], out var json, out _));
        Assert.DoesNotContain("unlockedBeasts", json, StringComparison.OrdinalIgnoreCase);
        Assert.True(DadProfileTransferService.TryDeserializeProfile(json, out var transferred, out _));
        roundTrip.Characters["First"] = transferred!.Clone();
        Assert.False(transferred.TryToCatchBeasts);
        Assert.Equal(17, transferred.CaptureHpFarBelow);
        Assert.Equal(83, transferred.CaptureHpNearOrEqual);
        Assert.Equal(new uint[] { 2, 8 }, roundTrip.UnlockedBeasts["First"]);
    }

    [Fact]
    public void LegacyDefaultsClampingCloneAndIndividualSyncAreSupported()
    {
        var legacy = JsonSerializer.Deserialize<AccountConfig>("{\"Characters\":{\"First\":{}}}")!;
        var config = legacy.Characters["First"];
        Assert.True(config.TryToCatchBeasts);
        Assert.Equal(90, config.CaptureHpFarBelow);
        Assert.Equal(40, config.CaptureHpNearOrEqual);
        Assert.Empty(legacy.UnlockedBeasts);
        config.CaptureHpFarBelow = -10;
        config.CaptureHpNearOrEqual = 101;
        Assert.Equal(1, config.CaptureHpFarBelow);
        Assert.Equal(100, config.CaptureHpNearOrEqual);
        var clone = config.Clone();
        Assert.True(clone.TryToCatchBeasts);
        Assert.Equal(1, clone.CaptureHpFarBelow);
        Assert.Equal(100, clone.CaptureHpNearOrEqual);
        legacy.DefaultConfig = new() { TryToCatchBeasts = false, CaptureHpFarBelow = 22, CaptureHpNearOrEqual = 77 };
        foreach (var label in new[] { "Try to catch beasts", "Capture HP: more than 5 levels below you", "Capture HP: within 5 levels below you or equal" })
            Assert.Equal(1, ConfigManager.ApplyDefaultSettingToAllCharacters(legacy, label));
        Assert.False(config.TryToCatchBeasts);
        Assert.Equal(22, config.CaptureHpFarBelow);
        Assert.Equal(77, config.CaptureHpNearOrEqual);
    }

    // DispatchProxy cannot implement IDataManager's self-constrained Excel-sheet methods.
    private sealed class EmptyDataManager : IDataManager
    {
        public ClientLanguage Language => throw new NotSupportedException();
        public GameData GameData => throw new NotSupportedException();
        public ExcelModule Excel => throw new NotSupportedException();
        public bool HasModifiedGameDataFiles => throw new NotSupportedException();
        public ExcelSheet<T> GetExcelSheet<T>(ClientLanguage? language = null, string? name = null)
            where T : struct, IExcelRow<T> => null!;
        public SubrowExcelSheet<T> GetSubrowExcelSheet<T>(ClientLanguage? language = null, string? name = null)
            where T : struct, IExcelSubrow<T> => throw new NotSupportedException();
        public FileResource? GetFile(string path) => throw new NotSupportedException();
        public T? GetFile<T>(string path) where T : FileResource => throw new NotSupportedException();
        public Task<T> GetFileAsync<T>(string path, CancellationToken cancellationToken)
            where T : FileResource => throw new NotSupportedException();
        public bool FileExists(string path) => throw new NotSupportedException();
    }

    private sealed class FakeRuntime : IBeastCaptureRuntime
    {
        public IReadOnlyList<BeastRosterEntry> Roster { get; init; } = new[] { new BeastRosterEntry(1, "Beast", 10) };
        internal BeastCaptureFrame Frame = new("Account", "First", new() { Enabled = true }, true, true, true, false, true, 90);
        internal BeastCaptureTarget? Target = EligibleTarget;
        internal BeastCaptureAction Action = new(true, false, 7.5f);
        internal bool? Owned = false;
        internal bool UnlockedAvailable = true;
        internal bool CaptureAccepted = true;
        internal bool ThrowCapture;
        internal readonly List<BeastCaptureTarget> NearbyTargets = new();
        internal readonly Dictionary<uint, bool?> BeastOwnership = new();
        internal readonly Dictionary<ulong, BeastCaptureAction> TargetActions = new();
        internal Func<ulong, bool>? TargetSelector;
        internal readonly List<ulong> SelectedTargets = new();
        internal readonly HashSet<uint> Unlocked = new();
        internal readonly List<ulong> CapturedTargets = new();
        internal readonly AccountConfig Account = new() { Characters = new() { ["First"] = new(), ["Second"] = new() } };
        internal int UnlockReads;
        internal int ActionReads;
        internal int SaveCount;
        public BeastCaptureFrame ReadFrame() => Frame;
        public BeastCaptureTarget? ReadTarget() => Target;
        public IEnumerable<BeastCaptureTarget> ReadNearbyTargets() => NearbyTargets;
        public bool? IsOwned(uint beastId) => BeastOwnership.TryGetValue(beastId, out var owned) ? owned : Owned;
        public IReadOnlySet<uint>? ReadUnlockedBeasts()
        {
            UnlockReads++;
            return UnlockedAvailable ? new HashSet<uint>(Unlocked) : null;
        }
        public void SaveUnlockedBeasts(IReadOnlySet<uint> beastIds)
        {
            if (ConfigManager.UpdateUnlockedBeasts(Account, Frame.CharacterKey, beastIds))
                SaveCount++;
        }
        public BeastCaptureAction ReadAction(ulong targetId)
        {
            ActionReads++;
            return TargetActions.TryGetValue(targetId, out var action) ? action : Action;
        }
        public bool TargetBeast(ulong targetId)
        {
            if (TargetSelector?.Invoke(targetId) == false)
                return false;
            SelectedTargets.Add(targetId);
            return true;
        }
        public bool UseCapture(ulong targetId)
        {
            CapturedTargets.Add(targetId);
            if (ThrowCapture)
                throw new InvalidOperationException("Native call unavailable");
            return CaptureAccepted;
        }
    }
}
