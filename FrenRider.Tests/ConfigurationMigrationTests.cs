using System.Text.Json;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class ConfigurationMigrationTests
{
    [Fact]
    public void NewProfilesUseBmrInBothContexts()
    {
        var config = new CharacterConfig();
        Assert.Equal(0, config.RotationPlugin);
        Assert.Equal(0, config.RotationPluginForay);
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
        var reloaded = JsonSerializer.Deserialize<AccountConfig>(JsonSerializer.Serialize(account))!;
        Assert.False(ConfigManager.MigrateRotationDefaults(reloaded));
        Assert.Equal(2, reloaded.DefaultConfig.RotationPlugin);
        Assert.Equal(3, reloaded.DefaultConfig.RotationPluginForay);
    }

    [Fact]
    public void LegacyAccountMigratesEverySavedProfileOnceAndRetainsLaterChoices()
    {
        var account = JsonSerializer.Deserialize<AccountConfig>("""
            {"DefaultConfig":{"RotationPlugin":2,"RotationPluginForay":3,"CustomIdleList":["/wave"],"Repair":3},
             "Characters":{"Example":{"RotationPlugin":1,"RotationPluginForay":4,"Cling":7}},
             "RemoteProfiles":[{"Config":{"RotationPlugin":3,"RotationPluginForay":2,"TornClothes":42}}]}
            """)!;

        Assert.True(ConfigManager.MigrateRotationDefaults(account));
        foreach (var config in new[] { account.DefaultConfig, account.Characters["Example"], account.RemoteProfiles[0].Config })
        {
            Assert.Equal(0, config.RotationPlugin);
            Assert.Equal(0, config.RotationPluginForay);
        }
        Assert.Equal(new[] { "/wave" }, account.DefaultConfig.CustomIdleList);
        Assert.Equal(3, account.DefaultConfig.Repair);
        Assert.Equal(7f, account.Characters["Example"].Cling);
        Assert.Equal(42, account.RemoteProfiles[0].Config.TornClothes);

        account.DefaultConfig.RotationPlugin = 2;
        account.Characters["Example"].RotationPluginForay = 3;
        account.RemoteProfiles[0].Config.RotationPlugin = 4;
        var reloaded = JsonSerializer.Deserialize<AccountConfig>(JsonSerializer.Serialize(account))!;
        Assert.False(ConfigManager.MigrateRotationDefaults(reloaded));
        Assert.Equal(2, reloaded.DefaultConfig.RotationPlugin);
        Assert.Equal(3, reloaded.Characters["Example"].RotationPluginForay);
        Assert.Equal(4, reloaded.RemoteProfiles[0].Config.RotationPlugin);
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
