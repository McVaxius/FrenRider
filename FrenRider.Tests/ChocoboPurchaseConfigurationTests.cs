using System.Reflection;
using System.Text.Json;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class ChocoboPurchaseConfigurationTests
{
    [Fact]
    public void NewAndLegacyProfilesLeavePurchasePoliciesUnset()
    {
        var legacy = JsonSerializer.Deserialize<CharacterConfig>(
            """{"ForceGysahl":true,"CompanionStrat":"Healer Stance","Cling":7.5}""")!;

        foreach (var config in new[] { new CharacterConfig(), legacy })
        {
            Assert.Equal(0, config.ChocoboGreensStockTarget);
            Assert.Equal(0, config.ChocoboFoodStockTarget);
            Assert.Equal(7897, config.ChocoboFoodItemId);
            Assert.False(config.ChocoboAutoFeed);
            Assert.False(config.ChocoboAutoAllocateSkills);
        }
        Assert.True(legacy.ForceGysahl);
        Assert.Equal("Healer Stance", legacy.CompanionStrat);
        Assert.Equal(7.5f, legacy.Cling);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, -2)]
    [InlineData(int.MinValue, 0)]
    [InlineData(25, 5)]
    public void SerializationCloneAndDefaultMappingPreserveExactSavedPolicies(int greens, int food)
    {
        var source = new CharacterConfig
        {
            ChocoboGreensStockTarget = greens,
            ChocoboFoodStockTarget = food,
            ChocoboFoodItemId = 7894,
            ChocoboAutoAllocateSkills = true,
            ChocoboSkillPriority = [1, 2],
        };
        var restored = JsonSerializer.Deserialize<CharacterConfig>(JsonSerializer.Serialize(source))!;
        var clone = source.Clone();
        var account = CreateAccount();
        account.DefaultConfig = source;

        Assert.Equal(2, ConfigManager.ApplyDefaultTabToAllCharacters(account, "Chocobo"));
        foreach (var copied in new[] { restored, clone }.Concat(account.Characters.Values))
        {
            AssertPolicies(copied, greens, food);
            Assert.Equal(7894, copied.ChocoboFoodItemId);
            Assert.True(copied.ChocoboAutoAllocateSkills);
            Assert.Equal(new[] { 1, 2 }, copied.ChocoboSkillPriority);
            Assert.NotSame(source.ChocoboSkillPriority, copied.ChocoboSkillPriority);
        }
        clone.ChocoboGreensStockTarget = 99;
        AssertPolicies(source, greens, food);
    }

    [Theory]
    [InlineData("Chocobo")]
    [InlineData("all")]
    [InlineData("Gysahl Greens stock target")]
    [InlineData("Companion food stock target")]
    public void DefaultSyncRespectsSettingScopeAndLeavesRemoteProfilesUntouched(string scope)
    {
        var account = CreateAccount();
        var remote = new RemoteProfileRow
        {
            Config = new CharacterConfig
            {
                ChocoboGreensStockTarget = 80,
                ChocoboFoodStockTarget = 90,
            },
        };
        account.RemoteProfiles.Add(remote);

        var count = scope switch
        {
            "Chocobo" => ConfigManager.ApplyDefaultTabToAllCharacters(account, scope),
            "all" => ConfigManager.ApplyDefaultToAllCharacters(account),
            _ => ConfigManager.ApplyDefaultSettingToAllCharacters(account, scope),
        };

        Assert.Equal(2, count);
        foreach (var target in account.Characters.Values)
        {
            AssertPolicies(target,
                scope is "Chocobo" or "all" or "Gysahl Greens stock target" ? 20 : 2,
                scope is "Chocobo" or "all" or "Companion food stock target" ? 10 : 3);
            Assert.Equal(scope == "all", target.Enabled);
            Assert.Equal(scope == "all" ? 3f : 9f, target.Cling);
            if (scope is not ("Chocobo" or "all"))
            {
                Assert.False(target.ChocoboAutoFeed);
                Assert.Equal(7897, target.ChocoboFoodItemId);
                Assert.False(target.ForceGysahl);
                Assert.False(target.ChocoboAutoAllocateSkills);
                Assert.Equal(new[] { 2, 1, 0 }, target.ChocoboSkillPriority);
            }
        }
        AssertPolicies(remote.Config, 80, 90);
        AssertPolicies(account.DefaultConfig, 20, 10);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChocoboTabResetUsesSelectedDefaultsAndPreservesOtherTabs(bool pluginDefaults)
    {
        var source = pluginDefaults ? new CharacterConfig() : CreateAccount().DefaultConfig;
        var target = new CharacterConfig
        {
            ChocoboGreensStockTarget = -4,
            ChocoboFoodStockTarget = -5,
            ChocoboFoodItemId = 7895,
            Enabled = true,
            Cling = 8f,
        };
        var copyTab = typeof(ConfigManager).GetMethod("ApplyTabSettings", BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.True((bool)copyTab.Invoke(null, new object[] { source, target, "Chocobo" })!);
        Assert.Equal(source.ChocoboFoodItemId, target.ChocoboFoodItemId);
        AssertPolicies(target, source.ChocoboGreensStockTarget, source.ChocoboFoodStockTarget);
        Assert.True(target.Enabled);
        Assert.Equal(8f, target.Cling);
    }

    [Fact]
    public void RemoteAndDefaultPurchasePoliciesDoNotReplaceLocalActivePolicies()
    {
        var account = CreateAccount();
        account.RemoteProfiles.Add(new RemoteProfileRow
        {
            RowId = "remote-row",
            CharacterId = "remote-only",
            Config = new CharacterConfig
            {
                ChocoboGreensStockTarget = 80,
                ChocoboFoodStockTarget = 90,
            },
        });

        Assert.True(ConfigManager.TryResolveActiveConfig(account, account.AccountId, "local-one", out var active));
        Assert.Same(account.Characters["local-one"], active);
        AssertPolicies(active!, 2, 3);
        Assert.False(ConfigManager.TryResolveActiveConfig(account, account.AccountId, "", out _));
        Assert.False(ConfigManager.TryResolveActiveConfig(account, account.AccountId, "remote-only", out _));
    }

    private static void AssertPolicies(CharacterConfig config, int greens, int food)
    {
        Assert.Equal(greens, config.ChocoboGreensStockTarget);
        Assert.Equal(food, config.ChocoboFoodStockTarget);
    }

    private static AccountConfig CreateAccount() => new()
    {
        AccountId = "test-account",
        DefaultConfig = new CharacterConfig
        {
            ChocoboGreensStockTarget = 20,
            ChocoboFoodStockTarget = 10,
            ChocoboAutoFeed = true,
            ChocoboFoodItemId = 7900,
            ForceGysahl = true,
            ChocoboAutoAllocateSkills = true,
            ChocoboSkillPriority = [1, 2],
            Enabled = true,
            Cling = 3f,
        },
        Characters =
        {
            ["local-one"] = new CharacterConfig { ChocoboGreensStockTarget = 2, ChocoboFoodStockTarget = 3, Cling = 9f },
            ["local-two"] = new CharacterConfig { ChocoboGreensStockTarget = 2, ChocoboFoodStockTarget = 3, Cling = 9f },
        },
    };
}
