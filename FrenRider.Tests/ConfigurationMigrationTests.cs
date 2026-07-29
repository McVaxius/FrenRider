namespace FrenRider.Tests;

public sealed class ConfigurationMigrationTests
{
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
