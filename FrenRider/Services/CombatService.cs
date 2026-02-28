using System;
using Dalamud.Game.ClientState.Conditions;
using FrenRider.Models;

namespace FrenRider.Services;

public enum CombatState
{
    OutOfCombat,
    EnteringCombat,  // Just entered combat, activating rotation
    InCombat,        // Active combat with rotation running
    LeavingCombat,   // Just left combat, deactivating rotation
}

public class CombatService
{
    private readonly Plugin plugin;
    private readonly FrenTracker tracker;
    private readonly ZoneService zoneService;

    private bool wasInCombat;
    private long lastRotationToggleMs;

    private static readonly string[] RotationPluginNames = { "BMR", "VBM", "RSR", "WRATH" };

    public CombatState State { get; private set; } = CombatState.OutOfCombat;
    public string StateDetail { get; private set; } = "";
    public string ActivePreset { get; private set; } = "";

    public CombatService(Plugin plugin, FrenTracker tracker, ZoneService zoneService)
    {
        this.plugin = plugin;
        this.tracker = tracker;
        this.zoneService = zoneService;
    }

    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();

        // Zone transition: deactivate rotation and reset
        if (zoneService.ZoneChanged)
        {
            if (wasInCombat) DeactivateRotation(config);
            State = CombatState.OutOfCombat;
            StateDetail = "Zone transition";
            wasInCombat = false;
            return;
        }

        if (!config.Enabled)
        {
            if (wasInCombat) DeactivateRotation(config);
            State = CombatState.OutOfCombat;
            StateDetail = "Disabled";
            wasInCombat = false;
            return;
        }

        var inCombat = Plugin.Condition[ConditionFlag.InCombat];
        var now = Environment.TickCount64;

        // Entered combat
        if (inCombat && !wasInCombat)
        {
            wasInCombat = true;
            State = CombatState.EnteringCombat;

            if (config.RotationType != 2) // 2 = none
            {
                ActivateRotation(config);
            }

            if (config.BossModAI == 0) // 0 = on
            {
                ToggleBossModAI(true);
            }
        }
        // Left combat
        else if (!inCombat && wasInCombat)
        {
            wasInCombat = false;
            State = CombatState.LeavingCombat;
            DeactivateRotation(config);

            if (config.BossModAI == 0)
            {
                ToggleBossModAI(false);
            }
        }
        // Ongoing combat
        else if (inCombat)
        {
            State = CombatState.InCombat;

            // LB check
            if (config.LimitPct >= 0)
            {
                CheckLimitBreak(config);
            }
        }
        else
        {
            State = CombatState.OutOfCombat;
            StateDetail = "";
            ActivePreset = "";
        }
    }

    private void ActivateRotation(CharacterConfig config)
    {
        var now = Environment.TickCount64;
        if (now - lastRotationToggleMs < 2000) return; // Cooldown
        lastRotationToggleMs = now;

        // Select preset based on zone type
        var preset = GetPresetForZone(config);
        ActivePreset = preset;

        // Select rotation plugin (different for foray)
        var pluginIdx = zoneService.CurrentZone == ZoneType.Foray
            ? config.RotationPluginForay
            : config.RotationPlugin;

        var pluginName = pluginIdx >= 0 && pluginIdx < RotationPluginNames.Length
            ? RotationPluginNames[pluginIdx]
            : "RSR";

        // Send activation commands
        switch (pluginName)
        {
            case "RSR":
                SendCommand("/rotation auto on");
                if (!string.IsNullOrEmpty(preset) && preset != "FRENRIDER")
                    SendCommand($"/rotation settings preset {preset}");
                break;
            case "WRATH":
                SendCommand("/wrath auto on");
                if (!string.IsNullOrEmpty(preset))
                    SendCommand($"/wrath settings preset {preset}");
                break;
            case "BMR":
                SendCommand("/bmrai on");
                break;
            case "VBM":
                SendCommand("/vbmai on");
                break;
        }

        // Set positional
        SetPositional(config, pluginName);

        State = CombatState.InCombat;
        StateDetail = $"{pluginName} active" + (string.IsNullOrEmpty(preset) ? "" : $" [{preset}]");

        Plugin.Log.Information($"Combat: Activated {pluginName} with preset '{preset}'");
    }

    private void DeactivateRotation(CharacterConfig config)
    {
        var pluginIdx = zoneService.CurrentZone == ZoneType.Foray
            ? config.RotationPluginForay
            : config.RotationPlugin;

        var pluginName = pluginIdx >= 0 && pluginIdx < RotationPluginNames.Length
            ? RotationPluginNames[pluginIdx]
            : "RSR";

        switch (pluginName)
        {
            case "RSR":
                SendCommand("/rotation auto off");
                break;
            case "WRATH":
                SendCommand("/wrath auto off");
                break;
            case "BMR":
                SendCommand("/bmrai off");
                break;
            case "VBM":
                SendCommand("/vbmai off");
                break;
        }

        State = CombatState.OutOfCombat;
        StateDetail = "";
        ActivePreset = "";

        Plugin.Log.Information($"Combat: Deactivated {pluginName}");
    }

    private string GetPresetForZone(CharacterConfig config)
    {
        if (zoneService.InFate)
            return config.AutoRotationTypeFATE;

        return zoneService.CurrentZone switch
        {
            ZoneType.DeepDungeon => config.AutoRotationTypeDD,
            _ => config.AutoRotationType,
        };
    }

    private void SetPositional(CharacterConfig config, string pluginName)
    {
        // PositionalInCombat: 0=Front, 1=Rear, 2=Any, 3=Auto
        if (config.PositionalInCombat == 3) return; // Auto = let plugin decide

        var positional = config.PositionalInCombat switch
        {
            0 => "front",
            1 => "rear",
            2 => "any",
            _ => "auto",
        };

        // RSR and WRATH support positional commands
        if (pluginName is "RSR" or "WRATH")
        {
            var cmd = pluginName == "RSR" ? "/rotation" : "/wrath";
            SendCommand($"{cmd} settings positional {positional}");
        }
    }

    private void ToggleBossModAI(bool enable)
    {
        var state = enable ? "on" : "off";
        SendCommand($"/bmrai {state}");
    }

    private void CheckLimitBreak(CharacterConfig config)
    {
        // LB automation: send LB command when HP threshold reached
        // This is a stub — actual implementation needs target HP checking
        // config.LimitPct: percentage threshold (-1 = disabled)
        // Future: check target's HP % and send /ac "Limit Break" when below threshold
    }

    private static void SendCommand(string command)
    {
        try
        {
            if (!Plugin.CommandManager.ProcessCommand(command))
                Plugin.Log.Warning($"Combat command not handled: {command}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Combat command failed [{command}]: {ex.Message}");
        }
    }
}
