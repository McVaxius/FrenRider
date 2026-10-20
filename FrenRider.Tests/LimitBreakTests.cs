using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using FrenRider.IPC;
using FrenRider.Models;
using FrenRider.Services;
using Xunit;

namespace FrenRider.Tests;

public sealed class LimitBreakTests
{
    [Theory]
    [InlineData(19u, LimitBreakRole.Tank)]
    [InlineData(24u, LimitBreakRole.Healer)]
    [InlineData(41u, LimitBreakRole.Melee)]
    [InlineData(38u, LimitBreakRole.PhysicalRanged)]
    [InlineData(42u, LimitBreakRole.MagicalRanged)]
    [InlineData(0u, LimitBreakRole.None)]
    public void JobChangesResolveOnlyTheCurrentRole(uint job, LimitBreakRole expected)
        => Assert.Equal(expected, LimitBreakPolicy.Role(job));

    [Fact]
    public void NativeTargetFootprintsCountTheChosenCircleOrForwardLine()
    {
        var enemies = new[] { Enemy(10, 0), Enemy(12, 0), Enemy(-10, 0), Enemy(10, 4), Enemy(10, 10) };
        Assert.Equal(1, CombatService.CountLimitBreakHits(LimitBreakRole.Melee, Vector3.Zero, enemies[0], enemies, 5));
        Assert.Equal(3, CombatService.CountLimitBreakHits(LimitBreakRole.MagicalRanged, Vector3.Zero, enemies[0], enemies, 5));
        Assert.Equal(2, CombatService.CountLimitBreakHits(LimitBreakRole.PhysicalRanged, Vector3.Zero, enemies[0], enemies, 30));
    }

    private static IBattleNpc Enemy(float x, float z)
    {
        var enemy = DispatchProxy.Create<IBattleNpc, EnemyProxy>();
        ((EnemyProxy)(object)enemy).Position = new(x, 0, z);
        return enemy;
    }

    public class EnemyProxy : DispatchProxy
    {
        internal Vector3 Position;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_Position" => Position,
            "get_HitboxRadius" => 1f,
            _ => throw new InvalidOperationException("Unexpected native observation: " + method?.Name),
        };
    }

    private static LimitBreakObservation Ready(LimitBreakRole role) => new(true, true, true, false,
        role, 3, 3, 30, 3, true, true, 100, 3);

    [Fact]
    public void OldProfilesRetainThresholdWithoutEnablingAnyRoleAndClonesAreIndependent()
    {
        var old = JsonSerializer.Deserialize<CharacterConfig>("""{"LimitPct":42}""")!;
        Assert.Equal(42, old.LimitBreak.Melee.TargetHpPercent);
        foreach (var role in Enum.GetValues<LimitBreakRole>()) Assert.Equal(0, LimitBreakPolicy.Decide(old.LimitBreak, Ready(role)));
        Assert.True(old.LimitBreak.Healer.Revive);
        Assert.False(old.LimitBreak.Healer.Heal);
        Assert.Equal(3, old.LimitBreak.PhysicalRanged.MinimumEnemies);
        var clone = old.Clone();
        clone.LimitBreak.Melee.Enabled = true;
        clone.LimitBreak.Healer.MinimumKoMembers = 5;
        Assert.False(old.LimitBreak.Melee.Enabled);
        Assert.Equal(3, old.LimitBreak.Healer.MinimumKoMembers);
        var stored = Newtonsoft.Json.JsonConvert.DeserializeObject<CharacterConfig>(
            Newtonsoft.Json.JsonConvert.SerializeObject(clone))!;
        Assert.True(stored.LimitBreak.Melee.Enabled);
        Assert.Equal(5, stored.LimitBreak.Healer.MinimumKoMembers);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 0)]
    [InlineData(2, 2, 2)]
    [InlineData(3, 2, 0)]
    [InlineData(3, 3, 3)]
    [InlineData(0, 0, 0)]
    [InlineData(4, 4, 0)]
    public void FullAvailableRequiresEveryAvailableBar(int available, int full, int expected)
        => Assert.Equal(expected, LimitBreakPolicy.Tier(LimitBreakGauge.FullAvailable, available, full));

    [Fact]
    public void ExplicitTierRequiresAvailabilityAndNeverSilentlyUpgrades()
    {
        Assert.Equal(0, LimitBreakPolicy.Tier(LimitBreakGauge.LB3, 2, 2));
        Assert.Equal(0, LimitBreakPolicy.Tier(LimitBreakGauge.LB2, 3, 1));
        Assert.Equal(2, LimitBreakPolicy.Tier(LimitBreakGauge.LB2, 3, 3));
        Assert.Equal(1, LimitBreakPolicy.Tier(LimitBreakGauge.LB1, 3, 2));
    }

    [Fact]
    public void DamageRolesHonorIndependentTargetsHpAndActionAreaCounts()
    {
        var settings = new LimitBreakSettings();
        settings.Melee.Enabled = true;
        settings.Melee.TargetHpPercent = 50;
        var s = Ready(LimitBreakRole.Melee) with { TargetHpPercent = 50, EnemiesHit = 1 };
        Assert.Equal(3, LimitBreakPolicy.Decide(settings, s));
        Assert.Equal(0, LimitBreakPolicy.Decide(settings, s with { TargetHpPercent = 51 }));
        Assert.Equal(0, LimitBreakPolicy.Decide(settings, s with { TargetIsBoss = false }));
        Assert.Equal(0, LimitBreakPolicy.Decide(settings, s with { HasTarget = false }));
        Assert.Equal(0, LimitBreakPolicy.Decide(settings, Ready(LimitBreakRole.PhysicalRanged)));
        settings.PhysicalRanged.Enabled = true;
        Assert.Equal(3, LimitBreakPolicy.Decide(settings, Ready(LimitBreakRole.PhysicalRanged) with { TargetIsBoss = false }));
        Assert.Equal(0, LimitBreakPolicy.Decide(settings, Ready(LimitBreakRole.PhysicalRanged) with { EnemiesHit = 2 }));
        Assert.Equal(0, LimitBreakPolicy.Decide(settings, Ready(LimitBreakRole.MagicalRanged)));
    }

    [Fact]
    public void TankAndHealerTriggersCannotSpendForAnotherRole()
    {
        var settings = new LimitBreakSettings();
        settings.Tank.Enabled = true;
        Assert.Equal(3, LimitBreakPolicy.Decide(settings, Ready(LimitBreakRole.Tank)));
        Assert.Equal(0, LimitBreakPolicy.Decide(settings, Ready(LimitBreakRole.Tank) with { PartyHpPercent = 31 }));
        Assert.Equal(0, LimitBreakPolicy.Decide(settings, Ready(LimitBreakRole.Tank) with { FullBars = 2 }));
        Assert.Equal(0, LimitBreakPolicy.Decide(settings, Ready(LimitBreakRole.Healer)));
        settings.Healer.Enabled = true;
        Assert.Equal(3, LimitBreakPolicy.Decide(settings, Ready(LimitBreakRole.Healer)));
        Assert.Equal(0, LimitBreakPolicy.Decide(settings, Ready(LimitBreakRole.Healer) with { KoMembers = 2 }));
        Assert.Equal(0, LimitBreakPolicy.Decide(settings, Ready(LimitBreakRole.Healer) with { FullBars = 2 }));
        settings.Healer.Heal = true;
        Assert.Equal(2, LimitBreakPolicy.Decide(settings, Ready(LimitBreakRole.Healer) with { FullBars = 2 }));
        Assert.Equal(0, LimitBreakPolicy.Decide(settings, Ready(LimitBreakRole.Healer) with { KoMembers = 0 }));
        Assert.Equal(0, LimitBreakPolicy.Decide(settings, Ready(LimitBreakRole.Healer) with { FullBars = 2, PartyHpPercent = 31 }));
        Assert.Equal(0, LimitBreakPolicy.Decide(settings, Ready(LimitBreakRole.Melee)));
    }

    [Fact]
    public void InactiveOutOfCombatBlockedOrPendingCannotRequestLimitBreak()
    {
        var settings = new LimitBreakSettings();
        settings.Melee.Enabled = true;
        var s = Ready(LimitBreakRole.Melee);
        Assert.Equal(0, LimitBreakPolicy.Decide(settings, s with { Active = false }));
        Assert.Equal(0, LimitBreakPolicy.Decide(settings, s with { InCombat = false }));
        Assert.Equal(0, LimitBreakPolicy.Decide(settings, s with { CanAct = false }));
        Assert.Equal(0, LimitBreakPolicy.Decide(settings, s with { Pending = true }));
    }

    [Fact]
    public void NativeRequestLatchWaitsForCastLockConsumptionOrUnstartedRequestExpiry()
    {
        var combat = new CombatService(null!, null!, null!, null!);
        combat.RecordLimitBreakRequest(100, 30000, 10);
        Assert.True(combat.IsLimitBreakRequestPending(30000, false, 0, false, 20));
        Assert.True(combat.IsLimitBreakRequestPending(0, true, 100, false, 6000));
        Assert.True(combat.IsLimitBreakRequestPending(0, false, 0, true, 6001));
        Assert.False(combat.IsLimitBreakRequestPending(0, false, 0, false, 6002));
        combat.RecordLimitBreakRequest(101, 20000, 7000);
        Assert.True(combat.IsLimitBreakRequestPending(20000, false, 0, false, 11999));
        Assert.False(combat.IsLimitBreakRequestPending(20000, false, 0, false, 12000));
    }

    [Fact]
    public void RoleSyncAndTransferDeepCopyAllPoliciesAndRejectIncompleteImports()
    {
        var account = new AccountConfig();
        account.DefaultConfig.LimitBreak.Tank.Enabled = true;
        account.DefaultConfig.LimitBreak.Healer.Heal = true;
        account.DefaultConfig.LimitBreak.Melee.TargetHpPercent = 20;
        account.DefaultConfig.LimitBreak.PhysicalRanged.MinimumEnemies = 5;
        account.DefaultConfig.LimitBreak.MagicalRanged.Gauge = LimitBreakGauge.LB2;
        account.Characters["Example@TestWorld"] = new CharacterConfig();
        Assert.Equal(1, ConfigManager.ApplyDefaultTabToAllCharacters(account, "Limit Break"));
        var target = account.Characters["Example@TestWorld"];
        Assert.Equal(5, target.LimitBreak.PhysicalRanged.MinimumEnemies);
        target.LimitBreak.Melee.TargetHpPercent = 77;
        Assert.Equal(20, account.DefaultConfig.LimitBreak.Melee.TargetHpPercent);
        Assert.True(DadProfileTransferService.TrySerializeProfile(target, out var json, out _));
        Assert.True(DadProfileTransferService.TryDeserializeProfile(json, out var restored, out _));
        Assert.True(restored!.LimitBreak.Tank.Enabled);
        Assert.True(restored.LimitBreak.Healer.Heal);
        Assert.Equal(77, restored.LimitBreak.Melee.TargetHpPercent);
        Assert.Equal(LimitBreakGauge.LB2, restored.LimitBreak.MagicalRanged.Gauge);
        var node = JsonNode.Parse(json)!;
        node["config"]!.AsObject().Remove("limitBreak");
        Assert.True(DadProfileTransferService.TryDeserializeProfile(node.ToJsonString(), out restored, out _));
        Assert.False(restored!.LimitBreak.Tank.Enabled);
        foreach (var policy in new[] { "tank", "healer", "melee", "physicalRanged", "magicalRanged" })
        {
            node = JsonNode.Parse(json)!;
            node["config"]!["limitBreak"]!.AsObject().Remove(policy);
            Assert.False(DadProfileTransferService.TryDeserializeProfile(node.ToJsonString(), out _, out _));
        }
        foreach (var invalid in new[] { "null", "{}", "{\"tank\":null}" })
        {
            node = JsonNode.Parse(json)!;
            node["config"]!["limitBreak"] = JsonNode.Parse(invalid);
            Assert.False(DadProfileTransferService.TryDeserializeProfile(node.ToJsonString(), out _, out _));
        }
        node = JsonNode.Parse(json)!;
        node["config"]!["limitBreak"]!["melee"]!["gauge"] = 99;
        Assert.False(DadProfileTransferService.TryDeserializeProfile(node.ToJsonString(), out _, out _));
    }
}
