using FrenRider.Services;
using FrenRider.Models;
using System.Reflection;
using System.Reflection.Emit;

namespace FrenRider.Tests;

public sealed class CombatServiceBossModAiCommandTests
{
    [Theory]
    [InlineData(ZoneType.Overworld, false, false, "general")]
    [InlineData(ZoneType.Overworld, true, false, "fate")]
    [InlineData(ZoneType.Overworld, true, true, "general")]
    [InlineData(ZoneType.DeepDungeon, false, false, "deep dungeon")]
    [InlineData(ZoneType.DeepDungeon, true, false, "fate")]
    [InlineData(ZoneType.DeepDungeon, true, true, "deep dungeon")]
    public void IgnoreFatesUsesOrdinaryZonePresetWithoutChangingSavedChoices(
        ZoneType zone, bool inFate, bool ignoreFates, string expected)
    {
        var config = new CharacterConfig
        {
            AutoRotationType = "general",
            AutoRotationTypeDD = "deep dungeon",
            AutoRotationTypeFATE = "fate",
            IgnoreFates = ignoreFates,
            AutoSyncFate = true,
        };

        Assert.Equal(expected, CombatService.SelectManualPresetForZone(config, zone, inFate));
        Assert.Equal("general", config.AutoRotationType);
        Assert.Equal("deep dungeon", config.AutoRotationTypeDD);
        Assert.Equal("fate", config.AutoRotationTypeFATE);
        Assert.True(config.AutoSyncFate);
    }

    [Fact]
    public void BossModAiOffTurnsBothImplementationsOff()
    {
        Assert.Equal(
            new[] { "/bmrai off", "/vbmai off" },
            CombatService.BuildBossModAiCommands(1, "BMR"));
    }

    [Theory]
    [InlineData(0, "BMR", new[] { "/bmrai forbidactions off", "/bmrai on" })]
    [InlineData(99, "RSR", new[] { "/bmrai forbidactions off", "/bmrai on" })]
    [InlineData(0, "WRATH", new[] { "/bmrai forbidactions off", "/bmrai on" })]
    [InlineData(0, "DAEDALUS", new[] { "/bmrai forbidactions off", "/bmrai on" })]
    [InlineData(0, "VBM", new[] { "/vbmai on" })]
    public void BossModAiOnUsesSelectedImplementation(int bossModAI, string pluginName, string[] commands)
    {
        Assert.Equal(
            commands,
            CombatService.BuildBossModAiCommands(bossModAI, pluginName));
    }

    [Theory]
    [InlineData("VBM", new string[] { })]
    [InlineData("BMR", new[] { "/bmrai forbidactions off", "/bmrai on" })]
    [InlineData("RSR", new[] { "/bmrai forbidactions off", "/bmrai on" })]
    [InlineData("WRATH", new[] { "/bmrai forbidactions off", "/bmrai on" })]
    [InlineData("DAEDALUS", new[] { "/bmrai forbidactions off", "/bmrai on" })]
    public void AdsInteractionPauseDefersOnlyVbmOn(string provider, string[] expected)
        => Assert.Equal(expected, CombatService.BuildBossModAiCommands(0, provider,
            interactionVbmPauseActive: true));

    [Theory]
    [InlineData("VBM")]
    [InlineData("BMR")]
    public void AdsInteractionPausePreservesExplicitAiOff(string provider)
        => Assert.Equal(new[] { "/bmrai off", "/vbmai off" },
            CombatService.BuildBossModAiCommands(1, provider, interactionVbmPauseActive: true));

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
    public void AdsHyperFocusStopsOtherProvidersAndFallsBackToRsrManual()
    {
        Assert.Equal(
            new[] { "/bmrai off", "/vbmai off", "/wrath auto off", "/rotation manual" },
            CombatService.BuildAdsHyperFocusCombatCommands());
    }

    [Fact]
    public void AdsHyperFocusOmitsRsrFallbackWhenTypedIpcHandledManualMode()
    {
        Assert.Equal(
            new[] { "/bmrai off", "/vbmai off", "/wrath auto off" },
            CombatService.BuildAdsHyperFocusCombatCommands(includeRsrFallback: false));
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

    [Theory]
    [InlineData("BMR")]
    [InlineData("VBM")]
    [InlineData("RSR")]
    [InlineData("WRATH")]
    [InlineData("DAEDALUS")]
    public void ManualBossModChoicesApplyToEveryRotationProviderWithoutTheLegacyForceFlag(string provider)
    {
        var config = new CharacterConfig
        {
            ConfigureRotationPresetManually = true,
            ForceBossModPresetRegardlessOfRotation = false,
            AutoRotationType = "null | exact name",
            AutoRotationTypeDD = "Deep Dungeon",
            AutoRotationTypeFATE = "FATE",
        };
        Assert.Equal("null | exact name", CombatService.SelectBossModPresetForProvider(config, provider, ZoneType.Overworld, false, "TANK"));
        Assert.Equal("Deep Dungeon", CombatService.SelectBossModPresetForProvider(config, provider, ZoneType.DeepDungeon, false, "TANK"));
        Assert.Equal("FATE", CombatService.SelectBossModPresetForProvider(config, provider, ZoneType.DeepDungeon, true, "TANK"));
        Assert.False(config.ForceBossModPresetRegardlessOfRotation);
    }

    [Fact]
    public void CatalogUsesAllNativeDefinitionsInOrderAndFiltersOnlyNativeUnavailableChoices()
    {
        var defaults = new[] { new NativePreset("Movement", true), new NativePreset("VBM Multibox", true), new NativePreset("none") };
        var users = new[] { new NativePreset("null"), new NativePreset(" Exact literal name ") };
        var all = defaults.Concat(users).ToArray();
        var bmr = AutorotIpcService.ReadPresetCatalog("BMR", all, defaults, users);
        var vbm = AutorotIpcService.ReadPresetCatalog("VBM", all, defaults, users);
        Assert.True(bmr.Readable);
        Assert.Equal(new[] { "Movement", "VBM Multibox", "null", " Exact literal name " }, bmr.DisplayedNames);
        Assert.Equal(new[] { "null", " Exact literal name " }, vbm.DisplayedNames);
        Assert.Equal("Movement", AutorotIpcService.ResolvePresetSelection("Movement", vbm));
        Assert.Equal("null", AutorotIpcService.ResolvePresetSelection("Deleted preset", vbm));
    }

    [Fact]
    public void NativeLookupCollisionsKeepOnlyTheExactlyReachableName()
    {
        var definitions = new[] { new NativePreset("Alpha"), new NativePreset("alpha"), new NativePreset(" Alpha "), new NativePreset("Beta") };
        var catalog = AutorotIpcService.ReadPresetCatalog("BMR", definitions, definitions, Array.Empty<NativePreset>());
        Assert.Equal(new[] { "Alpha", "Beta" }, catalog.DisplayedNames);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("none")]
    [InlineData("NONE")]
    public void ExplicitNoneIsPreservedEvenWhenNativeEntriesExist(string choice)
    {
        var presets = new[] { new NativePreset("First") };
        var catalog = AutorotIpcService.ReadPresetCatalog("BMR", presets, presets, Array.Empty<NativePreset>());
        Assert.Equal(choice, AutorotIpcService.ResolvePresetSelection(choice, catalog));
    }

    [Fact]
    public void UnreadableAndEmptyCatalogsDoNotProveDeletion()
    {
        var unavailable = AutorotIpcService.ReadPresetCatalog("BMR", new[] { new object() }, Array.Empty<object>(), Array.Empty<object>());
        var empty = AutorotIpcService.ReadPresetCatalog("VBM", Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>());
        Assert.False(unavailable.Readable);
        Assert.True(empty.Readable);
        Assert.Equal("Saved", AutorotIpcService.ResolvePresetSelection("Saved", unavailable));
        Assert.Equal("Saved", AutorotIpcService.ResolvePresetSelection("Saved", empty));
    }

    [Fact]
    public void MissingActiveSelectorReplacementPreservesDormantProfileFields()
    {
        var config = new CharacterConfig { AutoRotationType = "General", AutoRotationTypeDD = "Deleted", AutoRotationTypeFATE = "FATE" };
        var presets = new[] { new NativePreset("First") };
        var catalog = AutorotIpcService.ReadPresetCatalog("VBM", presets, presets, Array.Empty<NativePreset>());
        var selector = CombatService.GetManualPresetSelector(config, ZoneType.DeepDungeon, false);
        CombatService.WriteManualPresetSelector(config, selector, AutorotIpcService.ResolvePresetSelection(CombatService.ReadManualPresetSelector(config, selector), catalog));
        Assert.Equal("General", config.AutoRotationType);
        Assert.Equal("First", config.AutoRotationTypeDD);
        Assert.Equal("FATE", config.AutoRotationTypeFATE);
    }

    [Fact]
    public void LiveCatalogRoutesUseTheExistingProviderAndNeverStartAHostedService()
    {
        var first = new object();
        var second = new object();
        Assert.Same(first, AutorotIpcService.ReadRotationDatabase("BMR", new NativePlugin(first), typeof(NativePlugin).Assembly));
        Assert.Same(second, AutorotIpcService.ReadRotationDatabase("BMR", new NativePlugin(second), typeof(NativePlugin).Assembly));

        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("FrenRiderCatalogRouteTest"), AssemblyBuilderAccess.Run);
        var typeBuilder = assembly.DefineDynamicModule("NativeTypes").DefineType("BossMod.Services.TickService", TypeAttributes.Public);
        typeBuilder.DefineField("_rotationDB", typeof(object), FieldAttributes.Public);
        var tickType = typeBuilder.CreateType()!;
        var tick = Activator.CreateInstance(tickType)!;
        tickType.GetField("_rotationDB")!.SetValue(tick, first);
        var services = new NativeServices(tick);
        var hosted = new NativeHostedPlugin(new NativeHost(services));
        Assert.Same(first, AutorotIpcService.ReadRotationDatabase("VBM", hosted, assembly));
        Assert.Equal(tickType, services.RequestedType);
        services.Service = new object();
        Assert.Null(AutorotIpcService.ReadRotationDatabase("VBM", hosted, assembly));
        Assert.Null(AutorotIpcService.ReadRotationDatabase("VBM", hosted, typeof(NativeHostedPlugin).Assembly));
    }

    [Fact]
    public void SettingsOnlyChangesDoNotRequireEngineActivation()
    {
        var profile = new CharacterConfig();
        var before = new CombatSettingsSnapshot(profile, "RSR", "passive - ranged", 0, 0, 1, 3, DaedalusTargetMode.None);
        Assert.False(CombatService.RequiresCombatActivation(before, before with { RsrAggroType = 0 }));
        Assert.False(CombatService.RequiresCombatActivation(before, before with { Preset = "Custom" }));
        Assert.False(CombatService.RequiresCombatActivation(before, before with { Positional = 1 }));
        Assert.False(CombatService.RequiresCombatActivation(before, before with { BossModAI = 1 }));
        Assert.False(CombatService.RequiresCombatActivation(before, before));
        Assert.True(CombatService.RequiresCombatActivation(before, before with { Profile = new CharacterConfig() }));
        Assert.True(CombatService.RequiresCombatActivation(before, before with { Provider = "VBM" }));
        Assert.True(CombatService.RequiresCombatActivation(before, before with { RotationType = 1 }));
    }

    private sealed record NativePreset(string Name, bool HiddenByDefault = false);
    private sealed record NativePlugin(object _rotationDB);
    private sealed record NativeHost(IServiceProvider Services);
    private sealed record NativeHostedPlugin(NativeHost Host);
    private sealed class NativeServices(object service) : IServiceProvider
    {
        internal object Service = service;
        internal Type? RequestedType;
        public object? GetService(Type serviceType) { RequestedType = serviceType; return Service; }
    }
}
