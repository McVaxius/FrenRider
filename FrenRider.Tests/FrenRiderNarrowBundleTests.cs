using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class FrenRiderNarrowBundleTests
{
    [Fact]
    public void NewCharacterConfigUsesSafeNarrowBundleDefaults()
    {
        var config = new CharacterConfig();

        Assert.Equal(0, config.RotationType);
        Assert.Equal(0, config.RsrAggroType);
        Assert.False(config.EquipJobStoneForCurrentClass);
    }

    [Fact]
    public void ClonePreservesRsrAggroAndJobStoneSettings()
    {
        var config = new CharacterConfig
        {
            RsrAggroType = 4,
            EquipJobStoneForCurrentClass = true,
        };

        var clone = config.Clone();

        Assert.Equal(4, clone.RsrAggroType);
        Assert.True(clone.EquipJobStoneForCurrentClass);
    }

    [Fact]
    public void DefaultSyncCopiesRsrAggroAndJobStoneSettingsByOwningTabs()
    {
        var account = new AccountConfig
        {
            DefaultConfig = new CharacterConfig
            {
                RsrAggroType = 3,
                EquipJobStoneForCurrentClass = true,
            },
            Characters = new Dictionary<string, CharacterConfig>
            {
                ["First@World"] = new(),
                ["Second@World"] = new(),
            },
        };

        Assert.Equal(2, ConfigManager.ApplyDefaultTabToAllCharacters(account, "Combat"));
        Assert.Equal(2, ConfigManager.ApplyDefaultTabToAllCharacters(account, "Automation"));

        Assert.All(account.Characters.Values, config =>
        {
            Assert.Equal(3, config.RsrAggroType);
            Assert.True(config.EquipJobStoneForCurrentClass);
        });
    }

    [Fact]
    public void LegacyPreviouslyEngagedOperatingModeMigratesOnce()
    {
        var account = new AccountConfig
        {
            DefaultConfig = new CharacterConfig { RotationType = 4 },
            Characters = new Dictionary<string, CharacterConfig>
            {
                ["Legacy@World"] = new() { RotationType = 4 },
                ["Support@World"] = new() { RotationType = 3, RsrAggroType = 4 },
            },
        };

        Assert.True(ConfigManager.MigrateLegacyRsrSettings(account));
        Assert.Equal(0, account.DefaultConfig.RotationType);
        Assert.Equal(1, account.DefaultConfig.RsrAggroType);
        Assert.Equal(0, account.Characters["Legacy@World"].RotationType);
        Assert.Equal(1, account.Characters["Legacy@World"].RsrAggroType);
        Assert.Equal(3, account.Characters["Support@World"].RotationType);
        Assert.Equal(4, account.Characters["Support@World"].RsrAggroType);
        Assert.False(ConfigManager.MigrateLegacyRsrSettings(account));
    }

    [Theory]
    [InlineData(0, AutorotIpcService.RsrTargetHostileType.AllTargetsCanAttack)]
    [InlineData(1, AutorotIpcService.RsrTargetHostileType.TargetsHaveTarget)]
    [InlineData(2, AutorotIpcService.RsrTargetHostileType.AllTargetsWhenSoloInDuty)]
    [InlineData(3, AutorotIpcService.RsrTargetHostileType.AllTargetsWhenSolo)]
    [InlineData(4, AutorotIpcService.RsrTargetHostileType.SoloDeepDungeonSmart)]
    public void RsrAggroChoicesMapToTypedIpcValues(
        int configuredValue,
        AutorotIpcService.RsrTargetHostileType expected)
    {
        Assert.Equal(expected, CombatService.ResolveRsrTargetHostileType(configuredValue));
    }

    [Fact]
    public void JobStonePolicyRequiresEverySafetyGate()
    {
        var allowed = new JobStonePolicyInput(
            FrenRiderEnabled: true,
            FeatureEnabled: true,
            IsBaseClass: true,
            Level: 30,
            CanChangeEquipment: true,
            HasValidCurrentGearset: true,
            SoulCrystalSlotEmpty: true);

        Assert.True(JobStoneEquipService.ShouldAttempt(allowed));
        Assert.False(JobStoneEquipService.ShouldAttempt(allowed with { FrenRiderEnabled = false }));
        Assert.False(JobStoneEquipService.ShouldAttempt(allowed with { FeatureEnabled = false }));
        Assert.False(JobStoneEquipService.ShouldAttempt(allowed with { IsBaseClass = false }));
        Assert.False(JobStoneEquipService.ShouldAttempt(allowed with { Level = 29 }));
        Assert.False(JobStoneEquipService.ShouldAttempt(allowed with { CanChangeEquipment = false }));
        Assert.False(JobStoneEquipService.ShouldAttempt(allowed with { HasValidCurrentGearset = false }));
        Assert.False(JobStoneEquipService.ShouldAttempt(allowed with { SoulCrystalSlotEmpty = false }));
    }

    [Fact]
    public void JobStoneSelectionUsesFirstMatchingArmourySlot()
    {
        var eligible = new HashSet<uint> { 100, 200 };
        var slots = new[]
        {
            new JobStoneSlot(0, 999),
            new JobStoneSlot(1, 200),
            new JobStoneSlot(2, 100),
        };

        var selected = JobStoneEquipService.SelectFirstMatchingStone(eligible, slots);

        Assert.Equal(new JobStoneSlot(1, 200), selected);
    }
}
