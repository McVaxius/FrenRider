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
    [InlineData(0, "VBM", "/vbmai on")]
    public void BossModAiOnUsesSelectedImplementation(int bossModAI, string pluginName, string command)
    {
        Assert.Equal(
            new[] { command },
            CombatService.BuildBossModAiCommands(bossModAI, pluginName));
    }
}
