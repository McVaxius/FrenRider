using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FrenRider.Models;

namespace FrenRider.Services;

/// <summary>
/// Manages IPC communication with BMR/VBM to create and activate autorotation presets.
/// Loads FrenRider preset JSONs from data\bm and pushes them via IPC when requested.
/// </summary>
public class AutorotIpcService : IDisposable
{
    internal const string DaedalusIsEnabledChannel = "Daedalus.IsEnabled";
    internal const string DaedalusSetEnabledChannel = "Daedalus.SetEnabled";

    public enum RsrStateCommandType : byte
    {
        Off,
        Auto,
        TargetOnly,
        Manual,
        AutoDuty,
        Henched,
        PvP,
    }

    public enum RsrOtherCommandType : byte
    {
        Settings,
        Rotations,
        DutyRotations,
        DoActions,
        ToggleActions,
        NextAction,
    }

    public enum RsrTargetHostileType : byte
    {
        AllTargetsCanAttack,
        TargetsHaveTarget,
        AllTargetsWhenSoloInDuty,
        AllTargetsWhenSolo,
        SoloDeepDungeonSmart,
    }

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private BossModSettingsOwnership? ownedSettings;
    private WeakReference<object>? ownedProvider;
    private (string Account, string Character, CharacterConfig Config, string Provider) ownedIdentity;
    private bool cleanupStarted;
    private bool cleanupFailed;
    private BossModRuntimePresetState? cleanupRuntimeExpected;
    private BossModRuntimePresetState? cleanupRuntimeTarget;

    private static readonly string[] PresetFileNames =
    {
        "FRENRIDER - TANK.json",
        "FRENRIDER - MELEE.json",
        "FRENRIDER - RANGED.json",
        "passive - tank.json",
        "passive - melee.json",
        "passive - ranged.json",
    };

    public string LastStatus { get; private set; } = "";

    public AutorotIpcService(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
    }

    public BossModPresetCatalog ReadPresetCatalog(string rotationProvider)
    {
        var provider = GetBossModProvider(rotationProvider);
        if (!TryGetLiveBossMod(provider, out var instance, out var assembly, out var detail))
            return BossModPresetCatalog.Unavailable(provider, detail);
        try
        {
            var rotationDatabase = ReadRotationDatabase(provider, instance!, assembly!);
            var presets = rotationDatabase is null ? null : LiveMember(rotationDatabase, "Presets");
            if (presets is null || LiveMember(presets, "AllPresets") is not IEnumerable visible
                || LiveMember(presets, "DefaultPresets") is not IEnumerable defaults
                || LiveMember(presets, "UserPresets") is not IEnumerable users)
                return BossModPresetCatalog.Unavailable(provider, "The running provider's complete preset database is unavailable.");
            return ReadPresetCatalog(provider, visible, defaults, users);
        }
        catch (Exception ex)
        {
            return BossModPresetCatalog.Unavailable(provider, ex.GetBaseException().Message);
        }
    }

    internal static BossModPresetCatalog ReadPresetCatalog(string provider, IEnumerable visible,
        IEnumerable defaults, IEnumerable users)
    {
        var displayed = new List<string>();
        var definitions = new List<string>();
        foreach (var collection in new[] { defaults, users })
        foreach (var preset in collection)
        {
            if (preset is null || LiveMember(preset, "Name") is not string name)
                return BossModPresetCatalog.Unavailable(provider, "A preset definition is unreadable.");
            definitions.Add(name);
        }
        var nativeNames = new List<string>();
        foreach (var preset in visible)
        {
            if (preset is null || LiveMember(preset, "Name") is not string name
                || LiveMember(preset, "HiddenByDefault") is not bool hidden)
                return BossModPresetCatalog.Unavailable(provider, "A displayed preset is unreadable.");
            nativeNames.Add(name);
            if (IsNoPreset(name) || provider == "VBM" && (hidden || name == "VBM Multibox"))
                continue;
            displayed.Add(name);
        }
        displayed.RemoveAll(name => !string.Equals(nativeNames.FirstOrDefault(candidate =>
                string.Equals(candidate, name, StringComparison.CurrentCultureIgnoreCase)), name, StringComparison.Ordinal)
            || provider == "BMR" && !string.Equals(nativeNames.FirstOrDefault(candidate =>
                string.Equals(candidate.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)), name, StringComparison.Ordinal));
        return new BossModPresetCatalog(provider, true, displayed.Distinct(StringComparer.Ordinal).ToArray(),
            definitions, nativeNames, string.Empty);
    }

    internal static string ResolvePresetSelection(string saved, BossModPresetCatalog catalog)
    {
        if (IsNoPreset(saved) || !catalog.Readable || catalog.DisplayedNames.Count == 0
            || catalog.DefinitionNames.Any(name => string.Equals(name, saved, StringComparison.CurrentCultureIgnoreCase)))
            return saved;
        return catalog.DisplayedNames[0];
    }

    internal static bool IsNoPreset(string? name)
        => string.IsNullOrWhiteSpace(name) || string.Equals(name, "none", StringComparison.OrdinalIgnoreCase);

    internal static string GetBossModProvider(string rotationProvider)
        => string.Equals(rotationProvider, "VBM", StringComparison.OrdinalIgnoreCase) ? "VBM" : "BMR";

    private bool TryGetLiveBossMod(string provider, out object? instance, out Assembly? assembly, out string detail)
    {
        instance = null;
        assembly = null;
        try
        {
            var loaded = pluginInterface.InstalledPlugins.Where(plugin => plugin.IsLoaded
                && plugin.InternalName is "BossModReborn" or "BossMod").ToArray();
            var expectedName = provider == "VBM" ? "BossMod" : "BossModReborn";
            if (loaded.Length != 1 || loaded[0].InternalName != expectedName)
            {
                detail = loaded.Length > 1 ? "Both BossMod providers are loaded; their shared IPC is ambiguous."
                    : "The selected BossMod provider is not loaded.";
                return false;
            }
            instance = BossModExternalAutomationSnapshotProvider.FindLivePluginInstance(loaded[0], out assembly, out detail);
            return instance is not null && assembly is not null;
        }
        catch (Exception ex)
        {
            detail = ex.GetBaseException().Message;
            return false;
        }
    }

    internal static object? ReadRotationDatabase(string provider, object instance, Assembly assembly)
    {
        if (provider == "BMR")
            return LiveMember(instance, "_rotationDB");
        var tickType = assembly.GetType("BossMod.Services.TickService");
        var host = LiveMember(instance, "Host");
        if (tickType is null || host is null || LiveMember(host, "Services") is not IServiceProvider services)
            return null;
        var tick = services.GetService(tickType);
        return tick is not null && tick.GetType() == tickType ? LiveMember(tick, "_rotationDB") : null;
    }

    private static object? LiveMember(object root, string name)
        => BossModExternalAutomationSnapshotProvider.GetInstanceMember(root, name);

    internal bool PrepareOwnedBossModSettings(string rotationProvider, string account, string character,
        CharacterConfig config)
    {
        var provider = GetBossModProvider(rotationProvider);
        var identity = (Account: account, Character: character, Config: config, Provider: provider);
        if (!TryGetLiveBossMod(provider, out var live, out var assembly, out var detail))
        {
            LastStatus = detail;
            return false;
        }
        if (ownedSettings is not null && ownedIdentity.Provider == provider
            && (!ownedProvider!.TryGetTarget(out var captured) || !ReferenceEquals(captured, live)))
        {
            LastStatus = "BossMod provider reloaded; the departed session cannot be restored into its replacement.";
            log.Warning($"[FrenRider] Owned BossMod settings cleanup was incomplete: {LastStatus}");
            ownedSettings = null;
            ownedProvider = null;
            cleanupStarted = cleanupFailed = false;
            cleanupRuntimeExpected = cleanupRuntimeTarget = null;
        }
        if (ownedSettings is not null && (cleanupStarted || cleanupFailed))
            return false;
        if (ownedSettings is not null && ownedIdentity != identity)
            ReleaseOwnedBossModSettings(ownedIdentity.Config.CleanupMode == FrenRiderCleanupMode.TurnEverythingOff);
        if (cleanupFailed)
            return false;
        if (ownedSettings is null)
        {
            ownedIdentity = identity;
            ownedProvider = new WeakReference<object>(live!);
            ownedSettings = new BossModSettingsOwnership(provider, ReadBossModSettings(provider, assembly!));
        }
        return true;
    }

    private bool TryReadOwnedBossModSettings(out BossModSettingsSnapshot snapshot, bool allowCleanup = false)
    {
        snapshot = BossModSettingsSnapshot.Unavailable;
        if (!allowCleanup && (cleanupStarted || cleanupFailed))
            return false;
        if (ownedSettings is null)
        {
            LastStatus = "No owned BossMod settings session.";
            return false;
        }
        if (!TryGetLiveBossMod(ownedIdentity.Provider, out var live, out var assembly, out var detail))
        {
            LastStatus = detail;
            return false;
        }
        if (!ownedProvider!.TryGetTarget(out var captured) || !ReferenceEquals(captured, live))
        {
            LastStatus = "BossMod provider reloaded; the departed session cannot be restored into its replacement.";
            return false;
        }
        snapshot = ReadBossModSettings(ownedIdentity.Provider, assembly!);
        return true;
    }

    private BossModSettingsSnapshot ReadBossModSettings(string provider, Assembly assembly)
    {
        BossModRuntimePresetState? runtime = null;
        try
        {
            var forceDisabled = pluginInterface.GetIpcSubscriber<bool>("BossMod.Presets.GetForceDisabled").InvokeFunc();
            var names = provider == "VBM"
                ? pluginInterface.GetIpcSubscriber<List<string>>("BossMod.Presets.GetActiveList").InvokeFunc()?.ToArray()
                : pluginInterface.GetIpcSubscriber<string>("BossMod.Presets.GetActive").InvokeFunc() is { } active
                    ? new[] { active } : Array.Empty<string>();
            if (names is not null && names.All(name => name is not null))
                runtime = new BossModRuntimePresetState(forceDisabled, forceDisabled ? Array.Empty<string>() : names);
        }
        catch (Exception ex) { log.Debug($"BossMod runtime preset read unavailable: {ex.GetBaseException().Message}"); }
        var node = ReadAiConfig(assembly);
        var storedReadable = provider == "BMR" && node is not null
            && BossModExternalAutomationSnapshotProvider.TryGetInstanceMember(node, "AIAutorotPresetName", out var value)
            && value is null or string;
        var stored = storedReadable ? LiveMember(node!, "AIAutorotPresetName") as string : null;
        var aiEnabled = provider == "BMR" ? BossModExternalAutomationSnapshotProvider.TryGetAiActive(assembly)
            : node is null ? null : LiveMember(node, "Enabled") as bool?;
        double? distance = null;
        if (provider == "BMR")
        {
            try
            {
                var values = pluginInterface.GetIpcSubscriber<List<string>, bool, List<string>>("BossMod.Configuration")
                    .InvokeFunc(new List<string> { "AIConfig", "PreferredDistance" }, false);
                if (TryParsePreferredDistance(values, CultureInfo.CurrentCulture, out var parsed))
                    distance = parsed;
            }
            catch (Exception ex) { log.Debug($"BossMod preferred-distance read unavailable: {ex.GetBaseException().Message}"); }
        }
        return new BossModSettingsSnapshot(runtime, storedReadable, stored, distance, aiEnabled);
    }

    private static object? ReadAiConfig(Assembly assembly)
    {
        try
        {
            var service = assembly.GetType("BossMod.Service");
            var aiConfigType = assembly.GetType("BossMod.AI.AIConfig");
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var root = service?.GetProperty("Config", flags)?.GetValue(null) ?? service?.GetField("Config", flags)?.GetValue(null);
            if (root is null || root.GetType().Assembly != assembly || aiConfigType is null)
                return null;
            var getter = root.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .SingleOrDefault(method => method.Name == "Get" && method.IsGenericMethodDefinition
                    && method.GetGenericArguments().Length == 1 && method.GetParameters().Length == 0);
            var node = getter?.MakeGenericMethod(aiConfigType).Invoke(root, null);
            return node?.GetType() == aiConfigType ? node : null;
        }
        catch { }
        return null;
    }

    internal static bool TryParsePreferredDistance(IReadOnlyList<string>? values, CultureInfo culture, out double distance)
    {
        distance = 0;
        return values?.Count == 1 && double.TryParse(values[0], NumberStyles.Float, culture, out distance)
            && double.IsFinite(distance);
    }

    private bool IsExactRuntimePreset(string name)
    {
        try
        {
            var json = pluginInterface.GetIpcSubscriber<string, string>("BossMod.Presets.Get").InvokeFunc(name);
            if (json is null)
                return false;
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("Name", out var value)
                && value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), name, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private bool IsExactAiSelector(string name, BossModPresetCatalog catalog)
        => !string.IsNullOrWhiteSpace(name) && catalog.Readable
            && string.Equals(catalog.NativeNames.FirstOrDefault(candidate =>
                string.Equals(candidate.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)), name, StringComparison.Ordinal);

    private bool IsRestorableAiSelector(string? name, BossModPresetCatalog catalog)
        => name is not null ? IsExactAiSelector(name, catalog)
            : catalog.Readable && !catalog.NativeNames.Any(string.IsNullOrWhiteSpace);

    internal bool ApplyOwnedBossModPreset(string presetName)
    {
        if (IsNoPreset(presetName))
            return true;
        if (!TryReadOwnedBossModSettings(out var before))
            return false;
        var ownership = ownedSettings!;
        var catalog = ReadPresetCatalog(ownedIdentity.Provider);
        if (!catalog.Readable || !IsExactRuntimePreset(presetName)
            || ownedSettings!.Original.Runtime is not { } original
            || original.Names.Any(name => !IsExactRuntimePreset(name))
            || !ownedSettings.CanChangeRuntime(before))
        {
            LastStatus = "Preset retained: its exact name, original runtime state or current ownership is unavailable.";
            return false;
        }
        if (ownedIdentity.Provider == "BMR")
        {
            var selector = ownedSettings.Original.StoredAiSelector;
            if (!ownedSettings.Original.StoredSelectorReadable
                || !IsRestorableAiSelector(selector, catalog) || !IsExactAiSelector(presetName, catalog)
                || !ownedSettings.CanChangeStoredSelector(before))
            {
                LastStatus = "BMR preset retained: the original saved AI selector has no verified exact restoration path.";
                return false;
            }
            _ = WriteAiSelector(presetName);
            if (!TryReadOwnedBossModSettings(out var selectorApplied)
                || !ReferenceEquals(ownedSettings, ownership)
                || !selectorApplied.StoredSelectorReadable || selectorApplied.StoredAiSelector != presetName)
            {
                LastStatus = "BMR saved AI preset write could not be confirmed.";
                return false;
            }
            ownership.OwnStoredSelector(presetName);
            if (!ownership.CanChangeRuntime(selectorApplied))
            {
                LastStatus = "BossMod runtime changed during the selector callback; that external selection was retained.";
                return false;
            }
        }
        var requested = new BossModRuntimePresetState(false, new[] { presetName });
        var submitted = WriteRuntimePreset(ownedIdentity.Provider, requested);
        if (TryReadOwnedBossModSettings(out var applied) && ReferenceEquals(ownedSettings, ownership)
            && BossModRuntimePresetState.Matches(applied.Runtime, requested))
        {
            ownership.OwnRuntime(requested);
            LastStatus = $"{ownedIdentity.Provider} preset '{presetName}' confirmed.";
            return true;
        }
        LastStatus = submitted ? "BossMod preset write was submitted but its exact readback was not confirmed."
            : "BossMod preset setter is unavailable or rejected the selected name.";
        return false;
    }

    private bool WriteAiSelector(string name)
    {
        try
        {
            pluginInterface.GetIpcSubscriber<string, object>("BossMod.AI.SetPreset").InvokeAction(name);
            return true;
        }
        catch (Exception ex) { log.Debug($"BossMod AI preset setter unavailable: {ex.GetBaseException().Message}"); return false; }
    }

    private bool WriteRuntimePreset(string provider, BossModRuntimePresetState state)
    {
        if (state.ForceDisabled)
            return TryBoolIpc("BossMod.Presets.SetForceDisabled") == true;
        if (provider == "VBM")
            return TryBoolIpc("BossMod.Presets.SetActiveList", state.Names.ToList()) == true;
        return state.Names.Count == 0 ? TryBoolIpc("BossMod.Presets.ClearActive") == true
            : state.Names.Count == 1 && TryBoolIpc("BossMod.Presets.SetActive", state.Names[0]) == true;
    }

    internal bool ApplyOwnedPreferredDistance(Func<string, bool> sendCommand, double requested)
    {
        if (ownedIdentity.Provider != "BMR" || !double.IsFinite(requested)
            || !TryReadOwnedBossModSettings(out var before)
            || !ownedSettings!.CanChangeDistance(before.PreferredDistance))
        {
            LastStatus = "Preferred distance retained: its original or current value is unavailable or owned elsewhere.";
            return false;
        }
        var submitted = sendCommand($"/bmrai prefdistance {requested.ToString("R", CultureInfo.InvariantCulture)}");
        if (TryReadOwnedBossModSettings(out var after) && after.PreferredDistance == requested)
        {
            ownedSettings!.OwnDistance(requested);
            return true;
        }
        LastStatus = submitted ? "Preferred-distance command was submitted but its readback was not confirmed."
            : "Preferred-distance command dispatch failed.";
        return false;
    }

    internal bool SendBossModAiCommand(string command, Func<string, bool> sendCommand)
    {
        var relevant = ownedSettings is not null
            && (command is "/bmrai on" or "/bmrai off" or "/vbmai on" or "/vbmai off"
                || command.StartsWith("/bmrai follow ", StringComparison.Ordinal)
                || command.StartsWith("/vbmai follow ", StringComparison.Ordinal))
            && command.StartsWith(ownedIdentity.Provider == "VBM" ? "/vbmai " : "/bmrai ", StringComparison.Ordinal);
        if (relevant && cleanupFailed && !cleanupStarted)
            return false;
        var before = BossModSettingsSnapshot.Unavailable;
        var canObserve = relevant && TryReadOwnedBossModSettings(out before, allowCleanup: cleanupStarted);
        if (relevant && !canObserve)
            return false;
        var submitted = sendCommand(command);
        if (submitted && canObserve && TryReadOwnedBossModSettings(out var after, allowCleanup: cleanupStarted))
        {
            var enabled = !command.EndsWith(" off", StringComparison.Ordinal);
            if (cleanupStarted && BossModRuntimePresetState.Matches(before.Runtime, cleanupRuntimeExpected)
                && BossModSettingsOwnership.IsAiCommandRuntimeEffect(ownedIdentity.Provider, before, after, enabled))
                cleanupRuntimeExpected = after.Runtime;
            ownedSettings!.ObserveAiCommand(before, after, enabled);
        }
        return submitted;
    }

    internal bool BeginOwnedBossModCleanup(string account, string character)
    {
        cleanupStarted = ownedSettings is not null && ownedIdentity.Account == account && ownedIdentity.Character == character;
        cleanupRuntimeExpected = null;
        cleanupRuntimeTarget = null;
        if (!cleanupStarted)
            return !cleanupFailed;
        cleanupFailed = false;
        if (!TryReadOwnedBossModSettings(out var current, allowCleanup: true))
        {
            cleanupFailed = true;
            return false;
        }
        cleanupRuntimeExpected = current.Runtime;
        cleanupRuntimeTarget = ownedSettings!.GetRuntimeCleanupTarget(current, turnEverythingOff: false);
        if (ownedSettings.OwnedRuntime is not null && current.Runtime is null)
        {
            LastStatus = "BossMod runtime preset restoration retained: current state is unreadable.";
            cleanupFailed = true;
        }
        if (ownedSettings.OwnedStoredSelector is { } selector
            && current.StoredSelectorReadable && current.StoredAiSelector == selector
            && ownedSettings.Original.StoredSelectorReadable)
        {
            var ownership = ownedSettings;
            var originalSelector = ownership.Original.StoredAiSelector;
            if (!IsRestorableAiSelector(originalSelector, ReadPresetCatalog(ownedIdentity.Provider)))
            {
                LastStatus = "BMR saved AI selector restoration could not be confirmed.";
                cleanupFailed = true;
            }
            else
            {
                _ = WriteAiSelector(originalSelector ?? string.Empty);
                if (!TryReadOwnedBossModSettings(out var after, allowCleanup: true)
                    || !ReferenceEquals(ownedSettings, ownership))
                {
                    LastStatus = "BMR saved AI selector restoration could not be confirmed.";
                    cleanupFailed = true;
                    return false;
                }
                if (after.Runtime is not null && !BossModRuntimePresetState.Matches(cleanupRuntimeExpected, after.Runtime))
                {
                    cleanupRuntimeExpected = after.Runtime;
                    cleanupRuntimeTarget = ownership.GetRuntimeCleanupTarget(after, turnEverythingOff: false);
                }
                current = after;
                if (!after.StoredSelectorReadable || after.StoredAiSelector != originalSelector)
                {
                    LastStatus = "BMR saved AI selector restoration could not be confirmed.";
                    cleanupFailed = true;
                }
                else
                    ownership.OwnStoredSelector(originalSelector);
            }
        }
        else if (ownedSettings.OwnedStoredSelector is not null && !current.StoredSelectorReadable)
        {
            LastStatus = "BMR saved AI selector restoration retained: current state is unreadable.";
            cleanupFailed = true;
        }
        if (ownedSettings.OwnedDistance is { } distance && current.PreferredDistance == distance
            && ownedSettings.Original.PreferredDistance is { } originalDistance)
        {
            try
            {
                pluginInterface.GetIpcSubscriber<List<string>, bool, List<string>>("BossMod.Configuration")
                    .InvokeFunc(new List<string> { "AIConfig", "PreferredDistance", originalDistance.ToString("R", CultureInfo.CurrentCulture) }, true);
                if (!TryReadOwnedBossModSettings(out var after, allowCleanup: true) || after.PreferredDistance != originalDistance)
                {
                    LastStatus = "Preferred-distance restoration could not be confirmed.";
                    cleanupFailed = true;
                }
            }
            catch { LastStatus = "Preferred-distance restoration endpoint is unavailable."; cleanupFailed = true; }
        }
        else if (ownedSettings.OwnedDistance is not null && current.PreferredDistance is null)
        {
            LastStatus = "Preferred-distance restoration retained: current state is unreadable.";
            cleanupFailed = true;
        }
        return !cleanupFailed;
    }

    internal bool EndOwnedBossModCleanup(bool turnEverythingOff)
    {
        if (!cleanupStarted || ownedSettings is null)
            return !cleanupFailed;
        if (cleanupRuntimeExpected is not null)
        {
            if (!TryReadOwnedBossModSettings(out var current, allowCleanup: true) || current.Runtime is null)
                cleanupFailed = true;
            else if (BossModRuntimePresetState.Matches(current.Runtime, cleanupRuntimeExpected)
                || ownedSettings.MatchesKnownAiRuntimeEffect(cleanupRuntimeExpected, current))
            {
                var target = turnEverythingOff ? new BossModRuntimePresetState(true, Array.Empty<string>()) : cleanupRuntimeTarget;
                if (target is not null && !BossModRuntimePresetState.Matches(current.Runtime, target))
                {
                    var ownership = ownedSettings;
                    var namesAvailable = target.Names.All(IsExactRuntimePreset);
                    if (namesAvailable) WriteRuntimePreset(ownedIdentity.Provider, target);
                    if (!namesAvailable || !TryReadOwnedBossModSettings(out var restored, allowCleanup: true)
                        || !ReferenceEquals(ownedSettings, ownership) || !BossModRuntimePresetState.Matches(restored.Runtime, target))
                    {
                        LastStatus = "BossMod runtime preset cleanup could not be confirmed.";
                        cleanupFailed = true;
                    }
                }
            }
        }
        var succeeded = !cleanupFailed;
        if (succeeded)
        {
            ownedSettings = null;
            ownedProvider = null;
            ownedIdentity = default;
        }
        cleanupStarted = false;
        cleanupRuntimeExpected = null;
        cleanupRuntimeTarget = null;
        return succeeded;
    }

    internal void ReleaseOwnedBossModSettings(bool turnEverythingOff)
    {
        if (ownedSettings is null)
            return;
        var succeeded = BeginOwnedBossModCleanup(ownedIdentity.Account, ownedIdentity.Character);
        succeeded &= EndOwnedBossModCleanup(turnEverythingOff);
        if (!succeeded)
            log.Warning($"[FrenRider] Owned BossMod settings cleanup was incomplete: {LastStatus}");
    }

    internal void ObserveOwnedBossModIdentity(string rotationProvider, string account, string character, CharacterConfig config)
    {
        if (!cleanupStarted && !cleanupFailed && ownedSettings is not null
            && ownedIdentity != (account, character, config, GetBossModProvider(rotationProvider)))
            ReleaseOwnedBossModSettings(ownedIdentity.Config.CleanupMode == FrenRiderCleanupMode.TurnEverythingOff);
    }

    internal bool DisableOwnedBossModRuntime()
    {
        if (!TryReadOwnedBossModSettings(out var current) || !ownedSettings!.CanChangeRuntime(current))
            return false;
        var disabled = new BossModRuntimePresetState(true, Array.Empty<string>());
        _ = WriteRuntimePreset(ownedIdentity.Provider, disabled);
        if (!TryReadOwnedBossModSettings(out var after) || !BossModRuntimePresetState.Matches(after.Runtime, disabled))
            return false;
        ownedSettings.OwnRuntime(disabled);
        return true;
    }

    internal bool PauseOwnedVbm(Func<string, bool> sendCommand)
    {
        if (ownedIdentity.Provider != "VBM" || !TryReadOwnedBossModSettings(out var current) || current.AiEnabled is null
            || !ownedSettings!.CanChangeRuntime(current) || !ownedSettings.CanChangeAi(current))
            return false;
        if (current.AiEnabled != false)
        {
            if (!SendBossModAiCommand("/vbmai off", sendCommand)
                || !TryReadOwnedBossModSettings(out current) || current.AiEnabled != false)
                return false;
        }
        var disabled = new BossModRuntimePresetState(true, Array.Empty<string>());
        if (BossModRuntimePresetState.Matches(current.Runtime, disabled))
        {
            ownedSettings!.OwnRuntime(disabled);
            return true;
        }
        return DisableOwnedBossModRuntime();
    }

    internal bool CanResumeOwnedVbm()
        => ownedIdentity.Provider == "VBM" && TryReadOwnedBossModSettings(out var current)
            && ownedSettings!.CanChangeRuntime(current) && ownedSettings.CanChangeAi(current);

    internal bool ApplyUnownedBossModPreset(string rotationProvider, string name)
    {
        if (ownedSettings is not null && (cleanupStarted || cleanupFailed))
            return false;
        if (IsNoPreset(name))
            return true;
        var provider = GetBossModProvider(rotationProvider);
        if (!TryGetLiveBossMod(provider, out _, out var assembly, out var detail)
            || !IsExactRuntimePreset(name) || provider == "BMR" && !IsExactAiSelector(name, ReadPresetCatalog(provider)))
        {
            LastStatus = string.IsNullOrEmpty(detail) ? "The selected literal preset name is unavailable." : detail;
            return false;
        }
        if (provider == "BMR" && !WriteAiSelector(name))
            return false;
        var target = new BossModRuntimePresetState(false, new[] { name });
        _ = WriteRuntimePreset(provider, target);
        var after = ReadBossModSettings(provider, assembly!);
        return BossModRuntimePresetState.Matches(after.Runtime, target)
            && (provider != "BMR" || after.StoredSelectorReadable && after.StoredAiSelector == name);
    }

    /// <summary>
    /// Push the packaged autorot presets into BossMod-compatible rotation plugins.
    /// Tries the current BossMod IPC contract first, then legacy aliases if needed.
    /// </summary>
    public void CreatePresets(bool force = false, string rotationProvider = "BMR")
    {
        log.Information($"Starting autorot preset push (force={force})");
        if (!TryGetLiveBossMod(GetBossModProvider(rotationProvider), out _, out _, out var detail))
        {
            LastStatus = detail;
            return;
        }

        var before = BossModSettingsSnapshot.Unavailable;
        if (ownedSettings is not null && (!TryReadOwnedBossModSettings(out before) || before.Runtime is null
            || before.Runtime.Names.Any(name => !IsExactRuntimePreset(name))))
        {
            LastStatus = "Packaged presets retained: the current runtime selection has no verified restoration path.";
            return;
        }
        var runtimeWasOwned = ownedSettings?.CanChangeRuntime(before) == true;

        var presets = LoadPresetFiles();
        if (presets.Count == 0)
        {
            LastStatus = "No packaged presets found";
            log.Warning("Failed to create autorot presets - no packaged presets found");
            return;
        }

        var created = 0;
        foreach (var preset in presets)
        {
            if (TryCreatePreset(preset.Name, preset.Json, forceRecreate: true))
                created++;
        }

        if (before.Runtime is { } previous && TryReadOwnedBossModSettings(out var after)
            && !BossModRuntimePresetState.Matches(previous, after.Runtime))
        {
            // Native deletion can remove an active packaged preset. Preserve the complete
            // pre-recreation order rather than relying on GetActive's single-name view.
            if (runtimeWasOwned && after.Runtime is { ForceDisabled: false } changed
                && changed.Names.SequenceEqual(previous.Names.Where(name => !presets.Any(p => p.Name == name)), StringComparer.Ordinal))
                ownedSettings!.OwnRuntime(changed);
            _ = WriteRuntimePreset(ownedIdentity.Provider, previous);
            if (!TryReadOwnedBossModSettings(out var restored) || !BossModRuntimePresetState.Matches(restored.Runtime, previous))
            {
                LastStatus = "Packaged presets were refreshed, but the prior runtime selection could not be confirmed.";
                log.Warning(LastStatus);
                return;
            }
            if (runtimeWasOwned)
                ownedSettings!.OwnRuntime(previous);
        }

        if (created == PresetFileNames.Length)
        {
            LastStatus = "Six BossMod presets pushed";
            log.Information("All packaged autorot presets created successfully");
        }
        else if (created > 0)
        {
            LastStatus = $"Preset push partially succeeded ({created}/{PresetFileNames.Length})";
            log.Warning($"Autorot preset push partially succeeded ({created}/{PresetFileNames.Length})");
        }
        else
        {
            LastStatus = "No compatible BossMod preset IPC responded";
            log.Warning("Failed to create autorot presets - no rotation plugin IPC available");
        }
    }

    /// <summary>
    /// Force-activate a preset by name via IPC.
    /// </summary>
    public void ForcePreset(string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
            return;

        var handled = false;

        var result = TryBoolIpc("BossMod.Presets.SetActive", presetName);
        if (result.HasValue)
        {
            if (result.Value)
            {
                log.Information($"Preset '{presetName}' set active via BossMod IPC");
                handled = true;
            }
            else
                log.Warning($"BossMod.Presets.SetActive returned false for preset '{presetName}'");
        }

        result = TryBoolIpc("BossModReborn.Presets.SetActive", presetName);
        if (result.HasValue)
        {
            if (result.Value)
            {
                log.Information($"Preset '{presetName}' set active via BossModReborn IPC");
                handled = true;
            }
            else
                log.Warning($"BossModReborn.Presets.SetActive returned false for preset '{presetName}'");
        }

        var legacyResult = TryStringIpc("BossMod.Presets.ForceSet", presetName);
        if (legacyResult != null)
        {
            LogLegacyPresetResult("BossMod.Presets.ForceSet", presetName, legacyResult);
            handled = true;
        }

        legacyResult = TryStringIpc("BossModReborn.Presets.ForceSet", presetName);
        if (legacyResult != null)
        {
            LogLegacyPresetResult("BossModReborn.Presets.ForceSet", presetName, legacyResult);
            handled = true;
        }

        if (!handled)
            log.Warning($"No BossMod-compatible preset IPC responded while setting preset '{presetName}'");
    }

    /// <summary>
    /// Clear any forced preset.
    /// </summary>
    public void ClearForcedPreset()
    {
        var result = TryBoolIpc("BossMod.Presets.ClearActive");
        if (result.HasValue)
            return;

        result = TryBoolIpc("BossModReborn.Presets.ClearActive");
        if (result.HasValue)
            return;

        TryIpcAction("BossMod.Presets.ForceClear");
        TryIpcAction("BossModReborn.Presets.ForceClear");
    }

    public bool TrySetRsrMode(RsrStateCommandType mode)
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<RsrStateCommandType, object>("RotationSolverReborn.ChangeOperatingMode");
            subscriber.InvokeAction(mode);
            log.Debug($"RSR mode set via IPC: {mode}");
            return true;
        }
        catch (Exception ex)
        {
            log.Debug($"RSR mode IPC unavailable for {mode}: {ex.Message}");
            return false;
        }
    }

    public bool TrySetRsrHostileType(RsrTargetHostileType hostileType)
    {
        return TrySetRsrSetting("HostileType", hostileType.ToString());
    }

    internal bool TryCaptureDungeonRsrAggro(out DungeonRsrLiveOwnership? ownership)
        => TryCaptureRsrAggro(RsrTargetHostileType.AllTargetsCanAttack, out ownership);

    internal bool TryCaptureRsrAggro(RsrTargetHostileType target, out DungeonRsrLiveOwnership? ownership)
    {
        ownership = null;
        if (!TryReadLiveRsrTargeting(out var current, out var detail))
        {
            LastStatus = detail;
            return false;
        }
        if (current!.ExternalHostile is { } external && external != (byte)target)
        {
            LastStatus = "DAD dungeon RSR targeting retained: an active external targeting override has priority.";
            return false;
        }
        ownership = new DungeonRsrLiveOwnership(current);
        LastStatus = "DAD dungeon RSR targeting ownership accepted; application waits for eligible combat.";
        return true;
    }

    internal bool ApplyDungeonRsrAggro(DungeonRsrLiveOwnership ownership)
    {
        var applied = ApplyOwnedRsrAggro(ownership, RsrTargetHostileType.AllTargetsCanAttack);
        if (applied)
            LastStatus = "DAD dungeon All Attackable Targets confirmed without changing operating mode.";
        return applied;
    }

    internal bool TryObserveRsrAggroOwner(DungeonRsrLiveOwnership ownership, out bool matches)
    {
        var readable = TryReadLiveRsrTargeting(out var current, out var detail);
        matches = readable && ownership.MatchesIdentity(current!);
        if (!readable) LastStatus = detail;
        return readable;
    }

    internal bool ApplyOwnedRsrAggro(DungeonRsrLiveOwnership ownership, RsrTargetHostileType target)
    {
        if (!TryReadLiveRsrTargeting(out var current, out var detail) || !ownership.MatchesIdentity(current!))
        {
            LastStatus = string.IsNullOrEmpty(detail)
                ? "DAD dungeon RSR targeting retained: the provider, configuration or job changed."
                : detail;
            return false;
        }
        var all = (byte)target;
        if (current!.ExternalHostile is { } external && external != all)
        {
            LastStatus = "DAD dungeon RSR targeting retained: an active external targeting override has priority.";
            return false;
        }
        if (current.ConfiguredHostile != (ownership.ExpectedConfiguredHostile ?? ownership.OriginalConfiguredHostile))
        {
            LastStatus = "DAD dungeon RSR targeting retained: a newer configured targeting change is present.";
            return false;
        }
        if (current.ConfiguredHostile != all)
        {
            _ = TrySetRsrHostileType(target);
        }
        var readable = TryReadLiveRsrTargeting(out var after, out detail) && ownership.MatchesIdentity(after!);
        if (readable && after!.ConfiguredHostile == all && current.ConfiguredHostile != all)
            ownership.ExpectedConfiguredHostile = all;
        if (!readable || after!.ConfiguredHostile != all || after.EffectiveHostile != all)
        {
            LastStatus = string.IsNullOrEmpty(detail)
                ? "DAD dungeon RSR targeting write was not confirmed in configured and effective readback."
                : detail;
            return false;
        }
        LastStatus = $"RSR {target} confirmed without changing operating mode.";
        return true;
    }

    internal bool StopOwnedRsrAggro(DungeonRsrLiveOwnership ownership, RsrTargetHostileType savedTarget,
        out bool targetConfirmed)
    {
        targetConfirmed = false;
        if (!TryReadLiveRsrTargeting(out var current, out var detail) || !ownership.MatchesIdentity(current!))
        {
            LastStatus = string.IsNullOrEmpty(detail) ? "RSR targeting stop retained after a native identity change." : detail;
            return false;
        }
        if (current!.ConfiguredHostile != (ownership.ExpectedConfiguredHostile ?? ownership.OriginalConfiguredHostile)
            || current.ExternalHostile is { } external && external != (byte)savedTarget)
        {
            LastStatus = "RSR questing targeting released; newer native targeting is retained.";
            return true;
        }
        targetConfirmed = ApplyOwnedRsrAggro(ownership, savedTarget);
        return targetConfirmed;
    }

    internal bool ReleaseDungeonRsrAggro(DungeonRsrLiveOwnership ownership, bool preserveNewerSelection = false)
        => ReleaseOwnedRsrAggro(ownership, preserveNewerSelection);

    internal bool ReleaseOwnedRsrAggro(DungeonRsrLiveOwnership ownership, bool preserveNewerSelection = false)
    {
        if (preserveNewerSelection || ownership.ExpectedConfiguredHostile is null)
        {
            LastStatus = preserveNewerSelection
                ? "DAD dungeon RSR targeting released; the newer intentional selection is retained."
                : "DAD dungeon RSR targeting released without a live targeting write.";
            return true;
        }
        if (!TryReadLiveRsrTargeting(out var current, out var detail) || !ownership.MatchesIdentity(current!))
        {
            LastStatus = string.IsNullOrEmpty(detail)
                ? "DAD dungeon RSR targeting restoration is unavailable after a provider, configuration or job change."
                : detail;
            return false;
        }
        if (current!.ConfiguredHostile != ownership.ExpectedConfiguredHostile)
        {
            LastStatus = "DAD dungeon RSR targeting released; the newer live targeting value is retained.";
            return true;
        }
        _ = TrySetRsrHostileType((RsrTargetHostileType)ownership.OriginalConfiguredHostile);
        if (!TryReadLiveRsrTargeting(out var restored, out detail) || !ownership.MatchesIdentity(restored!)
            || restored!.ConfiguredHostile != ownership.OriginalConfiguredHostile
            || restored.EffectiveHostile != (restored.ExternalHostile ?? ownership.OriginalConfiguredHostile))
        {
            LastStatus = string.IsNullOrEmpty(detail)
                ? "DAD dungeon RSR targeting restoration was not confirmed."
                : detail;
            return false;
        }
        LastStatus = "DAD dungeon RSR configured targeting restored; active external overrides are retained.";
        return true;
    }

    private bool TryReadLiveRsrTargeting(out RsrLiveTargetingSnapshot? snapshot, out string detail)
    {
        snapshot = null;
        try
        {
            var loaded = pluginInterface.InstalledPlugins.Where(plugin => plugin.IsLoaded
                && plugin.InternalName == "RotationSolver").ToArray();
            if (loaded.Length != 1)
            {
                detail = "DAD dungeon RSR targeting is unavailable: exactly one loaded RotationSolver provider is required.";
                return false;
            }
            var instance = BossModExternalAutomationSnapshotProvider.FindLivePluginInstance(loaded[0], out var assembly, out detail);
            if (instance is null || assembly is null || assembly.GetName().Name != "RotationSolver"
                || instance.GetType().FullName != "RotationSolver.RotationSolverPlugin")
            {
                detail = "DAD dungeon RSR targeting is unavailable: the live provider identity is unsupported.";
                return false;
            }
            var reference = assembly.GetReferencedAssemblies().SingleOrDefault(name => name.Name == "RotationSolver.Basic");
            var basic = reference is null ? null : AssemblyLoadContext.GetLoadContext(assembly)?.Assemblies
                .SingleOrDefault(candidate => candidate.FullName == reference.FullName);
            if (basic is null)
            {
                detail = "DAD dungeon RSR targeting is unavailable: the provider's already-loaded Basic assembly is unavailable.";
                return false;
            }
            return TryReadRsrTargeting(instance, basic, out snapshot, out detail);
        }
        catch (Exception ex)
        {
            detail = $"DAD dungeon RSR targeting read is unavailable: {ex.GetBaseException().Message}";
            return false;
        }
    }

    internal static bool TryReadRsrTargeting(object provider, Assembly basic,
        out RsrLiveTargetingSnapshot? snapshot, out string detail)
    {
        snapshot = null;
        detail = "DAD dungeon RSR targeting is unavailable: the verified live member contract is unsupported.";
        try
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public;
            var service = basic.GetType("RotationSolver.Basic.Service");
            var data = basic.GetType("RotationSolver.Basic.DataCenter");
            var configs = basic.GetType("RotationSolver.Basic.Configuration.Configs");
            var hostileType = basic.GetType("RotationSolver.Basic.Data.TargetHostileType");
            var overrideType = basic.GetType("RotationSolver.Basic.Data.IpcStateOverrides");
            var configProperty = service?.GetProperty("Config", flags);
            var jobProperty = data?.GetProperty("Job", flags);
            var activeProperty = data?.GetProperty("ActiveIpcOverrides", flags);
            var effectiveProperty = data?.GetProperty("CurrentTargetToHostileType", flags);
            if (configs is null || hostileType is not { IsEnum: true } || Enum.GetUnderlyingType(hostileType) != typeof(byte)
                || overrideType is null || configProperty?.PropertyType != configs
                || jobProperty?.PropertyType.FullName != "ECommons.ExcelServices.Job" || !jobProperty.PropertyType.IsEnum
                || jobProperty.PropertyType.Assembly.GetName().Name != "ECommons"
                || activeProperty?.PropertyType != overrideType || effectiveProperty?.PropertyType != hostileType
                || configProperty.GetValue(null) is not { } config || config.GetType() != configs)
                return false;
            var configuredProperty = configs.GetProperty("HostileType", BindingFlags.Instance | BindingFlags.Public);
            var externalProperty = overrideType.GetProperty("HostileType", BindingFlags.Instance | BindingFlags.Public);
            if (configuredProperty?.PropertyType != hostileType
                || externalProperty?.PropertyType != typeof(Nullable<>).MakeGenericType(hostileType))
                return false;
            var job = Convert.ToUInt32(jobProperty.GetValue(null), CultureInfo.InvariantCulture);
            if (job == 0)
                return false;
            foreach (var expected in Enum.GetValues<RsrTargetHostileType>())
                if (!Enum.TryParse(hostileType, expected.ToString(), out var nativeValue)
                    || Convert.ToByte(nativeValue, CultureInfo.InvariantCulture) != (byte)expected)
                    return false;
            var configured = configuredProperty.GetValue(config);
            var effective = effectiveProperty.GetValue(null);
            var active = activeProperty.GetValue(null);
            if (active is not null && active.GetType() != overrideType)
                return false;
            var external = active is null ? null : externalProperty.GetValue(active);
            if (configured is null || effective is null || !Enum.IsDefined(hostileType, configured)
                || !Enum.IsDefined(hostileType, effective) || external is not null && !Enum.IsDefined(hostileType, external))
                return false;
            var configuredValue = Convert.ToByte(configured, CultureInfo.InvariantCulture);
            var effectiveValue = Convert.ToByte(effective, CultureInfo.InvariantCulture);
            var externalValue = external is null ? (byte?)null : Convert.ToByte(external, CultureInfo.InvariantCulture);
            if (!Enum.IsDefined(typeof(RsrTargetHostileType), configuredValue)
                || effectiveValue != (externalValue ?? configuredValue)
                || !ReferenceEquals(config, configProperty.GetValue(null))
                || job != Convert.ToUInt32(jobProperty.GetValue(null), CultureInfo.InvariantCulture))
                return false;
            snapshot = new RsrLiveTargetingSnapshot(provider, config, basic.FullName!, job,
                configuredValue, externalValue, effectiveValue);
            detail = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            detail = $"DAD dungeon RSR targeting read is unavailable: {ex.GetBaseException().Message}";
            return false;
        }
    }

    public bool TrySetRsrSupportTargeting(bool enabled)
    {
        return TrySetRsrSetting("FriendlyPartyNpcHealRaise3", enabled ? "true" : "false");
    }

    public bool TrySetRsrPoslockCasting(bool enabled)
    {
        return TrySetRsrSetting("PoslockCasting", enabled ? "true" : "false");
    }

    public bool TryGetDaedalusEnabled(out bool enabled)
    {
        enabled = false;

        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<bool>(DaedalusIsEnabledChannel);
            enabled = subscriber.InvokeFunc();
            log.Debug($"Daedalus enabled state read via IPC: {enabled}");
            return true;
        }
        catch (Exception ex)
        {
            log.Debug($"Daedalus enabled-state IPC unavailable: {ex.Message}");
            return false;
        }
    }

    public bool TrySetDaedalusEnabled(bool enabled)
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<bool, object>(DaedalusSetEnabledChannel);
            subscriber.InvokeAction(enabled);
            log.Debug($"Daedalus enabled state set via IPC: {enabled}");
            return true;
        }
        catch (Exception ex)
        {
            log.Debug($"Daedalus SetEnabled IPC unavailable for {enabled}: {ex.Message}");
            return false;
        }
    }

    private bool TrySetRsrSetting(string settingName, string value)
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<RsrOtherCommandType, string, object>("RotationSolverReborn.OtherCommand");
            subscriber.InvokeAction(RsrOtherCommandType.Settings, $"{settingName} {value}");
            log.Debug($"RSR setting applied via IPC: {settingName}={value}");
            return true;
        }
        catch (Exception ex)
        {
            log.Debug($"RSR setting IPC unavailable for {settingName}={value}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Set Automaton (CBT) tweak state via IPC.
    /// </summary>
    public bool SetAutomatonTweakState(string tweak, bool enabled)
    {
        try
        {
            // Use simple action call for Automaton IPC - no return value needed
            TryIpcAction<string, bool>("Automaton.SetTweakState", tweak, enabled);
            log.Information($"[IPC] Set Automaton tweak {tweak}={enabled}");
            return true;
        }
        catch (Exception ex)
        {
            log.Error($"[IPC] Failed to set Automaton tweak {tweak}={enabled}: {ex.Message}");
            return false;
        }
    }

    private IReadOnlyList<AutorotPreset> LoadPresetFiles()
    {
        var presetDir = GetPresetDirectory();
        if (!Directory.Exists(presetDir))
        {
            log.Warning($"BossMod preset directory not found: {presetDir}");
            return Array.Empty<AutorotPreset>();
        }

        var presets = new List<AutorotPreset>(PresetFileNames.Length);
        foreach (var fileName in PresetFileNames)
        {
            var path = Path.Combine(presetDir, fileName);
            if (!File.Exists(path))
            {
                log.Warning($"Packaged BossMod preset missing: {path}");
                continue;
            }

            try
            {
                var json = File.ReadAllText(path);
                var name = ReadPresetName(json);
                if (string.IsNullOrWhiteSpace(name))
                {
                    log.Warning($"Packaged BossMod preset has no Name property: {path}");
                    continue;
                }

                presets.Add(new AutorotPreset(name, json));
            }
            catch (Exception ex)
            {
                log.Warning(ex, $"Failed to read packaged BossMod preset: {path}");
            }
        }

        return presets;
    }

    private string GetPresetDirectory()
    {
        var assemblyDir = Path.GetDirectoryName(pluginInterface.AssemblyLocation.FullName);
        if (!string.IsNullOrWhiteSpace(assemblyDir))
            return Path.Combine(assemblyDir, "data", "bm");

        return Path.Combine(AppContext.BaseDirectory, "data", "bm");
    }

    private static string? ReadPresetName(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("Name", out var nameElement)
            ? nameElement.GetString()
            : null;
    }

    private bool TryCreatePreset(string name, string json, bool forceRecreate)
    {
        string? previouslyActivePreset = null;

        if (forceRecreate)
        {
            var existingPreset = TryStringIpc("BossMod.Presets.Get", name);
            if (existingPreset != null)
            {
                previouslyActivePreset = TryStringIpc("BossMod.Presets.GetActive");

                var deleteResult = TryBoolIpc("BossMod.Presets.Delete", name);
                if (deleteResult.HasValue)
                {
                    if (deleteResult.Value)
                        log.Information($"Preset '{name}' deleted before recreate via BossMod-compatible IPC");
                    else
                        log.Warning($"BossMod.Presets.Delete returned false for preset '{name}' before recreate");
                }
            }
        }

        var result = TryBoolIpc("BossMod.Presets.Create", json, true);
        if (result.HasValue)
        {
            if (result.Value)
            {
                log.Information($"Preset '{name}' created via BossMod-compatible IPC");

                if (!string.IsNullOrEmpty(previouslyActivePreset) &&
                    string.Equals(previouslyActivePreset, name, StringComparison.OrdinalIgnoreCase))
                {
                    var reactivateResult = TryBoolIpc("BossMod.Presets.SetActive", name);
                    if (reactivateResult == true)
                        log.Information($"Preset '{name}' restored as active after recreate");
                    else if (reactivateResult == false)
                        log.Warning($"BossMod.Presets.SetActive returned false while restoring preset '{name}' after recreate");
                }

                return true;
            }

            log.Warning($"BossMod.Presets.Create returned false for preset '{name}'");
            return false;
        }

        var legacyResult = TryStringIpc("BossMod.Presets.Create", json);
        if (legacyResult != null)
        {
            LogLegacyPresetResult("BossMod.Presets.Create", name, legacyResult);
            return true;
        }

        legacyResult = TryStringIpc("BossModReborn.Presets.Create", json);
        if (legacyResult != null)
        {
            LogLegacyPresetResult("BossModReborn.Presets.Create", name, legacyResult);
            return true;
        }

        return false;
    }

    private bool? TryBoolIpc(string channel)
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<bool>(channel);
            return subscriber.InvokeFunc();
        }
        catch (Exception ex)
        {
            log.Debug($"IPC {channel} not available: {ex.Message}");
            return null;
        }
    }

    private bool? TryBoolIpc<TArg>(string channel, TArg arg)
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<TArg, bool>(channel);
            return subscriber.InvokeFunc(arg);
        }
        catch (Exception ex)
        {
            log.Debug($"IPC {channel} not available: {ex.Message}");
            return null;
        }
    }

    private bool? TryBoolIpc<TArg1, TArg2>(string channel, TArg1 arg1, TArg2 arg2)
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<TArg1, TArg2, bool>(channel);
            return subscriber.InvokeFunc(arg1, arg2);
        }
        catch (Exception ex)
        {
            log.Debug($"IPC {channel} not available: {ex.Message}");
            return null;
        }
    }

    private string? TryStringIpc<TArg>(string channel, TArg arg)
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<TArg, string>(channel);
            return subscriber.InvokeFunc(arg);
        }
        catch (Exception ex)
        {
            log.Debug($"IPC {channel} not available: {ex.Message}");
            return null;
        }
    }

    private string? TryStringIpc(string channel)
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<string>(channel);
            return subscriber.InvokeFunc();
        }
        catch (Exception ex)
        {
            log.Debug($"IPC {channel} not available: {ex.Message}");
            return null;
        }
    }

    private void TryIpcAction(string channel)
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<object?>(channel);
            subscriber.InvokeFunc();
        }
        catch (Exception ex)
        {
            log.Debug($"IPC {channel} not available: {ex.Message}");
        }
    }

    private void TryIpcAction<TArg>(string channel, TArg arg)
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<TArg, object?>(channel);
            subscriber.InvokeFunc(arg);
        }
        catch (Exception ex)
        {
            log.Debug($"IPC {channel} not available: {ex.Message}");
        }
    }

    private void TryIpcAction<TArg1, TArg2>(string channel, TArg1 arg1, TArg2 arg2)
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<TArg1, TArg2, object?>(channel);
            subscriber.InvokeFunc(arg1, arg2);
        }
        catch (Exception ex)
        {
            log.Debug($"IPC {channel} not available: {ex.Message}");
        }
    }

    private void LogLegacyPresetResult(string channel, string presetName, string result)
    {
        if (result.Length == 0)
            log.Information($"Preset '{presetName}' handled via legacy IPC channel {channel}");
        else
            log.Warning($"Legacy IPC {channel} returned '{result}' for preset '{presetName}'");
    }

    public void Dispose()
    {
        ReleaseOwnedBossModSettings(ownedSettings is not null
            && ownedIdentity.Config.CleanupMode == FrenRiderCleanupMode.TurnEverythingOff);
    }

    private sealed record AutorotPreset(string Name, string Json);
}

public sealed record BossModPresetCatalog(string Provider, bool Readable,
    IReadOnlyList<string> DisplayedNames, IReadOnlyList<string> DefinitionNames,
    IReadOnlyList<string> NativeNames, string Detail)
{
    internal static BossModPresetCatalog Unavailable(string provider, string detail)
        => new(provider, false, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), detail);
}

internal sealed record BossModRuntimePresetState(bool ForceDisabled, IReadOnlyList<string> Names)
{
    internal static bool Matches(BossModRuntimePresetState? first, BossModRuntimePresetState? second)
        => first is not null && second is not null && first.ForceDisabled == second.ForceDisabled
            && first.Names.SequenceEqual(second.Names, StringComparer.Ordinal);
}

internal sealed record RsrLiveTargetingSnapshot(object Provider, object Config, string BasicAssemblyIdentity,
    uint Job, byte ConfiguredHostile, byte? ExternalHostile, byte EffectiveHostile);

internal sealed class DungeonRsrLiveOwnership(RsrLiveTargetingSnapshot original)
{
    private readonly WeakReference<object> provider = new(original.Provider);
    private readonly WeakReference<object> config = new(original.Config);
    private readonly uint job = original.Job;
    private readonly string basicAssemblyIdentity = original.BasicAssemblyIdentity;
    internal byte OriginalConfiguredHostile { get; } = original.ConfiguredHostile;
    internal byte? ExpectedConfiguredHostile { get; set; }
    internal bool MatchesIdentity(RsrLiveTargetingSnapshot current)
        => current is not null && current.Job == job && current.BasicAssemblyIdentity == basicAssemblyIdentity
            && provider.TryGetTarget(out var capturedProvider) && ReferenceEquals(capturedProvider, current.Provider)
            && config.TryGetTarget(out var capturedConfig) && ReferenceEquals(capturedConfig, current.Config);
}

internal sealed record BossModSettingsSnapshot(BossModRuntimePresetState? Runtime,
    bool StoredSelectorReadable, string? StoredAiSelector, double? PreferredDistance, bool? AiEnabled)
{
    internal static BossModSettingsSnapshot Unavailable { get; } = new(null, false, null, null, null);
}

internal sealed class BossModSettingsOwnership(string provider, BossModSettingsSnapshot original)
{
    internal BossModSettingsSnapshot Original { get; } = original;
    internal BossModRuntimePresetState? OwnedRuntime { get; private set; }
    internal string? OwnedStoredSelector { get; private set; }
    internal double? OwnedDistance { get; private set; }
    private bool? ownedAiEnabled;

    internal bool CanChangeRuntime(BossModSettingsSnapshot current)
        => Original.Runtime is not null && current.Runtime is not null
            && (OwnedRuntime is null ? BossModRuntimePresetState.Matches(current.Runtime, Original.Runtime)
                : MatchesOwnedRuntime(current));
    internal bool CanChangeStoredSelector(BossModSettingsSnapshot current)
        => Original.StoredSelectorReadable && current.StoredSelectorReadable
            && string.Equals(current.StoredAiSelector, OwnedStoredSelector ?? Original.StoredAiSelector, StringComparison.Ordinal);
    internal bool CanChangeDistance(double? current)
        => Original.PreferredDistance is { } originalDistance && double.IsFinite(originalDistance)
            && current == (OwnedDistance ?? originalDistance);
    internal bool CanChangeAi(BossModSettingsSnapshot current)
        => current.AiEnabled is not null && current.AiEnabled == (ownedAiEnabled ?? Original.AiEnabled);
    internal void OwnRuntime(BossModRuntimePresetState state) => OwnedRuntime = state;
    internal void OwnStoredSelector(string? selector) => OwnedStoredSelector = selector;
    internal void OwnDistance(double distance) => OwnedDistance = distance;
    internal bool MatchesOwnedRuntime(BossModSettingsSnapshot current)
    {
        if (BossModRuntimePresetState.Matches(current.Runtime, OwnedRuntime))
            return true;
        return OwnedRuntime is { } owned && MatchesKnownAiRuntimeEffect(owned, current);
    }
    internal BossModRuntimePresetState? GetRuntimeCleanupTarget(BossModSettingsSnapshot current, bool turnEverythingOff)
        => current.Runtime is null ? null : turnEverythingOff
            ? new BossModRuntimePresetState(true, Array.Empty<string>())
            : MatchesOwnedRuntime(current) ? Original.Runtime : current.Runtime;
    internal bool MatchesKnownAiRuntimeEffect(BossModRuntimePresetState previous, BossModSettingsSnapshot current)
    {
        if (ownedAiEnabled is null || current.AiEnabled != ownedAiEnabled || previous.ForceDisabled
            || current.Runtime is not { ForceDisabled: false } runtime)
            return false;
        if (provider == "BMR")
            return ownedAiEnabled == true && OwnedStoredSelector is { } selector
                && current.StoredSelectorReadable && current.StoredAiSelector == selector
                && runtime.Names.Count == 1 && runtime.Names[0] == selector;
        // VBM rebuilds only its hidden Multibox entry, appending it when AI is on.
        // The order and exact names of every other active preset must still match.
        var names = previous.Names.Where(name => name != "VBM Multibox");
        return runtime.Names.SequenceEqual(ownedAiEnabled == true ? names.Append("VBM Multibox") : names, StringComparer.Ordinal);
    }
    internal static bool IsAiCommandRuntimeEffect(string provider, BossModSettingsSnapshot before,
        BossModSettingsSnapshot after, bool enabled)
    {
        if (after.AiEnabled != enabled || after.Runtime is null || before.Runtime is null)
            return false;
        if (BossModRuntimePresetState.Matches(before.Runtime, after.Runtime))
            return true;
        if (provider == "VBM" && !before.Runtime.ForceDisabled && !after.Runtime.ForceDisabled)
        {
            var names = before.Runtime.Names.Where(name => name != "VBM Multibox");
            return after.Runtime.Names.SequenceEqual(enabled ? names.Append("VBM Multibox") : names, StringComparer.Ordinal);
        }
        return provider == "BMR" && after.Runtime is { ForceDisabled: false, Names.Count: 0 };
    }
    internal void ObserveAiCommand(BossModSettingsSnapshot before, BossModSettingsSnapshot after, bool enabled)
    {
        var matched = CanChangeRuntime(before);
        if (after.AiEnabled == enabled)
            ownedAiEnabled = enabled;
        if (!matched || !IsAiCommandRuntimeEffect(provider, before, after, enabled))
            return;
        OwnedRuntime = after.Runtime;
    }
}
