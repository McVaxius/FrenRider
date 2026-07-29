using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

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

    /// <summary>
    /// Push the packaged autorot presets into BossMod-compatible rotation plugins.
    /// Tries the current BossMod IPC contract first, then legacy aliases if needed.
    /// </summary>
    public void CreatePresets(bool force = false)
    {
        log.Information($"Starting autorot preset push (force={force})");

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
        // Nothing to clean up - presets persist in BMR/VBM
    }

    private sealed record AutorotPreset(string Name, string Json);
}
