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
    /// Push the embedded autorot presets into BMR/VBM.
    /// Tries both BossMod (VBM) and BossModReborn (BMR) IPC channels.
    /// </summary>
    public void CreatePresets()
    {
        if (presetsCreated) return;

        var created = false;
        created |= TryCreatePreset("FRENRIDER", FrenRiderPresetJson);
        created |= TryCreatePreset("DD", DdPresetJson);

        if (created)
        {
            presetsCreated = true;
            LastStatus = "Presets pushed to rotation plugin";
            log.Information("Autorot presets created successfully");
        }
        else
        {
            LastStatus = "No rotation plugin responded to IPC";
            log.Warning("Failed to create autorot presets - no rotation plugin IPC available");
        }
    }

    /// <summary>
    /// Force-activate a preset by name via IPC.
    /// </summary>
    public void ForcePreset(string presetJson)
    {
        var result = TryIpc<string, string>("BossMod.Presets.ForceSet", presetJson);
        if (result == null)
            result = TryIpc<string, string>("BossModReborn.Presets.ForceSet", presetJson);

        if (result != null && result.Length == 0)
            log.Information("Preset force-set via IPC");
        else if (result != null)
            log.Warning($"Preset force-set returned: {result}");
    }

    /// <summary>
    /// Clear any forced preset.
    /// </summary>
    public void ClearForcedPreset()
    {
        TryIpcAction("BossMod.Presets.ForceClear");
        TryIpcAction("BossModReborn.Presets.ForceClear");
    }

    private bool TryCreatePreset(string name, string json)
    {
        // Try VBM first, then BMR
        var result = TryIpc<string, string>("BossMod.Presets.Create", json);
        if (result != null)
        {
            if (result.Length == 0)
            {
                log.Information($"Preset '{name}' created via BossMod (VBM) IPC");
                return true;
            }
            else
            {
                log.Debug($"BossMod.Presets.Create for '{name}': {result}");
                // Non-empty result may mean preset already exists, which is fine
                return true;
            }
        }

        result = TryIpc<string, string>("BossModReborn.Presets.Create", json);
        if (result != null)
        {
            if (result.Length == 0)
            {
                log.Information($"Preset '{name}' created via BossModReborn (BMR) IPC");
                return true;
            }
            else
            {
                log.Debug($"BossModReborn.Presets.Create for '{name}': {result}");
                return true;
            }
        }

        return false;
    }

    private TResult? TryIpc<TArg, TResult>(string channel, TArg arg) where TResult : class
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<TArg, TResult>(channel);
            return subscriber.InvokeFunc(arg);
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
