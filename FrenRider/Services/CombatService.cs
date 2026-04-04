using System;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
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
    private const string BossModPassivePresetName = "AutoDuty Passive";
    private const int RotationTypeAuto = 0;
    private const int RotationTypeManual = 1;
    private const int RotationTypeNone = 2;
    private const int RotationTypeAutoSupport = 3;
    private const int RotationTypePreviouslyEngagedTargets = 4;

    private readonly Plugin plugin;
    private readonly FrenTracker tracker;
    private readonly ZoneService zoneService;

    private bool wasInCombat;
    private bool wasInDuty;
    private long lastRotationToggleMs;
    private int lastActivePluginIdx = -1;
    private long pendingCombatSettingsRefreshMs;
    private string lastObservedCombatSettingsSignature = string.Empty;
    private string pendingCombatSettingsSignature = string.Empty;

    private static readonly string[] RotationPluginNames = { "BMR", "VBM", "RSR", "WRATH" };
    private const long CombatSettingsRefreshDebounceMs = 400;

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
        var inCombat = Plugin.Condition[ConditionFlag.InCombat];
        var inDuty = Plugin.Condition[ConditionFlag.BoundByDuty];
        var now = Environment.TickCount64;

        // Zone transition: deactivate rotation and reset
        if (zoneService.ZoneChanged)
        {
            HandleZoneTransition(config, inCombat, inDuty);
            return;
        }

        if (!config.Enabled)
        {
            ResetCombatSettingsRefreshTracking();
            lastObservedCombatSettingsSignature = string.Empty;
            //if (wasInCombat) DeactivateRotation(config);
			//Plugin.Log.Information($"Combat: stopped FrenRider GHOST IN THE MACHINE 5 attemting to deactivate rotations after combat like an idiot");
			//debug/code review this is called every frame and could be an issue
            State = CombatState.OutOfCombat;
            StateDetail = "Disabled";
            wasInCombat = false;
            return;
        }

        TrackCombatSettingsChanges(config, now);

        // Entered duty (activate rotation immediately)
        if (inDuty && !wasInDuty)
        {
            wasInDuty = true;
            Plugin.Log.Information("Entered duty - activating rotation");

            if (!IsRotationDisabled(config))
            {
                ActivateRotation(config);
            }

        }
        // Left duty (deactivate rotation)
        else if (!inDuty && wasInDuty)
        {
            wasInDuty = false;
            wasInCombat = false;
            State = CombatState.LeavingCombat;
            //DeactivateRotation(config);
            //SendCommand("/rotation cancel"); //why is this here ? GHOST IN THE MACHINE6 another attemp to deactivate rotations once we leave duties. sigh
            Plugin.Log.Information("Left duty - deactivating rotation");
        }
        // Entered combat (while already in duty or not)
        else if (inCombat && !wasInCombat)
        {
            wasInCombat = true;
            State = CombatState.EnteringCombat;

            // Only activate if not already active from duty entry
            if (!inDuty && !IsRotationDisabled(config))
            {
                ActivateRotation(config);
            }

        }
        // Left combat (but stay active if in duty)
        else if (!inCombat && wasInCombat)
        {
            wasInCombat = false;

            // Only deactivate if NOT in duty
            if (!inDuty)
            {
                State = CombatState.LeavingCombat;
                //DeactivateRotation(config);
                //SendCommand("/rotation cancel"); //why is this here ? GHOST IN THE MACHINE7
            }
            else
            {
                // Still in duty, just out of combat - keep rotation active
                State = CombatState.InCombat;
                StateDetail = $"In duty (out of combat) - rotation active";
            }
        }
        // Ongoing combat or in duty
        else if (inCombat || inDuty)
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

        TryApplyPendingCombatSettingsRefresh(config, now, inCombat, inDuty);
    }

    private void ActivateRotation(CharacterConfig config, bool ignoreCooldown = false)
    {
        var now = Environment.TickCount64;
        if (!ignoreCooldown && now - lastRotationToggleMs < 2000) return; // Cooldown
        lastRotationToggleMs = now;

        // Select preset based on zone type
        var preset = GetPresetForZone(config);
        ActivePreset = preset;

        // Select rotation plugin (different for foray)
        var pluginName = GetSelectedRotationPluginName(config);
        lastActivePluginIdx = Array.IndexOf(RotationPluginNames, pluginName);

        // Disable other rotation plugins first
        DisableOtherRotationPlugins(config);
        ApplyBossModSafetyState(pluginName, preset, "activation");

        // Send activation commands
        switch (pluginName)
        {
            case "RSR":
                var rsrModeName = ApplyRsrMode(config);
                if (ShouldApplyPreset(preset) && !string.Equals(preset, "FRENRIDER", StringComparison.OrdinalIgnoreCase))
                    SendCommand($"/rotation settings preset {FormatCommandArgument(preset)}");
                StateDetail = $"{pluginName} {rsrModeName}" + (string.IsNullOrEmpty(preset) ? "" : $" [{preset}]");
                break;
            case "WRATH":
                SendCommand("/wrath auto on");
                if (ShouldApplyPreset(preset))
                    SendCommand($"/wrath settings preset {FormatCommandArgument(preset)}");
                StateDetail = $"{pluginName} active" + (string.IsNullOrEmpty(preset) ? "" : $" [{preset}]");
                break;
            case "BMR":
                StateDetail = $"{pluginName} active" + (string.IsNullOrEmpty(preset) ? "" : $" [{preset}]");
                break;
            case "VBM":
                StateDetail = $"{pluginName} active" + (string.IsNullOrEmpty(preset) ? "" : $" [{preset}]");
                break;
        }

        // Set positional
        SetPositional(config, pluginName);

        State = CombatState.InCombat;
        Plugin.Log.Information($"Combat: Activated {pluginName} with preset '{preset}'");
    }

    private void DeactivateRotation(CharacterConfig config)
    {
        var pluginName = GetLastActiveRotationPluginName(config);

        switch (pluginName)
        {
            case "RSR":
                if (!plugin.AutorotIpcService.TrySetRsrMode(AutorotIpcService.RsrStateCommandType.Off))
                    //SendCommand("/rotation cancel"); //why is this here ? GHOST IN THE MACHINE3
					Plugin.Log.Information($"Combat: stopped {pluginName} GHOST IN THE MACHINE 3 rotation cancel");
                break;
            case "WRATH":
					//SendCommand("/wrath auto off"); //why is this here ? GHOST IN THE MACHINE4
					Plugin.Log.Information($"Combat: stopped {pluginName} GHOST IN THE MACHINE 4 wrath auto off");
                break;
            case "BMR":
            case "VBM":
                break;
        }

        State = CombatState.OutOfCombat;
        StateDetail = "";
        ActivePreset = "";
        lastActivePluginIdx = -1;

        Plugin.Log.Information($"Combat: Deactivated {pluginName}");
    }

    private string ApplyRsrMode(CharacterConfig config)
    {
        switch (config.RotationType)
        {
            case RotationTypeManual:
                if (!plugin.AutorotIpcService.TrySetRsrMode(AutorotIpcService.RsrStateCommandType.Manual))
                    SendCommand("/rotation manual");
                return "Manual";

            case RotationTypeAutoSupport:
                plugin.AutorotIpcService.TrySetRsrHostileType(AutorotIpcService.RsrTargetHostileType.TargetsHaveTarget);
                plugin.AutorotIpcService.TrySetRsrSupportTargeting(true);
                if (!plugin.AutorotIpcService.TrySetRsrMode(AutorotIpcService.RsrStateCommandType.Henched))
                    SendCommand("/rotation auto on");
                return "Auto (Support)";

            case RotationTypePreviouslyEngagedTargets:
                plugin.AutorotIpcService.TrySetRsrHostileType(AutorotIpcService.RsrTargetHostileType.TargetsHaveTarget);
                if (!plugin.AutorotIpcService.TrySetRsrMode(AutorotIpcService.RsrStateCommandType.Auto))
                    SendCommand("/rotation auto on");
                return "Previously Engaged Targets";

            case RotationTypeAuto:
            default:
                plugin.AutorotIpcService.TrySetRsrHostileType(AutorotIpcService.RsrTargetHostileType.AllTargetsCanAttack);
                if (!plugin.AutorotIpcService.TrySetRsrMode(AutorotIpcService.RsrStateCommandType.Auto))
                    SendCommand("/rotation auto on");
                return "Auto";
        }
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

    private void ApplyPassiveRotationSettings(CharacterConfig config, string reason)
    {
        if (IsRotationDisabled(config))
            return;

        var pluginName = GetSelectedRotationPluginName(config);
        var preset = GetPresetForZone(config);
        ActivePreset = preset;
        ApplyBossModSafetyState(pluginName, preset, reason);

        switch (pluginName)
        {
            case "RSR":
                if (ShouldApplyPreset(preset) && !string.Equals(preset, "FRENRIDER", StringComparison.OrdinalIgnoreCase))
                    SendCommand($"/rotation settings preset {FormatCommandArgument(preset)}");
                SetPositional(config, pluginName);
                break;
            case "WRATH":
                if (ShouldApplyPreset(preset))
                    SendCommand($"/wrath settings preset {FormatCommandArgument(preset)}");
                SetPositional(config, pluginName);
                break;
            case "BMR":
            case "VBM":
                break;
        }

        Plugin.Log.Information($"Combat: Reapplied {pluginName} settings after {reason} with preset '{preset}'");
    }

    private void HandleZoneTransition(CharacterConfig config, bool inCombat, bool inDuty)
    {
        ResetCombatSettingsRefreshTracking();

        State = CombatState.OutOfCombat;
        StateDetail = "Zone transition";
        ActivePreset = "";
        wasInCombat = inCombat;
        wasInDuty = inDuty;

        if (!config.Enabled)
            return;

        if (IsRotationDisabled(config))
        {
            StateDetail = "Zone transition (rotation disabled)";
            return;
        }

        if (inDuty || inCombat)
            ActivateRotation(config, ignoreCooldown: true);
        else
            ApplyPassiveRotationSettings(config, "territory change");

        lastObservedCombatSettingsSignature = BuildCombatSettingsSignature(config);
    }

    private void TrackCombatSettingsChanges(CharacterConfig config, long now)
    {
        var signature = BuildCombatSettingsSignature(config);
        if (signature == lastObservedCombatSettingsSignature)
            return;

        lastObservedCombatSettingsSignature = signature;
        pendingCombatSettingsSignature = signature;
        pendingCombatSettingsRefreshMs = now + CombatSettingsRefreshDebounceMs;
    }

    private void TryApplyPendingCombatSettingsRefresh(CharacterConfig config, long now, bool inCombat, bool inDuty)
    {
        if (pendingCombatSettingsRefreshMs == 0 || now < pendingCombatSettingsRefreshMs)
            return;

        var signature = BuildCombatSettingsSignature(config);
        if (signature != pendingCombatSettingsSignature)
            return;

        ResetCombatSettingsRefreshTracking();
        lastObservedCombatSettingsSignature = signature;

        if (IsRotationDisabled(config))
        {
            if (lastActivePluginIdx >= 0)
                DeactivateRotation(config);
            return;
        }

        if (inDuty || inCombat)
            ActivateRotation(config, ignoreCooldown: true);
        else
            ApplyPassiveRotationSettings(config, "Combat / AI config change");
    }

    private void ResetCombatSettingsRefreshTracking()
    {
        pendingCombatSettingsRefreshMs = 0;
        pendingCombatSettingsSignature = string.Empty;
    }

    private string BuildCombatSettingsSignature(CharacterConfig config)
    {
        return string.Join("|",
            config.AutoRotationType,
            config.AutoRotationTypeDD,
            config.AutoRotationTypeFATE,
            config.RotationPlugin,
            config.RotationPluginForay,
            config.BossModAI,
            config.PositionalInCombat,
            config.MaxAIDistance,
            config.LimitPct,
            config.RotationType,
            zoneService.CurrentZone,
            zoneService.InFate,
            GetPresetForZone(config),
            GetSelectedRotationPluginName(config));
    }

    private string GetLastActiveRotationPluginName(CharacterConfig config)
    {
        return lastActivePluginIdx >= 0 && lastActivePluginIdx < RotationPluginNames.Length
            ? RotationPluginNames[lastActivePluginIdx]
            : GetSelectedRotationPluginName(config);
    }

    private string GetSelectedRotationPluginName(CharacterConfig config)
    {
        var pluginIdx = zoneService.CurrentZone == ZoneType.Foray
            ? config.RotationPluginForay
            : config.RotationPlugin;

        return pluginIdx >= 0 && pluginIdx < RotationPluginNames.Length
            ? RotationPluginNames[pluginIdx]
            : "RSR";
    }

    private void ApplyBossModSafetyState(string pluginName, string selectedPreset, string reason)
    {
        EnsureBossModAiEnabled();
		SendCommand($"/xldisableplugin AutoDuty");  //The real ghost in the machine is gone finally.
        switch (pluginName)
        {
            case "BMR":
				SendCommand($"/rotation cancel");  //ghost in the machine 8. disabling RSR when we switch to bmr
				SendCommand($"/wrath Auto off");  //ghost in the machine 8. disabling WRATH when we switch to bmr
                SendBmrPresetCommand(selectedPreset, reason);
                break;
            case "VBM":
				SendCommand($"/rotation cancel");  //ghost in the machine 8. disabling RSR when we switch to vbm
				SendCommand($"/wrath Auto off");  //ghost in the machine 8. disabling WRATH when we switch to vbm
                SendVbmPresetCommand(selectedPreset, reason);
                break;
            case "RSR":
				SendCommand($"/rotation Auto");  //ghost in the machine 8. disabling RSR when we switch to WRATH
                SendBmrPresetCommand(BossModPassivePresetName, $"{reason} because selected plugin is {pluginName}");
                SendVbmPresetCommand(BossModPassivePresetName, $"{reason} because selected plugin is {pluginName}");
                break;
            case "WRATH":
				SendCommand($"/rotation cancel");  //ghost in the machine 8. disabling RSR when we switch to WRATH
                SendBmrPresetCommand(BossModPassivePresetName, $"{reason} because selected plugin is {pluginName}");
                SendVbmPresetCommand(BossModPassivePresetName, $"{reason} because selected plugin is {pluginName}");
                break;
        }
    }

    private void SendBmrPresetCommand(string presetName, string reason)
    {
        if (!ShouldApplyPreset(presetName))
            return;

        SendCommand($"/bmrai setpresetname {presetName}");
        Plugin.Log.Information($"Combat: Sent BMR preset command for '{presetName}' after {reason}");
    }

    private void SendVbmPresetCommand(string presetName, string reason)
    {
        if (!ShouldApplyPreset(presetName))
            return;

        SendCommand($"/vbm ar set {presetName}");
        Plugin.Log.Information($"Combat: Sent VBM preset command for '{presetName}' after {reason}");
    }

    private void EnsureBossModAiEnabled()
    {
        SendCommand("/bmrai on");
    }

    private void DisableOtherRotationPlugins(CharacterConfig config)
    {
        // Only disable conflicting rotation engines. BossMod AI stays enabled for avoidance/movement.
        var pluginName = GetSelectedRotationPluginName(config);
        var pluginIdx = Array.IndexOf(RotationPluginNames, pluginName);
        var activePluginName = pluginIdx >= 0 ? pluginName : "none";

        Plugin.Log.Debug($"DisableOtherRotationPlugins: pluginIdx={pluginIdx}, activePlugin={activePluginName}, isForay={zoneService.CurrentZone == ZoneType.Foray}");

        for (var i = 0; i < RotationPluginNames.Length; i++)
        {
            var otherPluginName = RotationPluginNames[i];
            
            if (i == pluginIdx)
            {
                Plugin.Log.Debug($"  Skipping {otherPluginName} (index {i}) - this is the active plugin");
                continue; // Skip the active plugin
            }

            if (otherPluginName is "BMR" or "VBM")
            {
                Plugin.Log.Debug($"  Leaving {otherPluginName} enabled for avoidance / movement");
                continue;
            }

            Plugin.Log.Debug($"  Disabling {otherPluginName} (index {i})");
            switch (otherPluginName)
            {
                case "RSR":
                    //SendCommand("/rotation cancel"); //ghost in the machine 1
					Plugin.Log.Information($"Combat: stopped {pluginName} GHOST IN THE MACHINE 1 rotation cancel");
                    break;
                case "WRATH":
                    //SendCommand("/wrath auto off"); //ghost in the machine 2
					Plugin.Log.Information($"Combat: stopped {pluginName} GHOST IN THE MACHINE 2 rotation cancel");
                    break;
            }
        }
    }

    private void CheckLimitBreak(CharacterConfig config)
    {
        // LB automation: send LB command when HP threshold reached
        // This is a stub — actual implementation needs target HP checking
        // config.LimitPct: percentage threshold (-1 = disabled)
        // Future: check target's HP % and send /ac "Limit Break" when below threshold
    }

    private static unsafe void SendCommand(string command)
    {
        try
        {
            var uiModule = UIModule.Instance();
            if (uiModule == null)
            {
                Plugin.Log.Error($"Combat command failed [{command}]: UIModule is null");
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(command);
            var utf8String = Utf8String.FromSequence(bytes);
            uiModule->ProcessChatBoxEntry(utf8String, nint.Zero);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Combat command failed [{command}]: {ex.Message}");
        }
    }

    private static bool ShouldApplyPreset(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRotationDisabled(CharacterConfig config)
    {
        return config.RotationType == RotationTypeNone;
    }

    private static string FormatCommandArgument(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        return value.IndexOfAny([' ', '\t', '"']) >= 0
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }
}
