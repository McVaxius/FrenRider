using System.Numerics;
using System.Reflection;
using System.Text.Json;
using FrenRider.IPC;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class PhoenixDownRecoveryTests
{
    private static PhoenixActor Local => new(1, 10, true, false, false, Vector3.Zero);
    private static PhoenixActor Corpse => new(2, 20, true, true, false, new Vector3(10, 0, 0));

    [Fact]
    public void LegacyAccountProfilesReceiveDefaultsWithoutReplacingExplicitValues()
    {
        var account = JsonSerializer.Deserialize<AccountConfig>(
            "{\"Characters\":{\"Legacy@World\":{},\"Explicit@World\":{\"UsePhoenixDownsForRecovery\":false,\"ReviveAnyoneOutdoors\":false,\"AllowPhoenixDownInCombat\":true}}}")!;
        Assert.True(account.DefaultConfig.UsePhoenixDownsForRecovery);
        Assert.True(account.Characters["Legacy@World"].UsePhoenixDownsForRecovery);
        Assert.True(account.Characters["Legacy@World"].ReviveAnyoneOutdoors);
        Assert.False(account.Characters["Legacy@World"].AllowPhoenixDownInCombat);
        AssertOverrides(account.Characters["Explicit@World"]);
    }

    [Fact]
    public void ExcludedScopeCannotHoldOrUseAnItem()
    {
        using var lab = new Lab();
        lab.Frame = lab.Frame with { Scope = PhoenixRecoveryScope.None };
        lab.Service.Update(1000);
        Assert.Empty(lab.Attempts);
        Assert.False(lab.Movement);
        Assert.False(lab.Service.ShouldPauseDutyProgression());
    }

    [Fact]
    public void DefaultsLegacyOverridesCloneAndTransfer()
    {
        foreach (var config in new[] { new CharacterConfig(), JsonSerializer.Deserialize<CharacterConfig>("{}")! })
        {
            Assert.True(config.UsePhoenixDownsForRecovery);
            Assert.True(config.ReviveAnyoneOutdoors);
            Assert.False(config.AllowPhoenixDownInCombat);
        }
        var overridden = JsonSerializer.Deserialize<CharacterConfig>(
            "{\"UsePhoenixDownsForRecovery\":false,\"ReviveAnyoneOutdoors\":false,\"AllowPhoenixDownInCombat\":true}")!;
        AssertOverrides(overridden.Clone());
        Assert.True(DadProfileTransferService.TrySerializeProfile(overridden, out var json, out _));
        Assert.True(DadProfileTransferService.TryDeserializeProfile(json, out var transferred, out _));
        AssertOverrides(transferred!);
    }

    [Fact]
    public void AllThreeSettingsSyncByRowTabAndFullProfileAndResetToDefaults()
    {
        var account = new AccountConfig
        {
            DefaultConfig = new CharacterConfig { UsePhoenixDownsForRecovery = false, ReviveAnyoneOutdoors = false, AllowPhoenixDownInCombat = true },
            Characters = new() { ["Synthetic@World"] = new() },
        };
        var target = account.Characters.Values.Single();
        foreach (var name in new[] { "Use Phoenix Downs for recovery", "Revive anyone within range outdoors", "Allow Phoenix Down use during combat" })
            Assert.Equal(1, ConfigManager.ApplyDefaultSettingToAllCharacters(account, name));
        AssertOverrides(target);
        account.Characters["Synthetic@World"] = new();
        Assert.Equal(1, ConfigManager.ApplyDefaultTabToAllCharacters(account, "Profile"));
        AssertOverrides(account.Characters.Values.Single());
        account.Characters["Synthetic@World"] = new();
        Assert.Equal(1, ConfigManager.ApplyDefaultToAllCharacters(account));
        AssertOverrides(account.Characters.Values.Single());
        Assert.True((bool)typeof(ConfigManager).GetMethod("ApplyTabSettings", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { new CharacterConfig(), account.Characters.Values.Single(), "Profile" })!);
        Assert.True(account.Characters.Values.Single().UsePhoenixDownsForRecovery);
        Assert.True(account.Characters.Values.Single().ReviveAnyoneOutdoors);
        Assert.False(account.Characters.Values.Single().AllowPhoenixDownInCombat);
    }

    [Theory]
    [InlineData(true, 2u, 4, true, 2)]
    [InlineData(true, 2u, 8, true, 0)]
    [InlineData(true, 4u, 4, true, 0)]
    [InlineData(true, 5u, 4, true, 0)]
    [InlineData(true, 21u, 4, true, 0)]
    [InlineData(true, 30u, 4, true, 0)]
    [InlineData(true, 0u, 4, true, 0)]
    [InlineData(false, 0u, 0, true, 1)]
    [InlineData(false, 0u, 0, false, 0)]
    public void ScopeUsesDutyMetadataAndExcludesOtherInstances(bool instance, uint content, int size, bool outdoor, int expected)
        => Assert.Equal((PhoenixRecoveryScope)expected, PhoenixDownRecoveryService.ResolveScope(instance, content, size, outdoor));

    [Theory]
    [InlineData(20f, false, true, 0)]
    [InlineData(20.01f, false, true, 1)]
    [InlineData(20f, true, true, 1)]
    [InlineData(20f, false, false, 0)]
    [InlineData(20.01f, false, false, 1)]
    public void OnlyLivingHealersAtOrInsideTwentyYalmsBlock(float distance, bool dead, bool party, int expected)
    {
        using var lab = new Lab();
        lab.Actors.Add(new PhoenixActor(3, 30, party, dead, true, Corpse.Position + new Vector3(distance, 0, 0), PendingRaise: dead));
        lab.Service.Update(1000);
        Assert.Equal(expected, lab.Attempts.Count);
        Assert.True(lab.Service.PartyRecoveryActive);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public void CombatOffWaitsBeforeApproachAndItemEvenWhenNativeAllows(int scope)
    {
        using var lab = new Lab();
        lab.Frame = lab.Frame with { Scope = (PhoenixRecoveryScope)scope, InCombat = true };
        lab.Actors[1] = Corpse with { Position = new Vector3(30, 0, 0) };
        lab.Service.Update(1000);
        lab.Service.Update(2000);
        Assert.Empty(lab.Attempts);
        Assert.Empty(lab.Approaches);
        Assert.True(lab.Movement);
        Assert.True(lab.PreventPulls);
        Assert.False(lab.Actions); // Survivors can finish the current fight.
        lab.Frame = lab.Frame with { InCombat = false };
        lab.Service.Update(3000);
        Assert.Single(lab.Approaches);
        lab.Actors[0] = Local with { Position = new Vector3(16, 0, 0) };
        lab.Service.Update(3100);
        Assert.Equal(Corpse.Id, Assert.Single(lab.Attempts));
        Assert.False(lab.Navigating);
        Assert.Equal(14.5f, lab.Approaches.Single().Range);
    }

    [Theory]
    [InlineData(false, false, true, true)]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, true, true)]
    public void CombatOnStillRespectsInventoryCooldownAvailabilityAndLineOfSight(bool item, bool cooldown, bool available, bool los)
    {
        using var lab = new Lab();
        lab.Frame.Config.AllowPhoenixDownInCombat = true;
        lab.Frame = lab.Frame with { InCombat = true };
        lab.Item = new(item, cooldown, available, los);
        lab.Service.Update(1000);
        Assert.Equal(item && !cooldown && available && los ? 1 : 0, lab.Attempts.Count);
        Assert.True(lab.Movement);
    }

    [Fact]
    public void PartyHealersComeFirstThenOtherPartyMembersBeforeOutdoorStrangers()
    {
        using var lab = new Lab();
        lab.Frame = lab.Frame with { Scope = PhoenixRecoveryScope.Outdoors };
        lab.Actors.Add(new PhoenixActor(3, 30, true, true, true, new Vector3(8, 0, 0)));
        lab.Actors.Add(new PhoenixActor(4, 40, false, true, true, new Vector3(2, 0, 0)));
        lab.Service.Update(1000);
        Assert.Equal(3ul, Assert.Single(lab.Attempts));
    }

    [Theory]
    [InlineData(15f, true, 1)]
    [InlineData(15.01f, true, 0)]
    [InlineData(10f, false, 0)]
    public void OutdoorStrangersAreNeverApproachedAndRespectToggleAndRange(float distance, bool anyone, int expected)
    {
        using var lab = new Lab();
        lab.Frame = lab.Frame with { Scope = PhoenixRecoveryScope.Outdoors };
        lab.Frame.Config.ReviveAnyoneOutdoors = anyone;
        lab.Actors[1] = Corpse with { Party = false, Position = new Vector3(distance, 0, 0) };
        lab.Service.Update(1000);
        Assert.Empty(lab.Approaches);
        Assert.Equal(expected, lab.Attempts.Count);
        Assert.False(lab.Service.PartyRecoveryActive);
    }

    [Fact]
    public void PendingRaiseAndObservedCastSkipRecipientWithoutReleasingPartyHold()
    {
        using var lab = new Lab();
        lab.Actors[1] = Corpse with { PendingRaise = true };
        lab.Service.Update(1000);
        Assert.Empty(lab.Attempts);
        Assert.True(lab.Movement);
        lab.Actors[1] = Corpse;
        lab.Actors.Add(new PhoenixActor(3, 30, false, false, false, Vector3.Zero, CastTarget: Corpse.Id));
        lab.Service.Update(2000);
        Assert.Empty(lab.Attempts);
        lab.Actors.RemoveAt(2);
        lab.Service.Update(3000);
        Assert.Single(lab.Attempts);
    }

    [Fact]
    public void SortedContentIdsStaggerEveryClientAndMissingItemsDoNotReserveTheRecipient()
    {
        using var first = new Lab();
        using var second = new Lab();
        second.Actors[0] = Local with { ContentId = 30 };
        second.Frame = second.Frame with { CharacterId = 30, PartyContentIds = new ulong[] { 30, 20, 10 } };
        first.Frame = first.Frame with { PartyContentIds = new ulong[] { 30, 20, 10 } };
        first.Item = new(false, false, true, true);
        first.Service.Update(1000);
        second.Service.Update(1000);
        second.Service.Update(1999);
        Assert.Empty(second.Attempts);
        second.Service.Update(2000);
        Assert.Equal(Corpse.Id, Assert.Single(second.Attempts));
        Assert.Empty(first.Attempts);
        Assert.True(first.Movement);
    }

    [Fact]
    public void RejectedAndInterruptedAttemptsWaitOneSecondAndObservedCastHoldsUntilEnd()
    {
        using var lab = new Lab { Accepted = false };
        lab.Service.Update(1000);
        lab.Service.Update(1999);
        Assert.Single(lab.Attempts);
        lab.Accepted = true;
        lab.Service.Update(2000);
        Assert.Equal(2, lab.Attempts.Count);
        lab.Actors[0] = Local with { PhoenixCast = true, CastTarget = Corpse.Id };
        lab.Frame = lab.Frame with { Casting = true };
        lab.Service.Update(2100);
        lab.Service.Update(15000);
        Assert.True(lab.Actions);
        Assert.True(lab.Movement);
        Assert.Equal(2, lab.Attempts.Count);
        lab.Actors[0] = Local;
        lab.Frame = lab.Frame with { Casting = false };
        lab.Service.Update(15001);
        Assert.False(lab.Actions);
        lab.Service.Update(16000);
        Assert.Equal(2, lab.Attempts.Count);
        lab.Service.Update(16001);
        Assert.Equal(3, lab.Attempts.Count);
    }

    [Fact]
    public void RevivalAndHealerAppearanceStopFurtherAttempts()
    {
        using var lab = new Lab();
        lab.Service.Update(1000);
        lab.Actors[0] = Local with { PhoenixCast = true, CastTarget = Corpse.Id };
        lab.Service.Update(1100);
        lab.Actors[1] = Corpse with { Dead = false };
        lab.Service.Update(1200);
        Assert.True(lab.Movement); // The observed cast is still in flight.
        lab.Actors[0] = Local;
        lab.Service.Update(1300);
        Assert.False(lab.Movement);
        Assert.False(lab.Actions);
        lab.Actors[1] = Corpse;
        lab.Actors.Add(new PhoenixActor(3, 30, false, false, true, Corpse.Position));
        lab.Service.Update(10000);
        Assert.Single(lab.Attempts);
        Assert.True(lab.Movement);
    }

    [Fact]
    public void GroundMountDismountsFlyingWaitsAndMissingNavigationBlocks()
    {
        using var lab = new Lab();
        lab.Frame = lab.Frame with { Mounted = true, Flying = true };
        lab.Service.Update(1000);
        Assert.Equal(0, lab.Dismounts);
        lab.Frame = lab.Frame with { Flying = false };
        lab.Service.Update(2000);
        Assert.Equal(1, lab.Dismounts);
        Assert.Empty(lab.Attempts);
        lab.Frame = lab.Frame with { Mounted = false };
        lab.Actors[1] = Corpse with { Position = new Vector3(30, 0, 0) };
        lab.ApproachResult = PhoenixApproachResult.Blocked;
        lab.Service.Update(3000);
        Assert.Contains("unreachable", lab.Service.StatusText);
        Assert.Empty(lab.Attempts);
        Assert.True(lab.Movement);
    }

    [Fact]
    public void AdsAcknowledgementMustPrecedeMovementAndItems()
    {
        using var lab = new Lab { AdsOwnsDuty = true, AcknowledgementAvailable = false };
        lab.Service.Update(1000);
        Assert.Contains("update ADS", lab.Service.StatusText);
        lab.AcknowledgementAvailable = true;
        lab.Service.Update(2000);
        Assert.Empty(lab.Attempts);
        lab.Acknowledged = true;
        lab.Service.Update(3000);
        Assert.Single(lab.Attempts);
    }

    [Theory]
    [InlineData("combat")]
    [InlineData("healer")]
    [InlineData("cooldown")]
    [InlineData("revived")]
    [InlineData("disabled")]
    [InlineData("range")]
    [InlineData("identity")]
    [InlineData("los")]
    public void ReadinessChangesImmediatelyBeforeUsePreventAttempt(string change)
    {
        using var lab = new Lab();
        lab.OnActionHold = () =>
        {
            switch (change)
            {
                case "combat": lab.Frame = lab.Frame with { InCombat = true }; break;
                case "healer": lab.Actors.Add(new PhoenixActor(3, 30, false, false, true, Corpse.Position)); break;
                case "cooldown": lab.Item = lab.Item with { Cooldown = true }; break;
                case "revived": lab.Actors[1] = Corpse with { Dead = false }; break;
                case "disabled": lab.Frame.Config.UsePhoenixDownsForRecovery = false; break;
                case "range": lab.Actors[1] = Corpse with { Position = new Vector3(20, 0, 0) }; break;
                case "identity": lab.Frame = lab.Frame with { CharacterId = 99 }; break;
                case "los": lab.Item = lab.Item with { LineOfSight = false }; break;
            }
        };
        lab.Service.Update(1000);
        Assert.Empty(lab.Attempts);
        Assert.False(lab.Actions);
    }

    [Fact]
    public void DeadMemberDefersReturnButFullWipeReleasesIt()
    {
        using var lab = new Lab();
        lab.Actors[0] = Local with { Dead = true };
        lab.Actors[1] = Corpse with { Dead = false };
        lab.Service.Update(1000);
        Assert.True(lab.Service.DeferReturn);
        lab.Actors[1] = Corpse;
        lab.Service.Update(2000);
        Assert.False(lab.Service.DeferReturn);
        Assert.False(lab.Movement);
        Assert.Empty(lab.Attempts);
    }

    [Fact]
    public void OffscreenLivingPartyRescuerStillDefersReturn()
    {
        using var lab = new Lab();
        lab.Actors[0] = Local with { Dead = true };
        lab.Actors.RemoveAt(1); // The living rescuer is present only in native party HP truth.
        lab.Frame = lab.Frame with { PartyHasLivingMember = true };
        lab.Service.Update(1000);
        Assert.True(lab.Service.DeferReturn);
        Assert.True(lab.Service.ShouldPauseDutyProgression());
        lab.Frame = lab.Frame with { PartyHasLivingMember = false };
        lab.Service.Update(2000);
        Assert.False(lab.Service.DeferReturn);
    }

    [Fact]
    public void OffscreenPartyDeathHoldsProgressionWithoutTargetingAnUnresolvedActor()
    {
        using var lab = new Lab();
        lab.Actors.RemoveAt(1);
        lab.Frame = lab.Frame with { PartyHasDeadMember = true };
        lab.Service.Update(1000);
        Assert.True(lab.Service.ShouldPauseDutyProgression());
        Assert.True(lab.Movement);
        Assert.Contains("confirmed party corpse", lab.Service.StatusText);
        Assert.Empty(lab.Attempts);
        Assert.Empty(lab.Approaches);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("logout")]
    [InlineData("character")]
    [InlineData("transition")]
    [InlineData("unload")]
    public void CleanupReleasesActionsMovementNavigationAndReturn(string reason)
    {
        using var lab = new Lab();
        lab.Service.Update(1000);
        Assert.True(lab.Actions);
        switch (reason)
        {
            case "disable": lab.Frame.Config.UsePhoenixDownsForRecovery = false; break;
            case "logout": lab.Frame = lab.Frame with { Ready = false }; break;
            case "character": lab.Frame = lab.Frame with { CharacterId = 99, Actors = Array.Empty<PhoenixActor>() }; break;
            case "transition": lab.Service.Reset(); break;
            case "unload": lab.Service.Dispose(); break;
        }
        if (reason is not "transition" and not "unload")
            lab.Service.Update(1100);
        Assert.False(lab.Movement);
        Assert.False(lab.Actions);
        Assert.False(lab.Service.DeferReturn);
        Assert.False(lab.Navigating);
    }

    private static void AssertOverrides(CharacterConfig config)
    {
        Assert.False(config.UsePhoenixDownsForRecovery);
        Assert.False(config.ReviveAnyoneOutdoors);
        Assert.True(config.AllowPhoenixDownInCombat);
    }

    internal sealed class Lab : IPhoenixRecoveryRuntime, IDisposable
    {
        internal Lab()
        {
            Actors = new() { Local, Corpse };
            Frame = new() { Config = new() { Enabled = true }, Ready = true, CharacterId = 10, LocalId = 1,
                TerritoryId = 777, DutyId = 888, Scope = PhoenixRecoveryScope.Dungeon, Actors = Actors, PartyContentIds = new ulong[] { 10, 20 } };
            Service = new(this);
        }
        internal PhoenixDownRecoveryService Service { get; }
        internal PhoenixRecoveryFrame Frame;
        internal List<PhoenixActor> Actors;
        internal PhoenixItemReadiness Item = new(true, false, true, true);
        internal readonly List<ulong> Attempts = new();
        internal readonly List<(ulong Target, float Range)> Approaches = new();
        internal bool Accepted = true;
        internal bool AcknowledgementAvailable = true;
        internal bool Acknowledged;
        internal bool Movement, Actions, PreventPulls, Navigating;
        internal int Dismounts;
        internal PhoenixApproachResult ApproachResult = PhoenixApproachResult.Moving;
        internal Action? OnActionHold;
        public bool AdsOwnsDuty { get; set; }
        public PhoenixRecoveryFrame ReadFrame() => Frame;
        public PhoenixItemReadiness ReadItem(ulong targetId) => Item;
        public bool TryGetAdsAcknowledgement(out bool acknowledged) { acknowledged = Acknowledged; return AcknowledgementAvailable; }
        public void SetHolds(bool movement, bool actions, bool preventPulls)
        {
            Movement = movement; Actions = actions; PreventPulls = preventPulls;
            if (actions) { var callback = OnActionHold; OnActionHold = null; callback?.Invoke(); }
        }
        public PhoenixApproachResult Approach(PhoenixActor target, float stopRange, long now)
        { Approaches.Add((target.Id, stopRange)); Navigating = ApproachResult == PhoenixApproachResult.Moving; return ApproachResult; }
        public void StopApproach() => Navigating = false;
        public void Dismount() => Dismounts++;
        public bool UsePhoenixDown(ulong targetId) { Attempts.Add(targetId); return Accepted; }
        public void Dispose() => Service.Reset();
    }
}
