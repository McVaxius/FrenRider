using System.Text.Json;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class ConfigurationMigrationTests
{
    [Fact]
    public void NewAndUnsetProfilesUseGodModInBothContexts()
    {
        var config = new CharacterConfig();
        Assert.Equal(5, config.RotationPlugin);
        Assert.Equal(5, config.RotationPluginForay);
        var unset = JsonSerializer.Deserialize<CharacterConfig>("{}")!;
        Assert.Equal(5, unset.RotationPlugin);
        Assert.Equal(5, unset.RotationPluginForay);
        Assert.Equal(2, config.RsrAggroType);
        Assert.Equal(2, unset.RsrAggroType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewAccountsPreserveTheFirstUserProviderSelection(bool collector)
    {
        var accounts = new Dictionary<string, AccountConfig>();
        AccountConfig? account;
        string failure;
        var created = collector
            ? ConfigManager.TrySelectCollectorAccount(accounts, "Example", _ => true, out account, out failure)
            : ConfigManager.TrySelectLauncherAccount(accounts, "launcher-example", "Example", "Example", _ => true,
                _ => { }, out account, out failure);
        Assert.True(created, failure);
        account!.DefaultConfig.RotationPlugin = 2;
        account.DefaultConfig.RotationPluginForay = 3;
        account.DefaultConfig.RsrAggroType = 0;
        var reloaded = JsonSerializer.Deserialize<AccountConfig>(JsonSerializer.Serialize(account))!;
        Assert.False(ConfigManager.MigrateRotationDefaults(reloaded));
        Assert.Equal(2, reloaded.DefaultConfig.RotationPlugin);
        Assert.Equal(3, reloaded.DefaultConfig.RotationPluginForay);
        Assert.Equal(0, reloaded.DefaultConfig.RsrAggroType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void LegacyAccountMigratesOnlyOldLocalAggroDefaultsOnce(int version)
    {
        var account = JsonSerializer.Deserialize<AccountConfig>("""
            {"DefaultConfig":{"RotationPlugin":2,"RotationPluginForay":3,"RsrAggroType":0,"CustomIdleList":["/wave"],"Repair":3},
             "Characters":{"Example":{"RotationPlugin":1,"RotationPluginForay":4,"RsrAggroType":0,"Cling":7},
                           "Different":{"RsrAggroType":4}},
             "RemoteProfiles":[{"Config":{"RotationPlugin":3,"RotationPluginForay":2,"RsrAggroType":0,"TornClothes":42}}]}
            """)!;
        account.RotationDefaultsVersion = version;

        Assert.True(ConfigManager.MigrateRotationDefaults(account));
        Assert.Equal(AccountConfig.CurrentRotationDefaultsVersion, account.RotationDefaultsVersion);
        Assert.Equal(2, account.DefaultConfig.RsrAggroType);
        Assert.Equal(2, account.Characters["Example"].RsrAggroType);
        Assert.Equal(4, account.Characters["Different"].RsrAggroType);
        Assert.Equal(0, account.RemoteProfiles[0].Config.RsrAggroType);
        Assert.Equal(2, account.DefaultConfig.RotationPlugin);
        Assert.Equal(3, account.DefaultConfig.RotationPluginForay);
        Assert.Equal(1, account.Characters["Example"].RotationPlugin);
        Assert.Equal(4, account.Characters["Example"].RotationPluginForay);
        Assert.Equal(3, account.RemoteProfiles[0].Config.RotationPlugin);
        Assert.Equal(new[] { "/wave" }, account.DefaultConfig.CustomIdleList);
        Assert.Equal(3, account.DefaultConfig.Repair);
        Assert.Equal(7f, account.Characters["Example"].Cling);
        Assert.Equal(42, account.RemoteProfiles[0].Config.TornClothes);

        account.DefaultConfig.RsrAggroType = 0;
        account.Characters["Example"].RsrAggroType = 0;
        var reloaded = JsonSerializer.Deserialize<AccountConfig>(JsonSerializer.Serialize(account))!;
        Assert.False(ConfigManager.MigrateRotationDefaults(reloaded));
        Assert.Equal(0, reloaded.DefaultConfig.RsrAggroType);
        Assert.Equal(0, reloaded.Characters["Example"].RsrAggroType);
    }

    [Fact]
    public void LoadingAnExistingAccountPersistsAggroMigrationBeforeTheNextReload()
    {
        var directory = Directory.CreateTempSubdirectory("FrenRiderAggroMigrationTests-");
        try
        {
            var plugin = ActivationModeTests.Proxy<Dalamud.Plugin.IDalamudPluginInterface>(
                (method, _) => method.Name == "GetPluginConfigDirectory" ? directory.FullName : null);
            var log = ActivationModeTests.Proxy<Dalamud.Plugin.Services.IPluginLog>((_, _) => null);
            var path = Path.Combine(directory.FullName, "example_FrenRider.json");
            File.WriteAllText(path, """
                {"AccountId":"example","RotationDefaultsVersion":1,"DefaultConfig":{"RsrAggroType":0},
                 "Characters":{"Example":{"RsrAggroType":0}}}
                """);

            _ = new ConfigManager(plugin, log);
            var upgraded = JsonSerializer.Deserialize<AccountConfig>(File.ReadAllText(path))!;
            Assert.Equal(AccountConfig.CurrentRotationDefaultsVersion, upgraded.RotationDefaultsVersion);
            Assert.Equal(2, upgraded.DefaultConfig.RsrAggroType);
            Assert.Equal(2, upgraded.Characters["Example"].RsrAggroType);

            upgraded.Characters["Example"].RsrAggroType = 0;
            var saved = JsonSerializer.Serialize(upgraded);
            File.WriteAllText(path, saved);
            _ = new ConfigManager(plugin, log);
            Assert.Equal(saved, File.ReadAllText(path));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ExplicitProviderIdsRoundTripWithoutADefaultSweep(int provider)
    {
        var profile = JsonSerializer.Deserialize<CharacterConfig>(
            $"{{\"RotationPlugin\":{provider},\"RotationPluginForay\":{provider}}}")!;
        Assert.Equal(provider, profile.RotationPlugin);
        Assert.Equal(provider, profile.RotationPluginForay);
        Assert.Equal(provider, profile.Clone().RotationPlugin);
    }

    [Fact]
    public void NewConfigurationDefaultsToCastingMovementLockEnabled()
    {
        Assert.Equal(2, ConfigurationMigration.CurrentVersion);
        Assert.True(ConfigurationMigration.DefaultDontMoveWhileCasting);
    }

    [Fact]
    public void VersionOneConfigurationEnablesCastingMovementLockOnce()
    {
        var version = 1;
        var dontMoveWhileCasting = false;

        Assert.True(ConfigurationMigration.Apply(ref version, ref dontMoveWhileCasting));
        Assert.Equal(ConfigurationMigration.CurrentVersion, version);
        Assert.True(dontMoveWhileCasting);

        dontMoveWhileCasting = false;
        Assert.False(ConfigurationMigration.Apply(ref version, ref dontMoveWhileCasting));
        Assert.False(dontMoveWhileCasting);
    }

    [Fact]
    public void VersionTwoOptOutRemainsDisabled()
    {
        var version = 2;
        var dontMoveWhileCasting = false;

        Assert.False(ConfigurationMigration.Apply(ref version, ref dontMoveWhileCasting));
        Assert.Equal(ConfigurationMigration.CurrentVersion, version);
        Assert.False(dontMoveWhileCasting);
    }
}
