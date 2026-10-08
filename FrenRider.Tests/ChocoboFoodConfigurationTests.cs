using System.Text.Json;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class ChocoboFoodConfigurationTests
{
    [Fact]
    public void NewAndLegacyProfilesKeepFeedingOffAndDefaultToMimett()
    {
        var fresh = new CharacterConfig();
        var legacy = JsonSerializer.Deserialize<CharacterConfig>(
            """{"ForceGysahl":true,"CompanionStrat":"Healer Stance","Cling":7.5}""")!;

        Assert.False(fresh.ChocoboAutoFeed);
        Assert.Equal(7897, fresh.ChocoboFoodItemId);
        Assert.False(legacy.ChocoboAutoFeed);
        Assert.Equal(7897, legacy.ChocoboFoodItemId);
        Assert.True(legacy.ForceGysahl);
        Assert.Equal("Healer Stance", legacy.CompanionStrat);
        Assert.Equal(7.5f, legacy.Cling);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7894)]
    [InlineData(7895)]
    [InlineData(7897)]
    [InlineData(7898)]
    [InlineData(7900)]
    [InlineData(-1)]
    [InlineData(8166)]
    [InlineData(999999)]
    public void RoundTripAndClonePreserveExactSavedFoodIncludingUnsupportedValues(int itemId)
    {
        var source = new CharacterConfig
        {
            ChocoboAutoFeed = true,
            ChocoboFoodItemId = itemId,
            ForceGysahl = true,
            CompanionStrat = "Attacker Stance",
        };

        var restored = JsonSerializer.Deserialize<CharacterConfig>(JsonSerializer.Serialize(source))!;
        var clone = source.Clone();
        foreach (var copied in new[] { restored, clone })
        {
            Assert.True(copied.ChocoboAutoFeed);
            Assert.Equal(itemId, copied.ChocoboFoodItemId);
            Assert.True(copied.ForceGysahl);
            Assert.Equal(source.CompanionStrat, copied.CompanionStrat);
        }
    }

    [Theory]
    [InlineData("Chocobo")]
    [InlineData("all")]
    [InlineData("Companion food")]
    [InlineData("Automatically feed Chocobo")]
    public void DefaultSyncCopiesFoodSettingsOnlyToLocalProfiles(string scope)
    {
        var account = CreateAccount();
        var remote = new RemoteProfileRow
        {
            Config = new CharacterConfig { ChocoboFoodItemId = 7900 },
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
            Assert.Equal(scope != "Companion food", target.ChocoboAutoFeed);
            Assert.Equal(scope == "Automatically feed Chocobo" ? 7894 : 8166, target.ChocoboFoodItemId);
            if (scope != "all")
            {
                Assert.False(target.Enabled);
                Assert.Equal(9f, target.Cling);
            }
            if (scope is "Companion food" or "Automatically feed Chocobo")
            {
                Assert.False(target.ForceGysahl);
                Assert.Equal("Free Stance", target.CompanionStrat);
                Assert.False(target.ChocoboAutoAllocateSkills);
                Assert.Equal(new[] { 2, 1, 0 }, target.ChocoboSkillPriority);
            }
        }
        Assert.False(remote.Config.ChocoboAutoFeed);
        Assert.Equal(7900, remote.Config.ChocoboFoodItemId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(8166)]
    [InlineData(999999)]
    public void ChocoboTabDefaultMappingPreservesUnsetAndUnsupportedFood(int itemId)
    {
        var account = CreateAccount();
        account.DefaultConfig.ChocoboFoodItemId = itemId;

        Assert.Equal(2, ConfigManager.ApplyDefaultTabToAllCharacters(account, "Chocobo"));
        Assert.All(account.Characters.Values, target => Assert.Equal(itemId, target.ChocoboFoodItemId));
    }

    [Fact]
    public void FreshDefaultChocoboMappingResetsFoodWithoutChangingOtherTabs()
    {
        var account = CreateAccount();
        account.DefaultConfig = new CharacterConfig();
        foreach (var target in account.Characters.Values)
        {
            target.ChocoboAutoFeed = true;
            target.ChocoboFoodItemId = 7895;
        }

        Assert.Equal(2, ConfigManager.ApplyDefaultTabToAllCharacters(account, "Chocobo"));
        Assert.All(account.Characters.Values, target =>
        {
            Assert.False(target.ChocoboAutoFeed);
            Assert.Equal(7897, target.ChocoboFoodItemId);
            Assert.False(target.Enabled);
            Assert.Equal(9f, target.Cling);
        });
    }

    private static AccountConfig CreateAccount() => new()
    {
        AccountId = "test-account",
        DefaultConfig = new CharacterConfig
        {
            ChocoboAutoFeed = true,
            ChocoboFoodItemId = 8166,
            ForceGysahl = true,
            CompanionStrat = "Healer Stance",
            ChocoboAutoAllocateSkills = true,
            ChocoboSkillPriority = [1, 2],
            Enabled = true,
            Cling = 3f,
        },
        Characters =
        {
            ["local-one"] = new CharacterConfig { ChocoboFoodItemId = 7894, Cling = 9f },
            ["local-two"] = new CharacterConfig { ChocoboFoodItemId = 7894, Cling = 9f },
        },
    };
}
