using FrenRider.Models;
using FrenRider.Services;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FrenRider.Tests;

public sealed class ExternalAutomationCleanupServiceTests
{
    [Theory]
    [InlineData("BMR", false, "Original")]
    [InlineData("BMR", false, "")]
    [InlineData("BMR", true, "")]
    [InlineData("VBM", false, "First|Second")]
    [InlineData("VBM", true, "")]
    public void OwnedPresetRestorationRetainsDistinctOriginalRuntimeStates(string provider, bool disabled, string names)
    {
        var original = Settings(names, disabled: disabled);
        var ownership = new BossModSettingsOwnership(provider, original);
        ownership.OwnRuntime(Settings("Owned").Runtime!);
        Assert.True(ownership.CanChangeRuntime(Settings("Owned")));
        Assert.True(BossModRuntimePresetState.Matches(original.Runtime, ownership.GetRuntimeCleanupTarget(Settings("Owned"), false)));
        var off = ownership.GetRuntimeCleanupTarget(Settings("Owned"), true)!;
        Assert.True(off.ForceDisabled);
        Assert.Empty(off.Names);
    }

    [Fact]
    public void RepeatedOwnChangesKeepTheFirstBaselineAndTreatFieldsIndependently()
    {
        var original = Settings("Original", selector: "Saved original", distance: 7.5);
        var ownership = new BossModSettingsOwnership("BMR", original);
        ownership.OwnRuntime(Settings("First").Runtime!);
        ownership.OwnStoredSelector("First");
        ownership.OwnDistance(1.5);
        var current = Settings("First", selector: "First", distance: 1.5);
        Assert.True(ownership.CanChangeRuntime(current));
        Assert.True(ownership.CanChangeStoredSelector(current));
        Assert.True(ownership.CanChangeDistance(current.PreferredDistance));
        ownership.OwnRuntime(Settings("Second").Runtime!);
        ownership.OwnStoredSelector("Second");
        ownership.OwnDistance(2.5);
        Assert.Same(original, ownership.Original);
        Assert.Equal("Saved original", ownership.Original.StoredAiSelector);
        Assert.Equal(7.5, ownership.Original.PreferredDistance);
        Assert.False(ownership.CanChangeRuntime(Settings("External", selector: "Second", distance: 2.5)));
        Assert.True(ownership.CanChangeStoredSelector(Settings("External", selector: "Second", distance: 2.5)));
        Assert.False(ownership.CanChangeStoredSelector(Settings("Second", selector: "External", distance: 2.5)));
        Assert.False(ownership.CanChangeDistance(3.5));
    }

    [Fact]
    public void ANewerExternalRuntimeIsPreservedInsteadOfTheSessionOriginal()
    {
        var ownership = new BossModSettingsOwnership("VBM", Settings("Original|Other original"));
        ownership.OwnRuntime(Settings("Owned").Runtime!);
        var external = Settings("External|Other external");
        Assert.Same(external.Runtime, ownership.GetRuntimeCleanupTarget(external, false));
        Assert.False(ownership.MatchesOwnedRuntime(external));
        Assert.False(ownership.MatchesOwnedRuntime(Settings("Other original|Original")));
    }

    [Fact]
    public void UnreadableOriginalsAndCurrentFieldsNeverAuthorizeMutation()
    {
        var unavailable = new BossModSettingsOwnership("BMR", BossModSettingsSnapshot.Unavailable);
        var current = Settings("Current");
        Assert.False(unavailable.CanChangeRuntime(current));
        Assert.False(unavailable.CanChangeStoredSelector(current));
        Assert.False(unavailable.CanChangeDistance(1.5));
        var ownership = new BossModSettingsOwnership("BMR", current);
        Assert.False(ownership.CanChangeRuntime(BossModSettingsSnapshot.Unavailable));
        Assert.False(ownership.CanChangeStoredSelector(BossModSettingsSnapshot.Unavailable));
        Assert.False(ownership.CanChangeDistance(null));
        Assert.Null(ownership.GetRuntimeCleanupTarget(BossModSettingsSnapshot.Unavailable, false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnlyConfirmedBmrAiCommandsCanOwnTheirImmediateRuntimeClear(bool enabled)
    {
        var original = Settings("Original", selector: "Stored");
        var ownership = new BossModSettingsOwnership("BMR", original);
        ownership.OwnRuntime(Settings("Owned", selector: "Owned").Runtime!);
        ownership.OwnStoredSelector("Owned");
        var before = Settings("Owned", selector: "Owned", ai: !enabled);
        var after = Settings("", selector: "Owned", ai: enabled);
        Assert.False(ownership.MatchesOwnedRuntime(after));
        ownership.ObserveAiCommand(before, after, enabled);
        Assert.True(ownership.MatchesOwnedRuntime(after));
        Assert.True(BossModRuntimePresetState.Matches(original.Runtime, ownership.GetRuntimeCleanupTarget(after, false)));
        var unrelated = Settings("Different", selector: "Owned", ai: enabled);
        Assert.False(ownership.MatchesOwnedRuntime(unrelated));
    }

    [Fact]
    public void BmrNullRuntimeAfterAPresetOnlyWriteDoesNotEstablishOwnership()
    {
        var ownership = new BossModSettingsOwnership("BMR", Settings("Original", selector: "Original"));
        ownership.OwnRuntime(Settings("Owned").Runtime!);
        ownership.OwnStoredSelector("Owned");
        Assert.False(ownership.MatchesOwnedRuntime(Settings("", selector: "Owned", ai: true)));
        Assert.False(ownership.CanChangeRuntime(Settings("", selector: "Owned", ai: true)));
    }

    [Fact]
    public void RejectedAiReadbackAndUnexpectedRuntimeChangesDoNotClaimWrites()
    {
        var ownership = new BossModSettingsOwnership("BMR", Settings("Original", selector: "Original", ai: false));
        ownership.ObserveAiCommand(Settings("Original", ai: false), Settings("", ai: false), enabled: true);
        Assert.Null(ownership.OwnedRuntime);
        ownership.ObserveAiCommand(Settings("Original", ai: false), Settings("External", ai: true), enabled: true);
        Assert.Null(ownership.OwnedRuntime);
    }

    [Fact]
    public void VbmDelayedAiEffectsPreserveTheOrderOfAllOtherPresets()
    {
        var original = Settings("Original|Other", ai: false);
        var ownership = new BossModSettingsOwnership("VBM", original);
        var owned = Settings("First|Second", ai: false);
        ownership.OwnRuntime(owned.Runtime!);
        ownership.ObserveAiCommand(owned, owned with { AiEnabled = true }, enabled: true);
        Assert.True(ownership.MatchesOwnedRuntime(Settings("First|Second|VBM Multibox", ai: true)));
        Assert.False(ownership.MatchesOwnedRuntime(Settings("Second|First|VBM Multibox", ai: true)));
        Assert.False(ownership.MatchesOwnedRuntime(Settings("First|Second|External|VBM Multibox", ai: true)));
        var beforeOff = Settings("First|Second|VBM Multibox", ai: true);
        ownership.ObserveAiCommand(beforeOff, beforeOff with { AiEnabled = false }, enabled: false);
        Assert.True(ownership.MatchesOwnedRuntime(Settings("First|Second", ai: false)));
        Assert.True(BossModRuntimePresetState.Matches(original.Runtime, ownership.GetRuntimeCleanupTarget(Settings("First|Second", ai: false), false)));
    }

    [Theory]
    [InlineData("en-US", "1.5", true)]
    [InlineData("de-DE", "1,5", true)]
    [InlineData("de-DE", "1.5", false)]
    [InlineData("en-US", "NaN", false)]
    [InlineData("en-US", "Infinity", false)]
    [InlineData("en-US", "PreferredDistance = 1.5", false)]
    public void PreferredDistanceAcceptsOnlyOneFiniteNativeCultureValue(string culture, string value, bool accepted)
    {
        Assert.Equal(accepted, AutorotIpcService.TryParsePreferredDistance(new[] { value }, CultureInfo.GetCultureInfo(culture), out var distance));
        if (accepted) Assert.Equal(1.5, distance);
        Assert.False(AutorotIpcService.TryParsePreferredDistance(new[] { value, "extra" }, CultureInfo.GetCultureInfo(culture), out _));
    }

    private static BossModSettingsSnapshot Settings(string names, bool disabled = false, string selector = "Saved", double distance = 1.5, bool ai = false)
        => new(new BossModRuntimePresetState(disabled, names.Length == 0 ? Array.Empty<string>() : names.Split('|')),
            true, selector, distance, ai);

    [Theory]
    [InlineData("BMR", "", false)]
    [InlineData("BMR", "Original", false)]
    [InlineData("BMR", "", true)]
    [InlineData("VBM", "", false)]
    [InlineData("VBM", "Original|Other", false)]
    [InlineData("VBM", "", true)]
    public void LiteralIpcWritesRestoreTheFirstCompleteRuntimeBaseline(string provider, string originalNames, bool disabled)
    {
        var native = new NativeBossModProvider(provider, originalNames, disabled);
        using var service = native.CreateService();
        var profile = new CharacterConfig();
        Assert.True(service.PrepareOwnedBossModSettings(provider, "account", "character", profile));
        Assert.True(service.ApplyOwnedBossModPreset("null"));
        Assert.Equal(new[] { "null" }, native.Active);
        Assert.False(native.ForceDisabled);
        Assert.True(service.PrepareOwnedBossModSettings(provider, "account", "character", profile));
        Assert.True(service.ApplyOwnedBossModPreset("Next"));
        Assert.Equal(new[] { "Next" }, native.Active);
        service.ReleaseOwnedBossModSettings(turnEverythingOff: false);
        Assert.Equal(disabled, native.ForceDisabled);
        Assert.Equal(originalNames.Length == 0 ? Array.Empty<string>() : originalNames.Split('|'), native.Active);
        Assert.Equal("Original", native.Selector);
        Assert.Empty(native.Warnings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Missing")]
    [InlineData(null)]
    public void UnsupportedOriginalBmrSelectorsPreventPresetWrites(string? originalSelector)
    {
        var native = new NativeBossModProvider("BMR", "Original") { Selector = originalSelector };
        using var service = native.CreateService();
        Assert.True(service.PrepareOwnedBossModSettings("BMR", "account", "character", new CharacterConfig()));
        Assert.False(service.ApplyOwnedBossModPreset("null"));
        Assert.Equal(new[] { "Original" }, native.Active);
        Assert.Equal(originalSelector, native.Selector);
        Assert.Empty(native.Writes);
        Assert.Contains("restoration path", service.LastStatus);
    }

    [Fact]
    public void RejectedRuntimeWriteStillReleasesTheConfirmedPartialSelectorWrite()
    {
        var native = new NativeBossModProvider("BMR", "Original") { RejectRuntimeWrite = true };
        using var service = native.CreateService();
        Assert.True(service.PrepareOwnedBossModSettings("BMR", "account", "character", new CharacterConfig()));
        Assert.False(service.ApplyOwnedBossModPreset("null"));
        Assert.Equal("null", native.Selector);
        Assert.Equal(new[] { "Original" }, native.Active);
        native.RejectRuntimeWrite = false;
        service.ReleaseOwnedBossModSettings(turnEverythingOff: false);
        Assert.Equal("Original", native.Selector);
        Assert.Equal(new[] { "Original" }, native.Active);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void PreferredDistanceUsesConfirmedInvariantCommandsAndNativeCultureRestoration(string cultureName)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var native = new NativeBossModProvider("BMR", "Original");
            using var service = native.CreateService();
            Assert.True(service.PrepareOwnedBossModSettings("BMR", "account", "character", new CharacterConfig()));
            Assert.False(service.ApplyOwnedPreferredDistance(_ => true, 1.5));
            Assert.Equal(7.5, native.Distance);
            Assert.True(service.ApplyOwnedPreferredDistance(native.SendCommand, 1.5));
            Assert.Contains("/bmrai prefdistance 1.5", native.Writes);
            Assert.True(service.ApplyOwnedPreferredDistance(native.SendCommand, 2.5));
            service.ReleaseOwnedBossModSettings(turnEverythingOff: false);
            Assert.Equal(7.5, native.Distance);
            Assert.Contains("Configuration " + 7.5.ToString("R", CultureInfo.CurrentCulture), native.Writes);
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    [Fact]
    public void CleanupPreservesNewerIndependentFieldsAcrossItsOwnBmrAiOffEffect()
    {
        var native = new NativeBossModProvider("BMR", "Original");
        using var service = native.CreateService();
        Assert.True(service.PrepareOwnedBossModSettings("BMR", "account", "character", new CharacterConfig()));
        Assert.True(service.ApplyOwnedBossModPreset("null"));
        Assert.True(service.ApplyOwnedPreferredDistance(native.SendCommand, 1.5));
        native.Active = ["External"];
        native.Selector = "External";
        native.Distance = 9.5;
        native.AiEnabled = true;
        Assert.True(service.BeginOwnedBossModCleanup("account", "character"));
        Assert.True(service.SendBossModAiCommand("/bmrai off", native.SendCommand));
        Assert.Empty(native.Active);
        Assert.True(service.EndOwnedBossModCleanup(turnEverythingOff: false));
        Assert.Equal(new[] { "External" }, native.Active);
        Assert.Equal("External", native.Selector);
        Assert.Equal(9.5, native.Distance);
    }

    [Fact]
    public void VbmFollowTracksOnlyConfirmedNativeMultiboxEffectsAndRestoresOrderedPresets()
    {
        var native = new NativeBossModProvider("VBM", "Original|Other");
        using var service = native.CreateService();
        Assert.True(service.PrepareOwnedBossModSettings("VBM", "account", "character", new CharacterConfig()));
        Assert.True(service.ApplyOwnedBossModPreset("null"));
        Assert.True(service.SendBossModAiCommand("/vbmai follow Slot1", native.SendCommand));
        native.Active.Add("VBM Multibox");
        Assert.True(service.BeginOwnedBossModCleanup("account", "character"));
        Assert.True(service.SendBossModAiCommand("/vbmai off", native.SendCommand));
        native.Active.Remove("VBM Multibox");
        Assert.True(service.EndOwnedBossModCleanup(turnEverythingOff: false));
        Assert.Equal(new[] { "Original", "Other" }, native.Active);
        Assert.False(native.AiEnabled);
    }

    [Theory]
    [InlineData("BMR")]
    [InlineData("VBM")]
    public void ExplicitForceOffRestoresOwnedFieldsWithoutReactivatingAnOriginalRuntime(string provider)
    {
        var native = new NativeBossModProvider(provider, "Original");
        using var service = native.CreateService();
        Assert.True(service.PrepareOwnedBossModSettings(provider, "account", "character", new CharacterConfig()));
        Assert.True(service.ApplyOwnedBossModPreset("null"));
        if (provider == "BMR")
            Assert.True(service.ApplyOwnedPreferredDistance(native.SendCommand, 1.5));
        Assert.True(service.BeginOwnedBossModCleanup("account", "character"));
        Assert.True(service.SendBossModAiCommand(provider == "VBM" ? "/vbmai off" : "/bmrai off", native.SendCommand));
        Assert.True(service.EndOwnedBossModCleanup(turnEverythingOff: true));
        Assert.True(native.ForceDisabled);
        Assert.Empty(native.Active);
        Assert.Equal("Original", native.Selector);
        Assert.Equal(7.5, native.Distance);
        Assert.False(native.AiEnabled);
    }

    [Fact]
    public void ProviderReloadReportsTheDepartedSessionAndCapturesOnlyTheReplacementBaseline()
    {
        var native = new NativeBossModProvider("BMR", "Original");
        using var service = native.CreateService();
        var profile = new CharacterConfig();
        Assert.True(service.PrepareOwnedBossModSettings("BMR", "account", "character", profile));
        Assert.True(service.ApplyOwnedBossModPreset("null"));
        native.Reload();
        native.Active = ["Reloaded"];
        native.Selector = "Reloaded";
        native.Writes.Clear();
        Assert.True(service.PrepareOwnedBossModSettings("BMR", "account", "character", profile));
        Assert.Contains(native.Warnings, warning => warning.Contains("reloaded"));
        Assert.Empty(native.Writes);
        Assert.True(service.ApplyOwnedBossModPreset("Next"));
        service.ReleaseOwnedBossModSettings(turnEverythingOff: false);
        Assert.Equal(new[] { "Reloaded" }, native.Active);
        Assert.Equal("Reloaded", native.Selector);
    }

    [Fact]
    public void UnreadableCurrentDistanceReportsPartialCleanupWithoutGuessingAValue()
    {
        var native = new NativeBossModProvider("BMR", "Original");
        using var service = native.CreateService();
        Assert.True(service.PrepareOwnedBossModSettings("BMR", "account", "character", new CharacterConfig()));
        Assert.True(service.ApplyOwnedPreferredDistance(native.SendCommand, 1.5));
        native.UnavailableChannel = "BossMod.Configuration";
        native.Writes.Clear();
        Assert.False(service.BeginOwnedBossModCleanup("account", "character"));
        Assert.False(service.EndOwnedBossModCleanup(turnEverythingOff: false));
        Assert.Empty(native.Writes);
        Assert.Equal(1.5, native.Distance);
        Assert.Contains("unreadable", service.LastStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyVbmSnapshotUsesItsLiveAutomaticAiEnabledSetting(bool enabled)
    {
        var native = new NativeBossModProvider("VBM", "Original") { AiEnabled = enabled };
        var snapshotProvider = new BossModExternalAutomationSnapshotProvider(native.Interface, native.Log);
        var captured = snapshotProvider.Capture("account", "character");
        Assert.True(captured.Vbm.IsAvailable);
        Assert.Equal(enabled, captured.Vbm.AiActive);
        using var autorot = native.CreateService();
        var cleanup = new ExternalAutomationCleanupService(new NativeCommandSender(native), snapshotProvider,
            autorotIpcService: autorot);
        Assert.True(autorot.PrepareOwnedBossModSettings("VBM", "account", "character", new CharacterConfig()));
        cleanup.CaptureIfMissing("account", "character", "test");
        native.AiEnabled = !enabled;
        var result = cleanup.Cleanup(new CharacterConfig(), "account", "character", "test");
        Assert.Equal(enabled, native.AiEnabled);
        Assert.Contains(enabled ? "/vbmai on" : "/vbmai off", result.Commands);
    }

    private sealed record NativePreset(string Name, bool HiddenByDefault = false);
    private sealed record NativePresetDatabase(NativePreset[] AllPresets, NativePreset[] DefaultPresets, NativePreset[] UserPresets);
    private sealed record NativeRotationDatabase(NativePresetDatabase Presets);
    private sealed record NativeConfigRoot(object[] Nodes);
    private sealed record NativeHost(IServiceProvider Services);
    private sealed class NativeTickServices(object tick) : IServiceProvider
    {
        public object? GetService(Type type) => type == tick.GetType() ? tick : null;
    }
    private sealed class NativeCommandSender(NativeBossModProvider native) : IExternalAutomationCommandSender
    {
        public bool TrySendCommand(string command) => native.SendCommand(command);
    }

    private sealed class NativeBossModProvider
    {
        internal readonly IDalamudPluginInterface Interface;
        internal readonly IPluginLog Log;
        internal readonly List<string> Writes = [];
        internal readonly List<string> Warnings = [];
        internal List<string> Active;
        internal bool ForceDisabled;
        internal bool RejectRuntimeWrite;
        internal string? UnavailableChannel;
        internal double Distance = 7.5;
        private readonly object node;
        private readonly object? aiManager;
        private readonly object wrapper;
        private readonly Type pluginType;
        private readonly object database;
        private readonly object? host;

        internal string? Selector
        {
            get => (string?)node.GetType().GetField("AIAutorotPresetName")!.GetValue(node);
            set => node.GetType().GetField("AIAutorotPresetName")!.SetValue(node, value);
        }
        internal bool AiEnabled
        {
            get => (bool)node.GetType().GetField("Enabled")!.GetValue(node)!;
            set
            {
                node.GetType().GetField("Enabled")!.SetValue(node, value);
                aiManager?.GetType().GetField("Beh")!.SetValue(aiManager, value ? new object() : null);
            }
        }

        internal NativeBossModProvider(string provider, string names, bool disabled = false)
        {
            Active = names.Length == 0 ? [] : names.Split('|').ToList();
            ForceDisabled = disabled;
            var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("FrenRiderNativeBossMod" + Guid.NewGuid().ToString("N")),
                AssemblyBuilderAccess.Run).DefineDynamicModule("NativeProvider");
            var nodeBuilder = module.DefineType("BossMod.AI.AIConfig", TypeAttributes.Public);
            nodeBuilder.DefineField("Enabled", typeof(bool), FieldAttributes.Public);
            nodeBuilder.DefineField("AIAutorotPresetName", typeof(string), FieldAttributes.Public);
            node = Activator.CreateInstance(nodeBuilder.CreateType()!)!;
            Selector = "Original";
            var serviceBuilder = module.DefineType("BossMod.Service", TypeAttributes.Public);
            serviceBuilder.DefineField("Config", typeof(object), FieldAttributes.Public | FieldAttributes.Static);
            var serviceType = serviceBuilder.CreateType()!;
            serviceType.GetField("Config")!.SetValue(null, new NativeConfigRoot([node]));
            if (provider == "BMR")
            {
                var managerBuilder = module.DefineType("BossMod.AI.AIManager", TypeAttributes.Public);
                managerBuilder.DefineField("Instance", managerBuilder, FieldAttributes.Public | FieldAttributes.Static);
                managerBuilder.DefineField("Beh", typeof(object), FieldAttributes.Public);
                var managerType = managerBuilder.CreateType()!;
                aiManager = Activator.CreateInstance(managerType)!;
                managerType.GetField("Instance")!.SetValue(null, aiManager);
            }
            var presets = new[] { "Original", "Other", "null", "Next", "External", "Reloaded", "VBM Multibox" }
                .Select(name => new NativePreset(name, name == "VBM Multibox")).ToArray();
            database = new NativeRotationDatabase(new NativePresetDatabase(presets, presets, []));
            if (provider == "VBM")
            {
                var tickBuilder = module.DefineType("BossMod.Services.TickService", TypeAttributes.Public);
                tickBuilder.DefineField("_rotationDB", typeof(object), FieldAttributes.Public);
                var tickType = tickBuilder.CreateType()!;
                var tick = Activator.CreateInstance(tickType)!;
                tickType.GetField("_rotationDB")!.SetValue(tick, database);
                host = new NativeHost(new NativeTickServices(tick));
            }
            var pluginBuilder = module.DefineType("BossMod.Plugin", TypeAttributes.Public);
            pluginBuilder.DefineField("_rotationDB", typeof(object), FieldAttributes.Public);
            pluginBuilder.DefineField("Host", typeof(object), FieldAttributes.Public);
            pluginType = pluginBuilder.CreateType()!;
            var wrapperBuilder = module.DefineType("Dalamud.Plugin.Internal.Types.LocalPlugin", TypeAttributes.Public,
                typeof(object), [typeof(IExposedPlugin)]);
            wrapperBuilder.DefineField("instance", typeof(object), FieldAttributes.Public);
            wrapperBuilder.DefineField("Assembly", typeof(Assembly), FieldAttributes.Public);
            foreach (var method in typeof(IExposedPlugin).GetMethods())
            {
                var getter = wrapperBuilder.DefineMethod(method.Name,
                    MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final,
                    method.ReturnType, method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
                var il = getter.GetILGenerator();
                if (method.Name == "get_InternalName")
                    il.Emit(OpCodes.Ldstr, provider == "VBM" ? "BossMod" : "BossModReborn");
                else if (method.Name == "get_IsLoaded")
                    il.Emit(OpCodes.Ldc_I4_1);
                else if (method.ReturnType != typeof(void))
                {
                    if (method.ReturnType.IsValueType)
                    {
                        var local = il.DeclareLocal(method.ReturnType);
                        il.Emit(OpCodes.Ldloca, local);
                        il.Emit(OpCodes.Initobj, method.ReturnType);
                        il.Emit(OpCodes.Ldloc, local);
                    }
                    else il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Ret);
                wrapperBuilder.DefineMethodOverride(getter, method);
            }
            wrapper = Activator.CreateInstance(wrapperBuilder.CreateType()!)!;
            Reload();
            Log = (IPluginLog)Proxy(typeof(IPluginLog), (method, args) =>
            {
                if (method.Name == "Warning" && args?.FirstOrDefault() is string message)
                    Warnings.Add(message);
                return null;
            });
            Interface = (IDalamudPluginInterface)Proxy(typeof(IDalamudPluginInterface), (method, args) => method.Name switch
            {
                "get_InstalledPlugins" => new[] { (IExposedPlugin)wrapper },
                "GetIpcSubscriber" => Proxy(method.ReturnType, (call, values) => Invoke((string)args![0]!, call, values)),
                _ => null,
            });
        }

        internal AutorotIpcService CreateService() => new(Interface, Log);

        internal void Reload()
        {
            var plugin = Activator.CreateInstance(pluginType)!;
            pluginType.GetField("_rotationDB")!.SetValue(plugin, database);
            pluginType.GetField("Host")!.SetValue(plugin, host);
            wrapper.GetType().GetField("instance")!.SetValue(wrapper, plugin);
            wrapper.GetType().GetField("Assembly")!.SetValue(wrapper, pluginType.Assembly);
        }

        internal bool SendCommand(string command)
        {
            Writes.Add(command);
            if (command.StartsWith("/bmrai prefdistance ", StringComparison.Ordinal))
                Distance = double.Parse(command["/bmrai prefdistance ".Length..], CultureInfo.InvariantCulture);
            else if (command is "/bmrai on" or "/bmrai off")
            {
                AiEnabled = command.EndsWith(" on", StringComparison.Ordinal);
                Active.Clear();
                ForceDisabled = false;
            }
            else if (command is "/vbmai on" or "/vbmai off" || command.StartsWith("/vbmai follow ", StringComparison.Ordinal))
                AiEnabled = !command.EndsWith(" off", StringComparison.Ordinal);
            return true;
        }

        private object? Invoke(string channel, MethodInfo call, object?[]? values)
        {
            if (UnavailableChannel == channel) throw new InvalidOperationException("native endpoint unavailable");
            if (channel == "BossMod.Presets.GetForceDisabled") return ForceDisabled;
            if (channel == "BossMod.Presets.GetActive") return Active.Count == 1 ? Active[0] : null;
            if (channel == "BossMod.Presets.GetActiveList") return Active.ToList();
            if (channel == "BossMod.Presets.Get") return JsonSerializer.Serialize(new { Name = (string)values![0]! });
            if (channel == "BossMod.Configuration")
            {
                var arguments = (List<string>)values![0]!;
                if ((bool)values[1]!)
                {
                    Writes.Add("Configuration " + arguments[2]);
                    Distance = double.Parse(arguments[2], CultureInfo.CurrentCulture);
                }
                return new List<string> { Distance.ToString("R", CultureInfo.CurrentCulture) };
            }
            Writes.Add(channel + (values?.FirstOrDefault() is string name ? " " + name : string.Empty));
            if (channel == "BossMod.AI.SetPreset")
            {
                Assert.Equal("InvokeAction", call.Name);
                Selector = (string)values![0]!;
                return null;
            }
            if (RejectRuntimeWrite) return false;
            if (channel == "BossMod.Presets.SetActive") { Active = [(string)values![0]!]; ForceDisabled = false; }
            else if (channel == "BossMod.Presets.SetActiveList") { Active = ((List<string>)values![0]!).ToList(); ForceDisabled = false; }
            else if (channel == "BossMod.Presets.ClearActive") { Active.Clear(); ForceDisabled = false; }
            else if (channel == "BossMod.Presets.SetForceDisabled") { Active.Clear(); ForceDisabled = true; }
            else throw new InvalidOperationException("unexpected native endpoint " + channel);
            return true;
        }

        private static object Proxy(Type type, Func<MethodInfo, object?[]?, object?> handler)
        {
            var proxy = DispatchProxy.Create(type, typeof(QuestionableTestProxy));
            ((QuestionableTestProxy)proxy).Handler = handler;
            return proxy;
        }
    }

    [Fact]
    public void DungeonRsrUsesActualConfiguredAndEffectiveReadbackWithoutOperatingModeCalls()
    {
        var native = new NativeRsrProvider();
        using var service = native.CreateService();
        Assert.True(service.TryCaptureDungeonRsrAggro(out var ownership));
        Assert.Empty(native.Writes);
        Assert.Equal(2, native.Configured);
        Assert.True(service.ApplyDungeonRsrAggro(ownership!));
        Assert.Equal(0, native.Configured);
        Assert.Equal(new[] { "HostileType AllTargetsCanAttack" }, native.Writes);
        Assert.True(service.ApplyDungeonRsrAggro(ownership!));
        Assert.Single(native.Writes);
        Assert.True(service.ReleaseDungeonRsrAggro(ownership!));
        Assert.Equal(2, native.Configured);
        Assert.Equal("HostileType AllTargetsWhenSoloInDuty", native.Writes.Last());
    }

    [Fact]
    public void DungeonRsrPreservesActiveExternalTargetingAndNewerConfiguredValues()
    {
        var native = new NativeRsrProvider();
        using var service = native.CreateService();
        native.ExternalHostile = 1;
        Assert.False(service.TryCaptureDungeonRsrAggro(out _));
        Assert.Empty(native.Writes);
        native.ExternalHostile = 0;
        Assert.True(service.TryCaptureDungeonRsrAggro(out var ownership));
        Assert.True(service.ApplyDungeonRsrAggro(ownership!));
        Assert.Equal((byte)0, native.ExternalHostile);
        native.ExternalHostile = 4;
        Assert.False(service.ApplyDungeonRsrAggro(ownership!));
        Assert.True(service.ReleaseDungeonRsrAggro(ownership!));
        Assert.Equal(2, native.Configured);
        Assert.Equal((byte)4, native.ExternalHostile);
        native.ExternalHostile = null;
        Assert.True(service.TryCaptureDungeonRsrAggro(out ownership));
        Assert.True(service.ApplyDungeonRsrAggro(ownership!));
        native.Configured = 3;
        native.Writes.Clear();
        Assert.False(service.ApplyDungeonRsrAggro(ownership!));
        Assert.True(service.ReleaseDungeonRsrAggro(ownership!));
        Assert.Equal(3, native.Configured);
        Assert.Empty(native.Writes);
    }

    [Fact]
    public void DungeonRsrRejectsUnconfirmedWritesAndDoesNotRestoreIntoAnotherJobOrProvider()
    {
        var native = new NativeRsrProvider();
        using var service = native.CreateService();
        Assert.True(service.TryCaptureDungeonRsrAggro(out var ownership));
        native.RejectWrite = true;
        Assert.False(service.ApplyDungeonRsrAggro(ownership!));
        Assert.Equal(2, native.Configured);
        native.RejectWrite = false;
        Assert.True(service.ApplyDungeonRsrAggro(ownership!));
        native.Job = 32;
        native.Writes.Clear();
        Assert.False(service.ApplyDungeonRsrAggro(ownership!));
        Assert.False(service.ReleaseDungeonRsrAggro(ownership!));
        Assert.Equal(4, native.Configured);
        Assert.Empty(native.Writes);
        native.Job = 30;
        native.Reload();
        Assert.False(service.ReleaseDungeonRsrAggro(ownership!));
        Assert.Equal(0, native.Configured);
        Assert.Empty(native.Writes);
    }

    [Fact]
    public void DungeonRsrReleaseRetainsANewerSavedSelectionAndUnavailableReadbackIsExplicit()
    {
        var native = new NativeRsrProvider();
        using var service = native.CreateService();
        Assert.True(service.TryCaptureDungeonRsrAggro(out var ownership));
        Assert.True(service.ApplyDungeonRsrAggro(ownership!));
        native.Writes.Clear();
        Assert.True(service.ReleaseDungeonRsrAggro(ownership!, preserveNewerSelection: true));
        Assert.Equal(0, native.Configured);
        Assert.Empty(native.Writes);
        native.Job = 0;
        Assert.False(service.TryCaptureDungeonRsrAggro(out _));
        Assert.Contains("unavailable", service.LastStatus);
        native.Job = 30;
        native.Unavailable = true;
        Assert.False(service.ReleaseDungeonRsrAggro(ownership!));
        Assert.Contains("unavailable", service.LastStatus);
        Assert.Empty(native.Writes);
    }

    private sealed class NativeRsrProvider
    {
        private static int sequence;
        internal readonly List<string> Writes = [];
        internal bool RejectWrite;
        internal bool Unavailable;
        private readonly IDalamudPluginInterface pluginInterface;
        private readonly Type dataType;
        private readonly Type jobType;
        private readonly Type hostileType;
        private readonly Type overrideType;
        private readonly Type pluginType;
        private readonly object wrapper;
        private readonly Dictionary<uint, byte> configured = new() { [30] = 2, [32] = 4 };
        internal uint Job
        {
            get => Convert.ToUInt32(dataType.GetField("CurrentJob")!.GetValue(null));
            set => dataType.GetField("CurrentJob")!.SetValue(null, Enum.ToObject(jobType, value));
        }
        internal byte Configured { get => configured[Job]; set => configured[Job] = value; }
        internal byte? ExternalHostile
        {
            get
            {
                var current = dataType.GetField("External")!.GetValue(null);
                var value = current is null ? null : overrideType.GetField("Hostile")!.GetValue(current);
                return value is null ? null : Convert.ToByte(value);
            }
            set
            {
                var current = value is null ? null : Activator.CreateInstance(overrideType)!;
                if (current is not null) overrideType.GetField("Hostile")!.SetValue(current, Enum.ToObject(hostileType, value!.Value));
                dataType.GetField("External")!.SetValue(null, current);
            }
        }

        internal NativeRsrProvider()
        {
            var version = new Version(90, 0, 0, Interlocked.Increment(ref sequence));
            var commons = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ECommons") { Version = version },
                AssemblyBuilderAccess.Run).DefineDynamicModule("Job");
            var jobs = commons.DefineEnum("ECommons.ExcelServices.Job", TypeAttributes.Public, typeof(uint));
            jobs.DefineLiteral("None", 0U);
            jobs.DefineLiteral("NIN", 30U);
            jobs.DefineLiteral("DRK", 32U);
            jobType = jobs.CreateTypeInfo()!;
            var basic = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RotationSolver.Basic") { Version = version },
                AssemblyBuilderAccess.Run).DefineDynamicModule("Targeting");
            var hostile = basic.DefineEnum("RotationSolver.Basic.Data.TargetHostileType", TypeAttributes.Public, typeof(byte));
            foreach (var value in Enum.GetValues<AutorotIpcService.RsrTargetHostileType>())
                hostile.DefineLiteral(value.ToString(), (byte)value);
            hostileType = hostile.CreateTypeInfo()!;
            var overrides = basic.DefineType("RotationSolver.Basic.Data.IpcStateOverrides", TypeAttributes.Public | TypeAttributes.Sealed);
            var optionalHostile = typeof(Nullable<>).MakeGenericType(hostileType);
            var hostileField = overrides.DefineField("Hostile", optionalHostile, FieldAttributes.Public);
            var externalGetter = overrides.DefineMethod("get_HostileType", MethodAttributes.Public | MethodAttributes.SpecialName, optionalHostile, Type.EmptyTypes);
            var il = externalGetter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, hostileField); il.Emit(OpCodes.Ret);
            var externalProperty = overrides.DefineProperty("HostileType", PropertyAttributes.None, optionalHostile, Type.EmptyTypes);
            externalProperty.SetGetMethod(externalGetter);
            overrideType = overrides.CreateType()!;
            var data = basic.DefineType("RotationSolver.Basic.DataCenter", TypeAttributes.Public);
            var currentJob = data.DefineField("CurrentJob", jobType, FieldAttributes.Public | FieldAttributes.Static);
            var external = data.DefineField("External", overrideType, FieldAttributes.Public | FieldAttributes.Static);
            var jobGetter = data.DefineMethod("get_Job", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName, jobType, Type.EmptyTypes);
            il = jobGetter.GetILGenerator();
            il.Emit(OpCodes.Ldsfld, currentJob); il.Emit(OpCodes.Ret);
            data.DefineProperty("Job", PropertyAttributes.None, jobType, Type.EmptyTypes).SetGetMethod(jobGetter);
            var activeGetter = data.DefineMethod("get_ActiveIpcOverrides", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName, overrideType, Type.EmptyTypes);
            il = activeGetter.GetILGenerator();
            il.Emit(OpCodes.Ldsfld, external); il.Emit(OpCodes.Ret);
            data.DefineProperty("ActiveIpcOverrides", PropertyAttributes.None, overrideType, Type.EmptyTypes).SetGetMethod(activeGetter);
            var configs = basic.DefineType("RotationSolver.Basic.Configuration.Configs", TypeAttributes.Public);
            var values = configs.DefineField("Values", typeof(Dictionary<uint, byte>), FieldAttributes.Public);
            var configuredGetter = configs.DefineMethod("get_HostileType", MethodAttributes.Public | MethodAttributes.SpecialName, hostileType, Type.EmptyTypes);
            il = configuredGetter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, values); il.Emit(OpCodes.Ldsfld, currentJob);
            il.Emit(OpCodes.Conv_U4); il.Emit(OpCodes.Callvirt, typeof(Dictionary<uint, byte>).GetProperty("Item")!.GetMethod!); il.Emit(OpCodes.Ret);
            configs.DefineProperty("HostileType", PropertyAttributes.None, hostileType, Type.EmptyTypes).SetGetMethod(configuredGetter);
            var configType = configs.CreateType()!;
            var config = Activator.CreateInstance(configType)!;
            configType.GetField("Values")!.SetValue(config, configured);
            var service = basic.DefineType("RotationSolver.Basic.Service", TypeAttributes.Public);
            var currentConfig = service.DefineField("CurrentConfig", configType, FieldAttributes.Public | FieldAttributes.Static);
            var configGetter = service.DefineMethod("get_Config", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName, configType, Type.EmptyTypes);
            il = configGetter.GetILGenerator();
            il.Emit(OpCodes.Ldsfld, currentConfig); il.Emit(OpCodes.Ret);
            service.DefineProperty("Config", PropertyAttributes.None, configType, Type.EmptyTypes).SetGetMethod(configGetter);
            var serviceType = service.CreateType()!;
            serviceType.GetField("CurrentConfig")!.SetValue(null, config);
            var effectiveGetter = data.DefineMethod("get_CurrentTargetToHostileType", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName, hostileType, Type.EmptyTypes);
            il = effectiveGetter.GetILGenerator();
            var useConfig = il.DefineLabel();
            var optional = il.DeclareLocal(optionalHostile);
            il.Emit(OpCodes.Ldsfld, external); il.Emit(OpCodes.Brfalse, useConfig);
            il.Emit(OpCodes.Ldsfld, external); il.Emit(OpCodes.Ldfld, overrideType.GetField("Hostile")!); il.Emit(OpCodes.Stloc, optional);
            il.Emit(OpCodes.Ldloca, optional); il.Emit(OpCodes.Call, optionalHostile.GetProperty("HasValue")!.GetMethod!); il.Emit(OpCodes.Brfalse, useConfig);
            il.Emit(OpCodes.Ldloca, optional); il.Emit(OpCodes.Call, optionalHostile.GetProperty("Value")!.GetMethod!); il.Emit(OpCodes.Ret);
            il.MarkLabel(useConfig);
            il.Emit(OpCodes.Call, serviceType.GetProperty("Config")!.GetMethod!);
            il.Emit(OpCodes.Callvirt, configType.GetProperty("HostileType")!.GetMethod!); il.Emit(OpCodes.Ret);
            data.DefineProperty("CurrentTargetToHostileType", PropertyAttributes.None, hostileType, Type.EmptyTypes).SetGetMethod(effectiveGetter);
            dataType = data.CreateType()!;
            Job = 30;
            var provider = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RotationSolver") { Version = version },
                AssemblyBuilderAccess.Run).DefineDynamicModule("Provider");
            var plugin = provider.DefineType("RotationSolver.RotationSolverPlugin", TypeAttributes.Public);
            plugin.DefineField("BasicConfigReference", configType, FieldAttributes.Public);
            pluginType = plugin.CreateType()!;
            var exposed = provider.DefineType("Dalamud.Plugin.Internal.Types.LocalPlugin", TypeAttributes.Public, typeof(object), [typeof(IExposedPlugin)]);
            exposed.DefineField("instance", typeof(object), FieldAttributes.Public);
            exposed.DefineField("Assembly", typeof(Assembly), FieldAttributes.Public);
            foreach (var method in typeof(IExposedPlugin).GetMethods())
            {
                var getter = exposed.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final,
                    method.ReturnType, method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
                il = getter.GetILGenerator();
                if (method.Name == "get_InternalName") il.Emit(OpCodes.Ldstr, "RotationSolver");
                else if (method.Name == "get_IsLoaded") il.Emit(OpCodes.Ldc_I4_1);
                else if (method.ReturnType != typeof(void))
                {
                    if (method.ReturnType.IsValueType)
                    {
                        var local = il.DeclareLocal(method.ReturnType);
                        il.Emit(OpCodes.Ldloca, local); il.Emit(OpCodes.Initobj, method.ReturnType); il.Emit(OpCodes.Ldloc, local);
                    }
                    else il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Ret);
                exposed.DefineMethodOverride(getter, method);
            }
            wrapper = Activator.CreateInstance(exposed.CreateType()!)!;
            Reload();
            pluginInterface = (IDalamudPluginInterface)Proxy(typeof(IDalamudPluginInterface), (method, args) =>
            {
                if (Unavailable) throw new InvalidOperationException("native RSR unavailable");
                if (method.Name == "get_InstalledPlugins") return new[] { (IExposedPlugin)wrapper };
                if (method.Name != "GetIpcSubscriber" || (string)args![0]! != "RotationSolverReborn.OtherCommand")
                    throw new InvalidOperationException("unexpected RSR channel " + method.Name);
                return Proxy(method.ReturnType, (call, arguments) =>
                {
                    Assert.Equal("InvokeAction", call.Name);
                    Assert.Equal(AutorotIpcService.RsrOtherCommandType.Settings, arguments![0]);
                    var command = (string)arguments[1]!;
                    Writes.Add(command);
                    if (!RejectWrite) Configured = (byte)Enum.Parse<AutorotIpcService.RsrTargetHostileType>(command["HostileType ".Length..]);
                    return null;
                });
            });
        }

        internal AutorotIpcService CreateService() => new(pluginInterface, (IPluginLog)Proxy(typeof(IPluginLog), (_, _) => null));
        internal void Reload()
        {
            wrapper.GetType().GetField("instance")!.SetValue(wrapper, Activator.CreateInstance(pluginType));
            wrapper.GetType().GetField("Assembly")!.SetValue(wrapper, pluginType.Assembly);
        }
        private static object Proxy(Type type, Func<MethodInfo, object?[]?, object?> handler)
        {
            var proxy = DispatchProxy.Create(type, typeof(QuestionableTestProxy));
            ((QuestionableTestProxy)proxy).Handler = handler;
            return proxy;
        }
    }

    [Fact]
    public void CaptureStoresSnapshotsPerAccountAndCharacter()
    {
        var provider = new FakeSnapshotProvider();
        provider.Snapshots[("account", "one")] = Snapshot("account", "one", forbidMovement: true);
        provider.Snapshots[("account", "two")] = Snapshot("account", "two", forbidMovement: false);
        var sender = new FakeCommandSender();
        var service = new ExternalAutomationCleanupService(sender, provider);

        service.CaptureIfMissing("account", "one", "test");
        service.CaptureIfMissing("account", "two", "test");
        service.Cleanup(new CharacterConfig(), "account", "two", "test");

        Assert.Contains("/bmrai forbidmovement off", sender.Commands);
        Assert.DoesNotContain("/bmrai forbidmovement on", sender.Commands);
    }

    [Fact]
    public void RestoreSnapshotReplaysCapturedState()
    {
        var provider = new FakeSnapshotProvider();
        provider.Snapshots[("account", "character")] = Snapshot("account", "character", forbidMovement: true, cbtAutoFollow: true);
        var sender = new FakeCommandSender();
        var service = new ExternalAutomationCleanupService(sender, provider);

        service.CaptureIfMissing("account", "character", "test");
        var result = service.Cleanup(new CharacterConfig(), "account", "character", "test");

        Assert.Equal(ExternalAutomationCleanupState.Restored, result.State);
        Assert.Equal(
            new[]
            {
                "/bmrai forbidmovement on",
                "/bmrai followoutofcombat off",
                "/bmrai followcombat on",
                "/bmrai followmodule off",
                "/vbmai forbidmovement on",
                "/vbmai followoutofcombat off",
                "/vbmai followcombat on",
                "/vbmai followmodule off",
                "/cbt enable AutoFollow",
            },
            sender.Commands);
    }

    [Fact]
    public void RestoreSnapshotTurnsBmrAiOffFirstWhenCapturedIdle()
    {
        var provider = new FakeSnapshotProvider();
        provider.Snapshots[("account", "character")] = Snapshot("account", "character", forbidMovement: true, bmrAiActive: false);
        var sender = new FakeCommandSender();
        var service = new ExternalAutomationCleanupService(sender, provider);

        service.CaptureIfMissing("account", "character", "test");
        var result = service.Cleanup(new CharacterConfig(), "account", "character", "test");

        Assert.Equal(ExternalAutomationCleanupState.Restored, result.State);
        Assert.Equal("/bmrai off", sender.Commands[0]);
        Assert.Contains("/bmrai forbidmovement on", sender.Commands);
        Assert.DoesNotContain("/bmrai on", sender.Commands);
    }

    [Fact]
    public void RestoreSnapshotTurnsBmrAiOnLastWhenCapturedActive()
    {
        var provider = new FakeSnapshotProvider();
        provider.Snapshots[("account", "character")] = Snapshot("account", "character", forbidMovement: true, bmrAiActive: true);
        var sender = new FakeCommandSender();
        var service = new ExternalAutomationCleanupService(sender, provider);

        service.CaptureIfMissing("account", "character", "test");
        var result = service.Cleanup(new CharacterConfig(), "account", "character", "test");

        Assert.Equal(ExternalAutomationCleanupState.Restored, result.State);
        var followModuleIndex = sender.Commands.IndexOf("/bmrai followmodule off");
        var aiOnIndex = sender.Commands.IndexOf("/bmrai on");
        Assert.True(aiOnIndex > followModuleIndex);
        Assert.DoesNotContain("/bmrai off", sender.Commands);
    }

    [Fact]
    public void TurnEverythingOffStopsManagedAutomationAndWrathWhenStarted()
    {
        var provider = new FakeSnapshotProvider();
        var sender = new FakeCommandSender();
        var service = new ExternalAutomationCleanupService(
            sender,
            provider,
            rsrCleanupController: new AutorotRsrCleanupController(() => true, sender));
        var config = new CharacterConfig { CleanupMode = FrenRiderCleanupMode.TurnEverythingOff };

        service.MarkWrathAutoStarted("account", "character", "test");
        var result = service.Cleanup(config, "account", "character", "test");

        Assert.Equal(ExternalAutomationCleanupState.ForceOff, result.State);
        Assert.Equal(
            new[] { "/bmrai off", "/vbmai off", "/cbt disable AutoFollow", "/wrath auto off" },
            sender.Commands);
        Assert.Contains(AutorotRsrCleanupController.TypedActionLabel, result.Commands);
        Assert.DoesNotContain(AutorotRsrCleanupController.FallbackCommand, sender.Commands);
    }

    [Fact]
    public void TurnEverythingOffDoesNotStopWrathWhenFrenRiderDidNotStartIt()
    {
        var provider = new FakeSnapshotProvider();
        var sender = new FakeCommandSender();
        var service = new ExternalAutomationCleanupService(sender, provider);
        var config = new CharacterConfig { CleanupMode = FrenRiderCleanupMode.TurnEverythingOff };

        service.Cleanup(config, "account", "character", "test");

        Assert.DoesNotContain("/wrath auto off", sender.Commands);
    }

    [Fact]
    public void FailedCommandReportsPartialCleanup()
    {
        var provider = new FakeSnapshotProvider();
        var sender = new FakeCommandSender { FailedCommands = { "/vbmai off" } };
        var service = new ExternalAutomationCleanupService(sender, provider);
        var config = new CharacterConfig { CleanupMode = FrenRiderCleanupMode.TurnEverythingOff };

        var result = service.Cleanup(config, "account", "character", "test");

        Assert.Equal(ExternalAutomationCleanupState.Partial, result.State);
        Assert.Contains("Partial", result.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TypedRsrSuccessDoesNotSendFallbackAndReportsOneCompositeAction()
    {
        var provider = new FakeSnapshotProvider();
        var sender = new FakeCommandSender();
        var controller = new AutorotRsrCleanupController(() => true, sender);
        var service = new ExternalAutomationCleanupService(
            sender,
            provider,
            rsrCleanupController: controller);

        var result = service.Cleanup(
            new CharacterConfig { CleanupMode = FrenRiderCleanupMode.TurnEverythingOff },
            "account",
            "character",
            "test");

        Assert.Equal(ExternalAutomationCleanupState.ForceOff, result.State);
        Assert.DoesNotContain(AutorotRsrCleanupController.FallbackCommand, sender.Commands);
        Assert.Equal(1, result.Commands.Count(command =>
            command is AutorotRsrCleanupController.TypedActionLabel or AutorotRsrCleanupController.FallbackCommand));
        Assert.Contains(AutorotRsrCleanupController.TypedActionLabel, result.Commands);
    }

    [Fact]
    public void FailedTypedRsrUsesSuccessfulCommandFallbackOnce()
    {
        var provider = new FakeSnapshotProvider();
        var sender = new FakeCommandSender();
        var controller = new AutorotRsrCleanupController(() => false, sender);
        var service = new ExternalAutomationCleanupService(
            sender,
            provider,
            rsrCleanupController: controller);

        var result = service.Cleanup(
            new CharacterConfig { CleanupMode = FrenRiderCleanupMode.TurnEverythingOff },
            "account",
            "character",
            "test");

        Assert.Equal(ExternalAutomationCleanupState.ForceOff, result.State);
        Assert.Equal(1, sender.Commands.Count(command =>
            command == AutorotRsrCleanupController.FallbackCommand));
        Assert.Contains(AutorotRsrCleanupController.FallbackCommand, result.Commands);
        Assert.DoesNotContain(AutorotRsrCleanupController.TypedActionLabel, result.Commands);
    }

    [Fact]
    public void ThrowingTypedRsrStillUsesCommandFallbackOnce()
    {
        var provider = new FakeSnapshotProvider();
        var sender = new FakeCommandSender();
        var controller = new AutorotRsrCleanupController(
            () => throw new InvalidOperationException("typed IPC unavailable"),
            sender);
        var service = new ExternalAutomationCleanupService(
            sender,
            provider,
            rsrCleanupController: controller);

        var result = service.Cleanup(
            new CharacterConfig { CleanupMode = FrenRiderCleanupMode.TurnEverythingOff },
            "account",
            "character",
            "test");

        Assert.Equal(ExternalAutomationCleanupState.ForceOff, result.State);
        Assert.Equal(1, sender.Commands.Count(command =>
            command == AutorotRsrCleanupController.FallbackCommand));
        Assert.Contains(AutorotRsrCleanupController.FallbackCommand, result.Commands);
    }

    [Fact]
    public void TypedAndFallbackRsrFailureCountsAsOneFailedCompositeAction()
    {
        var provider = new FakeSnapshotProvider();
        var sender = new FakeCommandSender
        {
            FailedCommands = { AutorotRsrCleanupController.FallbackCommand },
        };
        var controller = new AutorotRsrCleanupController(() => false, sender);
        var service = new ExternalAutomationCleanupService(
            sender,
            provider,
            rsrCleanupController: controller);

        var result = service.Cleanup(
            new CharacterConfig { CleanupMode = FrenRiderCleanupMode.TurnEverythingOff },
            "account",
            "character",
            "test");

        Assert.Equal(ExternalAutomationCleanupState.Partial, result.State);
        Assert.Contains("1/4", result.StatusText, StringComparison.Ordinal);
        Assert.Equal(1, result.Commands.Count(command =>
            command is AutorotRsrCleanupController.TypedActionLabel or AutorotRsrCleanupController.FallbackCommand));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RestoreSnapshotRestoresCapturedDaedalusEnabledState(bool capturedEnabled)
    {
        var provider = new FakeSnapshotProvider();
        provider.Snapshots[("account", "character")] = Snapshot("account", "character", forbidMovement: false);
        var sender = new FakeCommandSender();
        var daedalus = new FakeDaedalusAutomationController { ReadEnabled = capturedEnabled };
        var service = new ExternalAutomationCleanupService(
            sender,
            provider,
            daedalusAutomationController: daedalus);

        service.CaptureIfMissing("account", "character", "test");
        var result = service.Cleanup(new CharacterConfig(), "account", "character", "test");

        Assert.Equal(ExternalAutomationCleanupState.Restored, result.State);
        Assert.Equal(new[] { capturedEnabled }, daedalus.SetRequests);
        Assert.Contains(AutorotDaedalusAutomationController.GetActionLabel(capturedEnabled), result.Commands);
    }

    [Fact]
    public void TurnEverythingOffDisablesDaedalus()
    {
        var provider = new FakeSnapshotProvider();
        var sender = new FakeCommandSender();
        var daedalus = new FakeDaedalusAutomationController();
        var service = new ExternalAutomationCleanupService(
            sender,
            provider,
            daedalusAutomationController: daedalus);

        var result = service.Cleanup(
            new CharacterConfig { CleanupMode = FrenRiderCleanupMode.TurnEverythingOff },
            "account",
            "character",
            "test");

        Assert.Equal(ExternalAutomationCleanupState.ForceOff, result.State);
        Assert.Equal(new[] { false }, daedalus.SetRequests);
        Assert.Contains(AutorotDaedalusAutomationController.GetActionLabel(false), result.Commands);
    }

    [Fact]
    public void UnavailableDaedalusSnapshotIsReportedWithoutWritingState()
    {
        var provider = new FakeSnapshotProvider();
        provider.Snapshots[("account", "character")] = Snapshot("account", "character", forbidMovement: false);
        var sender = new FakeCommandSender();
        var daedalus = new FakeDaedalusAutomationController { CanRead = false };
        var service = new ExternalAutomationCleanupService(
            sender,
            provider,
            daedalusAutomationController: daedalus);

        service.CaptureIfMissing("account", "character", "test");
        var result = service.Cleanup(new CharacterConfig(), "account", "character", "test");

        Assert.Equal(ExternalAutomationCleanupState.Partial, result.State);
        Assert.Empty(daedalus.SetRequests);
    }

    [Fact]
    public void FailedDaedalusRestoreReportsPartialCleanup()
    {
        var provider = new FakeSnapshotProvider();
        provider.Snapshots[("account", "character")] = Snapshot("account", "character", forbidMovement: false);
        var sender = new FakeCommandSender();
        var daedalus = new FakeDaedalusAutomationController
        {
            ReadEnabled = true,
            SetSucceeds = false,
        };
        var service = new ExternalAutomationCleanupService(
            sender,
            provider,
            daedalusAutomationController: daedalus);

        service.CaptureIfMissing("account", "character", "test");
        var result = service.Cleanup(new CharacterConfig(), "account", "character", "test");

        Assert.Equal(ExternalAutomationCleanupState.Partial, result.State);
        Assert.Equal(new[] { true }, daedalus.SetRequests);
    }

    private static ExternalAutomationSnapshot Snapshot(
        string accountId,
        string characterKey,
        bool forbidMovement,
        bool cbtAutoFollow = false,
        bool? bmrAiActive = null,
        bool? vbmAiActive = null)
    {
        var bmr = new BossModAutomationSnapshot(
            true,
            bmrAiActive,
            forbidMovement,
            false,
            true,
            false,
            false,
            null,
            string.Empty);
        var vbm = new BossModAutomationSnapshot(
            true,
            vbmAiActive,
            forbidMovement,
            false,
            true,
            false,
            false,
            null,
            string.Empty);
        var cbt = new CbtAutomationSnapshot(true, cbtAutoFollow, string.Empty);
        return new ExternalAutomationSnapshot(accountId, characterKey, bmr, vbm, cbt, DateTimeOffset.UtcNow);
    }

    private sealed class FakeSnapshotProvider : IExternalAutomationSnapshotProvider
    {
        public Dictionary<(string AccountId, string CharacterKey), ExternalAutomationSnapshot> Snapshots { get; } = new();

        public ExternalAutomationSnapshot Capture(string accountId, string characterKey)
            => Snapshots.TryGetValue((accountId, characterKey), out var snapshot)
                ? snapshot
                : Snapshot(accountId, characterKey, forbidMovement: false);
    }

    private sealed class FakeCommandSender : IExternalAutomationCommandSender
    {
        public List<string> Commands { get; } = new();
        public HashSet<string> FailedCommands { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public bool TrySendCommand(string command)
        {
            Commands.Add(command);
            return !FailedCommands.Contains(command);
        }
    }

    private sealed class FakeDaedalusAutomationController : IDaedalusAutomationController
    {
        public bool CanRead { get; init; } = true;
        public bool ReadEnabled { get; init; }
        public bool SetSucceeds { get; init; } = true;
        public List<bool> SetRequests { get; } = new();

        public bool TryGetEnabled(out bool enabled)
        {
            enabled = ReadEnabled;
            return CanRead;
        }

        public bool TrySetEnabled(bool enabled)
        {
            SetRequests.Add(enabled);
            return SetSucceeds;
        }
    }
}
