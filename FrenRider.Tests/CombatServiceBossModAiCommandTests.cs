using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class CombatServiceBossModAiCommandTests
{
    [Fact]
    public void BossModAiOffTurnsBothImplementationsOff()
    {
        Assert.Equal(
            new[] { "/bmrai off", "/vbmai off" },
            CombatService.BuildBossModAiCommands(1, "BMR"));
    }

    [Theory]
    [InlineData(0, "BMR", "/bmrai on")]
    [InlineData(99, "RSR", "/bmrai on")]
    [InlineData(0, "WRATH", "/bmrai on")]
    [InlineData(0, "DAEDALUS", "/bmrai on")]
    [InlineData(0, "VBM", "/vbmai on")]
    public void BossModAiOnUsesSelectedImplementation(int bossModAI, string pluginName, string command)
    {
        Assert.Equal(
            new[] { command },
            CombatService.BuildBossModAiCommands(bossModAI, pluginName));
    }

    [Theory]
    [InlineData("BMR", "FRENRIDER - TANK", "/bmrai setpresetname FRENRIDER - TANK")]
    [InlineData("RSR", "passive - ranged", "/bmrai setpresetname passive - ranged")]
    [InlineData("WRATH", "passive - melee", "/bmrai setpresetname passive - melee")]
    [InlineData("DAEDALUS", "passive - tank", "/bmrai setpresetname passive - tank")]
    [InlineData("VBM", "FRENRIDER - RANGED", "/vbm ar set FRENRIDER - RANGED")]
    public void PresetPushTargetsOnlySelectedBossModProvider(
        string pluginName,
        string presetName,
        string expectedCommand)
    {
        Assert.Equal(
            new[] { expectedCommand },
            CombatService.BuildBossModPresetCommands(pluginName, presetName));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("none")]
    [InlineData("NONE")]
    public void PresetPushSkipsDisabledPresetNames(string presetName)
    {
        Assert.Empty(CombatService.BuildBossModPresetCommands("VBM", presetName));
        Assert.Empty(CombatService.BuildBossModPresetCommands("DAEDALUS", presetName));
    }

    [Theory]
    [InlineData(0, "BMR")]
    [InlineData(1, "VBM")]
    [InlineData(2, "RSR")]
    [InlineData(3, "WRATH")]
    [InlineData(4, "DAEDALUS")]
    [InlineData(-1, "RSR")]
    [InlineData(5, "RSR")]
    public void RotationPluginIndexesRemainCompatible(int pluginIndex, string pluginName)
    {
        Assert.Equal(pluginName, CombatService.ResolveRotationPluginName(pluginIndex));
    }

    [Fact]
    public void DaedalusUsesReviewedTypedIpcChannels()
    {
        Assert.Equal("Daedalus.IsEnabled", AutorotIpcService.DaedalusIsEnabledChannel);
        Assert.Equal("Daedalus.SetEnabled", AutorotIpcService.DaedalusSetEnabledChannel);
    }

    [Fact]
    public void QuestionableSoloInitialShutdownStopsEveryCombatEngine()
    {
        Assert.Equal(
            new[] { "/bmrai off", "/vbmai off", "/rotation cancel", "/wrath auto off" },
            CombatService.BuildQuestionableDutyCombatOffCommands());
    }

    [Fact]
    public void QuestionableSoloInitialShutdownOmitsRsrFallbackWhenIpcHandledIt()
    {
        Assert.Equal(
            new[] { "/bmrai off", "/vbmai off", "/wrath auto off" },
            CombatService.BuildQuestionableDutyCombatOffCommands(includeRsrFallback: false));
    }

    [Fact]
    public void AdsOwnedDungeonBootstrapUsesConfiguredRsrAutoMode()
    {
        Assert.Equal(
            AutorotIpcService.RsrStateCommandType.Auto,
            CombatService.ResolveRsrStateCommandType(rotationType: 0));
    }

    [Fact]
    public void RotationTypeNoneDoesNotActivateDuringDutyBootstrap()
    {
        Assert.False(CombatService.ShouldActivateConfiguredRotation(rotationType: 2));
        Assert.True(CombatService.ShouldActivateConfiguredRotation(rotationType: 0));
    }
}
