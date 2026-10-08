using System.Reflection;
using System.Text.Json;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class ChocoboSkillConfigurationTests
{
    [Fact]
    public void NewAndLegacyProfilesKeepSkillAutomationOffAndHealerAsTheOnlyPriority()
    {
        var fresh = new CharacterConfig();
        var legacy = JsonSerializer.Deserialize<CharacterConfig>(
            """{"ForceGysahl":true,"CompanionStrat":"Attacker Stance","Cling":7.5}""")!;

        Assert.False(fresh.ChocoboAutoAllocateSkills);
        Assert.False(legacy.ChocoboAutoAllocateSkills);
        Assert.Equal(new[] { 2 }, fresh.ChocoboSkillPriority);
        Assert.Equal(new[] { 2 }, legacy.ChocoboSkillPriority);
        Assert.NotSame(fresh.ChocoboSkillPriority, legacy.ChocoboSkillPriority);
        Assert.True(legacy.ForceGysahl);
        Assert.Equal("Attacker Stance", legacy.CompanionStrat);
        Assert.Equal(7.5f, legacy.Cling);
    }

    public static IEnumerable<object?[]> SavedPriorityCases()
    {
        for (var first = 0; first < 3; first++)
        {
            yield return [new List<int> { first }];
            for (var second = 0; second < 3; second++)
            {
                if (second == first) continue;
                yield return [new List<int> { first, second }];
                yield return [new List<int> { first, second, 3 - first - second }];
            }
        }
        yield return [null];
        yield return [new List<int>()];
        yield return [new List<int> { 2, 2 }];
        yield return [new List<int> { -1, 2 }];
        yield return [new List<int> { 99 }];
        yield return [new List<int> { 2, 0, 1, 2 }];
    }

    [Theory]
    [MemberData(nameof(SavedPriorityCases))]
    public void SavedPriorityRoundTripAndClonePreserveExactChoicesIncludingMalformedValues(List<int>? priority)
    {
        var source = new CharacterConfig
        {
            ChocoboAutoAllocateSkills = true,
            ChocoboSkillPriority = priority,
            ForceGysahl = false,
            CompanionStrat = "Healer Stance",
        };

        var restored = JsonSerializer.Deserialize<CharacterConfig>(JsonSerializer.Serialize(source))!;
        var clone = source.Clone();
        foreach (var copied in new[] { restored, clone })
        {
            Assert.True(copied.ChocoboAutoAllocateSkills);
            Assert.Equal(priority, copied.ChocoboSkillPriority);
            Assert.False(copied.ForceGysahl);
            Assert.Equal(source.CompanionStrat, copied.CompanionStrat);
            if (priority != null) Assert.NotSame(priority, copied.ChocoboSkillPriority);
        }
        if (clone.ChocoboSkillPriority != null)
        {
            clone.ChocoboSkillPriority.Add(98);
            Assert.DoesNotContain(98, source.ChocoboSkillPriority!);
            Assert.DoesNotContain(98, restored.ChocoboSkillPriority!);
        }
    }

    [Theory]
    [InlineData("Chocobo")]
    [InlineData("all")]
    [InlineData("Skill priority")]
    public void DefaultSyncCopiesIndependentPrioritiesToLocalProfilesOnly(string scope)
    {
        var account = CreateAccount();
        var remote = new RemoteProfileRow
        {
            Config = new CharacterConfig { ChocoboSkillPriority = [0] },
        };
        account.RemoteProfiles.Add(remote);

        var count = scope switch
        {
            "Chocobo" => ConfigManager.ApplyDefaultTabToAllCharacters(account, scope),
            "all" => ConfigManager.ApplyDefaultToAllCharacters(account),
            _ => ConfigManager.ApplyDefaultSettingToAllCharacters(account, scope),
        };

        Assert.Equal(2, count);
        var targets = account.Characters.Values.ToArray();
        foreach (var target in targets)
        {
            Assert.Equal(account.DefaultConfig.ChocoboSkillPriority, target.ChocoboSkillPriority);
            Assert.NotSame(account.DefaultConfig.ChocoboSkillPriority, target.ChocoboSkillPriority);
            Assert.Equal(scope != "Skill priority", target.ChocoboAutoAllocateSkills);
            if (scope != "all")
            {
                Assert.False(target.Enabled);
                Assert.Equal(9f, target.Cling);
            }
        }
        Assert.NotSame(targets[0].ChocoboSkillPriority, targets[1].ChocoboSkillPriority);
        targets[0].ChocoboSkillPriority!.Clear();
        Assert.Equal(new[] { 1, 2 }, targets[1].ChocoboSkillPriority);
        Assert.Equal(new[] { 1, 2 }, account.DefaultConfig.ChocoboSkillPriority);
        Assert.False(remote.Config.ChocoboAutoAllocateSkills);
        Assert.Equal(new[] { 0 }, remote.Config.ChocoboSkillPriority);
    }

    [Fact]
    public void AutoAllocationSettingSyncPreservesPriorityAndOtherChocoboSettings()
    {
        var account = CreateAccount();

        Assert.Equal(2, ConfigManager.ApplyDefaultSettingToAllCharacters(account, "Automatically allocate Chocobo skills"));
        Assert.All(account.Characters.Values, target =>
        {
            Assert.True(target.ChocoboAutoAllocateSkills);
            Assert.Equal(new[] { 2 }, target.ChocoboSkillPriority);
            Assert.False(target.ForceGysahl);
            Assert.Equal("Free Stance", target.CompanionStrat);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChocoboTabResetCopiesTheSelectedDefaultsWithoutSharingPriority(bool pluginDefaults)
    {
        var source = pluginDefaults ? new CharacterConfig() : CreateAccount().DefaultConfig;
        var target = new CharacterConfig
        {
            ChocoboAutoAllocateSkills = !source.ChocoboAutoAllocateSkills,
            ChocoboSkillPriority = [0],
            Cling = 8f,
            Enabled = true,
        };
        var copyTab = typeof(ConfigManager).GetMethod("ApplyTabSettings", BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.True((bool)copyTab.Invoke(null, new object[] { source, target, "Chocobo" })!);
        Assert.Equal(source.ChocoboAutoAllocateSkills, target.ChocoboAutoAllocateSkills);
        Assert.Equal(source.ChocoboSkillPriority, target.ChocoboSkillPriority);
        Assert.NotSame(source.ChocoboSkillPriority, target.ChocoboSkillPriority);
        Assert.Equal(8f, target.Cling);
        Assert.True(target.Enabled);
    }

    [Fact]
    public void NullPriorityRemainsNullThroughDefaultSyncAndClone()
    {
        var account = CreateAccount();
        account.DefaultConfig.ChocoboSkillPriority = null;

        Assert.Equal(2, ConfigManager.ApplyDefaultTabToAllCharacters(account, "Chocobo"));
        Assert.All(account.Characters.Values, target => Assert.Null(target.ChocoboSkillPriority));
        Assert.Null(account.DefaultConfig.Clone().ChocoboSkillPriority);
    }

    private static AccountConfig CreateAccount() => new()
    {
        AccountId = "test-account",
        DefaultConfig = new CharacterConfig
        {
            ChocoboAutoAllocateSkills = true,
            ChocoboSkillPriority = [1, 2],
            ForceGysahl = true,
            CompanionStrat = "Healer Stance",
            Enabled = true,
            Cling = 3f,
        },
        Characters =
        {
            ["local-one"] = new CharacterConfig { Cling = 9f },
            ["local-two"] = new CharacterConfig { Cling = 9f },
        },
    };
}
