using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace FrenRider.Services;

/// <summary>
/// Manages IPC communication with BMR/VBM to create and activate autorotation presets.
/// Embeds FRENRIDER and DD preset JSONs and pushes them via IPC when requested.
/// </summary>
public class AutorotIpcService : IDisposable
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private bool presetsCreated;

    public string LastStatus { get; private set; } = "";

    public AutorotIpcService(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
    }

    /// <summary>
    /// Push the embedded autorot presets into BossMod-compatible rotation plugins.
    /// Tries the current BossMod IPC contract first, then legacy aliases if needed.
    /// </summary>
    public void CreatePresets(bool force = false)
    {
        if (presetsCreated && !force)
        {
            LastStatus = "Presets already pushed this session";
            log.Debug("Skipping autorot preset push because presets were already created this session");
            return;
        }

        log.Information($"Starting autorot preset push (force={force})");

        var frenRiderCreated = TryCreatePreset("FRENRIDER", FrenRiderPresetJson, force);
        var ddCreated = TryCreatePreset("DD", DdPresetJson, force);

        if (frenRiderCreated && ddCreated)
        {
            presetsCreated = true;
            LastStatus = "Presets pushed to rotation plugin";
            log.Information("Autorot presets created successfully");
        }
        else if (frenRiderCreated || ddCreated)
        {
            LastStatus = "Preset push partially succeeded";
            log.Warning("Autorot preset push partially succeeded");
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

        var result = TryBoolIpc("BossMod.Presets.SetActive", presetName);
        if (result.HasValue)
        {
            if (result.Value)
                log.Information($"Preset '{presetName}' set active via BossMod IPC");
            else
                log.Warning($"BossMod.Presets.SetActive returned false for preset '{presetName}'");
            return;
        }

        result = TryBoolIpc("BossModReborn.Presets.SetActive", presetName);
        if (result.HasValue)
        {
            if (result.Value)
                log.Information($"Preset '{presetName}' set active via BossModReborn IPC");
            else
                log.Warning($"BossModReborn.Presets.SetActive returned false for preset '{presetName}'");
            return;
        }

        var legacyResult = TryStringIpc("BossMod.Presets.ForceSet", presetName);
        if (legacyResult != null)
        {
            LogLegacyPresetResult("BossMod.Presets.ForceSet", presetName, legacyResult);
            return;
        }

        legacyResult = TryStringIpc("BossModReborn.Presets.ForceSet", presetName);
        if (legacyResult != null)
            LogLegacyPresetResult("BossModReborn.Presets.ForceSet", presetName, legacyResult);
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

    // ===== Embedded Preset JSONs =====

    private const string FrenRiderPresetJson = """
{"Name":"FRENRIDER","Modules":{"BossMod.Autorotation.xan.BLM":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"}],"BossMod.Autorotation.xan.SMN":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"}],"BossMod.Autorotation.xan.PCT":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"}],"BossMod.Autorotation.xan.RDM":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"}],"BossMod.Autorotation.xan.AST":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"}],"BossMod.Autorotation.xan.SGE":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"}],"BossMod.Autorotation.xan.WHM":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"}],"BossMod.Autorotation.xan.DRG":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"}],"BossMod.Autorotation.xan.MNK":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"}],"BossMod.Autorotation.xan.NIN":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"}],"BossMod.Autorotation.xan.RPR":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"}],"BossMod.Autorotation.xan.SAM":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"}],"BossMod.Autorotation.xan.VPR":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"}],"BossMod.Autorotation.xan.DNC":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"}],"BossMod.Autorotation.xan.MCH":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"}],"BossMod.Autorotation.xan.DRK":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"}],"BossMod.Autorotation.xan.GNB":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"}],"BossMod.Autorotation.VeynWAR":[{"Track":"AOE","Option":"AutoFinishCombo"},{"Track":"Burst","Option":"Spend"},{"Track":"Potion","Option":"Manual"},{"Track":"Infuriate","Option":"ForceIfNoNC"},{"Track":"IR","Option":"Automatic"},{"Track":"Upheaval","Option":"Automatic"},{"Track":"PR","Option":"Automatic"},{"Track":"Onslaught","Option":"Force"},{"Track":"Tomahawk","Option":"Opener"},{"Track":"Wrath","Option":"Automatic"}],"BossMod.Autorotation.xan.TankAI":[{"Track":"Stance","Option":"Disabled"},{"Track":"Personal mits","Option":"Disabled"},{"Track":"Invuln","Option":"Disabled"}],"BossMod.Autorotation.VeynBRD":[],"BossMod.Autorotation.xan.HealerAI":[{"Track":"Raise","Option":"Slowcast"},{"Track":"RaiseTargets","Option":"Everyone"},{"Track":"Esuna2","Option":"Enabled"}],"BossMod.Autorotation.xan.MeleeAI":[],"BossMod.Autorotation.xan.RangedAI":[],"BossMod.Autorotation.akechi.AkechiPLD":[{"Track":"Dash","Option":"Delay"}],"BossMod.Autorotation.xan.SCH":[],"BossMod.Autorotation.xan.PhantomAI":[{"Track":"Chemist","Option":"InCombat"}],"BossMod.Autorotation.xan.Caster":[{"Track":"Raise","Option":"Slowcast"}],"BossMod.Autorotation.xan.BozjaAI":[],"BossMod.Autorotation.MiscAI.NormalMovement":[{"Track":"Cast","Option":"Leeway"},{"Track":"Destination","Option":"Pathfind"},{"Track":"SpecialModes","Option":"Automatic"}]}}
""";

    private const string DdPresetJson = """
{"Name":"DD","Modules":{"BossMod.Autorotation.xan.BLM":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"}],"BossMod.Autorotation.xan.SMN":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"},{"Track":"Buffs","Option":"Auto"}],"BossMod.Autorotation.xan.PCT":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"},{"Track":"Buffs","Option":"Auto"}],"BossMod.Autorotation.xan.RDM":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"},{"Track":"Buffs","Option":"Auto"}],"BossMod.Autorotation.xan.AST":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"},{"Track":"Buffs","Option":"Auto"}],"BossMod.Autorotation.xan.SGE":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"},{"Track":"Buffs","Option":"Auto"}],"BossMod.Autorotation.xan.WHM":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"},{"Track":"Buffs","Option":"Auto"}],"BossMod.Autorotation.xan.DRG":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"},{"Track":"Buffs","Option":"Auto"}],"BossMod.Autorotation.xan.MNK":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"}],"BossMod.Autorotation.xan.NIN":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"},{"Track":"Buffs","Option":"Auto"}],"BossMod.Autorotation.xan.RPR":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"},{"Track":"Buffs","Option":"Auto"}],"BossMod.Autorotation.xan.SAM":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"},{"Track":"Buffs","Option":"Auto"}],"BossMod.Autorotation.xan.VPR":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"},{"Track":"Buffs","Option":"Auto"}],"BossMod.Autorotation.xan.DNC":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"},{"Track":"Buffs","Option":"Auto"}],"BossMod.Autorotation.xan.MCH":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"},{"Track":"Buffs","Option":"Auto"}],"BossMod.Autorotation.xan.DRK":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"},{"Track":"Buffs","Option":"Auto"}],"BossMod.Autorotation.xan.GNB":[{"Track":"Targeting","Option":"Auto"},{"Track":"AOE","Option":"AOE"},{"Track":"Buffs","Option":"Auto"}],"BossMod.Autorotation.VeynWAR":[{"Track":"AOE","Option":"AutoFinishCombo"},{"Track":"Burst","Option":"Spend"},{"Track":"Potion","Option":"Manual"},{"Track":"Infuriate","Option":"ForceIfNoNC"},{"Track":"IR","Option":"Automatic"},{"Track":"Upheaval","Option":"Automatic"},{"Track":"PR","Option":"Automatic"},{"Track":"Onslaught","Option":"Force"},{"Track":"Tomahawk","Option":"Opener"},{"Track":"Wrath","Option":"Automatic"}],"BossMod.Autorotation.xan.TankAI":[],"BossMod.Autorotation.xan.DeepDungeonAI":[{"Track":"Kite enemies","Option":"Disabled"}],"BossMod.Autorotation.xan.HealerAI":[{"Track":"Raise","Option":"Raise without requiring Swiftcast to be available"},{"Track":"RaiseTargets","Option":"Any dead player"}],"BossMod.Autorotation.xan.MeleeAI":[],"BossMod.Autorotation.xan.RangedAI":[],"BossMod.Autorotation.VeynBRD":[],"BossMod.Autorotation.akechi.AkechiPLD":[],"BossMod.Autorotation.xan.SCH":[],"BossMod.Autorotation.xan.Caster":[{"Track":"Raise","Option":"Allow raising without Swiftcast (not applicable to RDM)"},{"Track":"RaiseTargets","Option":"Any dead player"}],"BossMod.Autorotation.MiscAI.NormalMovement":[{"Track":"Cast","Option":"Leeway"}]}}
""";
}
