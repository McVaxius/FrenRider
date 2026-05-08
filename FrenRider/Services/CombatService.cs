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
    private bool mountedRotationSuppressed;
    private string mountedSuppressedPluginName = string.Empty;
    private bool wrathAutoActive;
    private uint lastWarnedManagedPresetJobId = uint.MaxValue;
    private bool warnedMissingManagedPresetJob;

    private static readonly string[] RotationPluginNames = { "BMR", "VBM", "RSR", "WRATH" };
    private const long CombatSettingsRefreshDebounceMs = 400;
    private const string ManagedPresetRoleTank = "TANK";
    private const string ManagedPresetRoleMelee = "MELEE";
    private const string ManagedPresetRoleRanged = "RANGED";

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
        var mountedOrMounting = Plugin.Condition[ConditionFlag.Mounted] || Plugin.Condition[ConditionFlag.Mounting71];
        var now = Environment.TickCount64;

        if (!config.Enabled)
        {
            SetWrathAuto(false, "plugin disabled");
            RestoreMountedRotationLifecycle(config, inCombat, inDuty, "plugin disabled", reapplySelection: false);
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

        LogFateCombatDecisionIfChanged(config, inCombat, inDuty, mountedOrMounting);

        if (HandleMountedRotationLifecycle(config, mountedOrMounting, inCombat, inDuty))
            return;

        // Zone transition: deactivate rotation and reset
        if (zoneService.ZoneChanged)
        {
            HandleZoneTransition(config, inCombat, inDuty);
            return;
        }

        if (plugin.AdsIntegrationService.ShouldPauseDutySystems)
        {
            ResetCombatSettingsRefreshTracking();
            lastObservedCombatSettingsSignature = string.Empty;
            State = CombatState.OutOfCombat;
            StateDetail = plugin.AdsIntegrationService.IsHandoffPending
                ? "ADS handoff pending"
                : "ADS active";
            ActivePreset = "";
            wasInCombat = inCombat;
            wasInDuty = inDuty;
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

        // Select rotation plugin (different for foray)
        var pluginName = GetSelectedRotationPluginName(config);
        lastActivePluginIdx = Array.IndexOf(RotationPluginNames, pluginName);
        var bossModPreset = GetBossModPresetForPlugin(config, pluginName);
        var manualPreset = GetManualPresetForZone(config);
        ActivePreset = bossModPreset;

        // Disable other rotation plugins first
        DisableOtherRotationPlugins(config);
        ApplyBossModSafetyState(pluginName, bossModPreset, "activation");

        // Send activation commands
        switch (pluginName)
        {
            case "RSR":
                var rsrModeName = ApplyRsrMode(config);
                if (config.ConfigureRotationPresetManually &&
                    ShouldApplyPreset(manualPreset) &&
                    !string.Equals(manualPreset, "FRENRIDER", StringComparison.OrdinalIgnoreCase))
                    SendCommand($"/rotation settings preset {FormatCommandArgument(manualPreset)}");
                StateDetail = $"{pluginName} {rsrModeName}" + (string.IsNullOrEmpty(bossModPreset) ? "" : $" [{bossModPreset}]");
                break;
            case "WRATH":
                SetWrathAuto(true, "activation");
                StateDetail = $"{pluginName} auto" + (string.IsNullOrEmpty(bossModPreset) ? "" : $" [{bossModPreset}]");
                break;
            case "BMR":
                StateDetail = $"{pluginName} active" + (string.IsNullOrEmpty(bossModPreset) ? "" : $" [{bossModPreset}]");
                break;
            case "VBM":
                StateDetail = $"{pluginName} active" + (string.IsNullOrEmpty(bossModPreset) ? "" : $" [{bossModPreset}]");
                break;
        }

        // Set positional
        SetPositional(config, pluginName);

        State = CombatState.InCombat;
        Plugin.Log.Information($"Combat: Activated {pluginName} with BossMod preset '{bossModPreset}'");
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
                SetWrathAuto(false, "deactivation");
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

    private string GetManualPresetForZone(CharacterConfig config)
    {
        if (zoneService.InFate)
            return config.AutoRotationTypeFATE;

        return zoneService.CurrentZone switch
        {
            ZoneType.DeepDungeon => config.AutoRotationTypeDD,
            _ => config.AutoRotationType,
        };
    }

    private string GetBossModPresetForPlugin(CharacterConfig config, string pluginName)
    {
        var managedPreset = GetManagedBossModPreset(pluginName);
        if (!config.ConfigureRotationPresetManually)
            return managedPreset;

        var manualPreset = GetManualPresetForZone(config);
        return pluginName switch
        {
            "BMR" or "VBM" => manualPreset,
            "RSR" or "WRATH" when config.ForceBossModPresetRegardlessOfRotation => manualPreset,
            _ => managedPreset,
        };
    }

    private string GetManagedBossModPreset(string pluginName)
    {
        var role = GetManagedPresetRole();
        return pluginName is "BMR" or "VBM"
            ? $"FRENRIDER - {role}"
            : $"passive - {role.ToLowerInvariant()}";
    }

    private string GetManagedPresetRole()
    {
        var jobId = GetCurrentClassJobId();
        if (!jobId.HasValue)
            return ManagedPresetRoleRanged;

        return jobId.Value switch
        {
            1 or 3 or 19 or 21 or 32 or 37 => ManagedPresetRoleTank,
            2 or 4 or 20 or 22 or 29 or 30 or 34 or 39 or 41 => ManagedPresetRoleMelee,
            5 or 6 or 7 or 23 or 24 or 25 or 26 or 27 or 28 or 31 or 33 or 35 or 36 or 38 or 40 or 42 => ManagedPresetRoleRanged,
            _ => WarnUnknownClassJob(jobId.Value),
        };
    }

    private uint? GetCurrentClassJobId()
    {
        try
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null)
            {
                WarnMissingClassJob("local player unavailable");
                return null;
            }

            var jobId = player.ClassJob.RowId;
            if (jobId == 0)
            {
                WarnMissingClassJob("class job row is 0");
                return null;
            }

            warnedMissingManagedPresetJob = false;
            return jobId;
        }
        catch (Exception ex)
        {
            WarnMissingClassJob(ex.Message);
            return null;
        }
    }

    private string WarnUnknownClassJob(uint jobId)
    {
        if (lastWarnedManagedPresetJobId != jobId)
        {
            Plugin.Log.Warning($"Combat: unknown class job row {jobId}; using ranged BossMod preset");
            lastWarnedManagedPresetJobId = jobId;
        }

        return ManagedPresetRoleRanged;
    }

    private void WarnMissingClassJob(string reason)
    {
        if (warnedMissingManagedPresetJob)
            return;

        Plugin.Log.Warning($"Combat: cannot resolve current class job ({reason}); using ranged BossMod preset");
        warnedMissingManagedPresetJob = true;
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

        // FrenRider only manages Wrath auto state; leave Wrath targeting/settings manual.
        if (pluginName is "RSR")
        {
            SendCommand($"/rotation settings positional {positional}");
        }
    }

    private void ApplyPassiveRotationSettings(CharacterConfig config, string reason)
    {
        if (IsRotationDisabled(config))
            return;

        var pluginName = GetSelectedRotationPluginName(config);
        lastActivePluginIdx = Array.IndexOf(RotationPluginNames, pluginName);
        var bossModPreset = GetBossModPresetForPlugin(config, pluginName);
        var manualPreset = GetManualPresetForZone(config);
        ActivePreset = bossModPreset;
        ApplyBossModSafetyState(pluginName, bossModPreset, reason);

        switch (pluginName)
        {
            case "RSR":
                if (config.ConfigureRotationPresetManually &&
                    ShouldApplyPreset(manualPreset) &&
                    !string.Equals(manualPreset, "FRENRIDER", StringComparison.OrdinalIgnoreCase))
                    SendCommand($"/rotation settings preset {FormatCommandArgument(manualPreset)}");
                SetPositional(config, pluginName);
                break;
            case "WRATH":
                SetWrathAuto(true, reason);
                break;
            case "BMR":
            case "VBM":
                break;
        }

        Plugin.Log.Information($"Combat: Reapplied {pluginName} settings after {reason} with BossMod preset '{bossModPreset}'");
    }

    public void ApplyPresetSelection(string reason, bool installPresets = true)
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        if (IsRotationDisabled(config))
            return;

        var pluginName = GetSelectedRotationPluginName(config);
        lastActivePluginIdx = Array.IndexOf(RotationPluginNames, pluginName);
        var bossModPreset = GetBossModPresetForPlugin(config, pluginName);
        ActivePreset = bossModPreset;
        ApplyBossModPreset(bossModPreset, reason, installPresets);
    }

    private bool HandleMountedRotationLifecycle(CharacterConfig config, bool mountedOrMounting, bool inCombat, bool inDuty)
    {
        if (inDuty)
        {
            RestoreMountedRotationLifecycle(config, inCombat, inDuty, "duty entry");
            return false;
        }

        if (!mountedOrMounting)
        {
            RestoreMountedRotationLifecycle(config, inCombat, inDuty, "dismount");
            return false;
        }

        SuppressMountedRotationLifecycle(config);
        ResetCombatSettingsRefreshTracking();
        lastObservedCombatSettingsSignature = string.Empty;
        State = CombatState.OutOfCombat;
        StateDetail = "Mounted - rotations suppressed";
        ActivePreset = "";
        wasInCombat = inCombat;
        wasInDuty = inDuty;
        return true;
    }

    private void SuppressMountedRotationLifecycle(CharacterConfig config)
    {
        if (mountedRotationSuppressed)
            return;

        var pluginName = GetSelectedRotationPluginName(config);
        mountedSuppressedPluginName = pluginName;

        switch (pluginName)
        {
            case "BMR":
                SendCommand("/bmrai off");
                break;
            case "VBM":
                SendCommand("/vbmai off");
                break;
            case "RSR":
                SendCommand("/rotation cancel");
                break;
            case "WRATH":
                SetWrathAuto(false, "mounted rotation suppression");
                break;
        }

        mountedRotationSuppressed = true;
        Plugin.Log.Information($"[FrenRider] Mounted rotation suppression enabled for {pluginName} to protect mounted follow.");
    }

    private void RestoreMountedRotationLifecycle(CharacterConfig config, bool inCombat, bool inDuty, string reason, bool reapplySelection = true)
    {
        if (!mountedRotationSuppressed)
            return;

        var pluginName = string.IsNullOrWhiteSpace(mountedSuppressedPluginName)
            ? GetSelectedRotationPluginName(config)
            : mountedSuppressedPluginName;

        switch (pluginName)
        {
            case "BMR":
                SendCommand("/bmrai on");
                break;
            case "VBM":
                SendCommand("/vbmai on");
                break;
            case "RSR":
                SendCommand("/rotation auto");
                break;
            case "WRATH":
                if (reapplySelection && !IsRotationDisabled(config))
                    SetWrathAuto(true, $"mounted lifecycle restore ({reason})");
                break;
        }

        mountedRotationSuppressed = false;
        mountedSuppressedPluginName = string.Empty;
        lastRotationToggleMs = 0;
        Plugin.Log.Information($"[FrenRider] Mounted rotation suppression cleared for {pluginName} after {reason}.");

        if (!reapplySelection || IsRotationDisabled(config))
            return;

        if (inDuty || inCombat)
            ActivateRotation(config, ignoreCooldown: true);
        else
            ApplyPassiveRotationSettings(config, $"mounted lifecycle restore ({reason})");
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

    private void LogFateCombatDecisionIfChanged(CharacterConfig config, bool inCombat, bool inDuty, bool mountedOrMounting)
    {
        if (!zoneService.FateChanged)
            return;

        var fateText = zoneService.InFate
            ? $"entered:{zoneService.CurrentFateId}"
            : $"left:{zoneService.PreviousFateId}";
        var pluginName = GetSelectedRotationPluginName(config);
        var preset = GetBossModPresetForPlugin(config, pluginName);
        Plugin.Log.Information(
            $"[FR][FATE] CombatDecision fate={fateText}; territory={zoneService.TerritoryId}; inCombat={inCombat}; inDuty={inDuty}; mountedOrMounting={mountedOrMounting}; plugin={pluginName}; preset={preset}; state={State}");
    }

    private string BuildCombatSettingsSignature(CharacterConfig config)
    {
        var manualPresetSignature = config.ConfigureRotationPresetManually
            ? string.Join(",",
                config.AutoRotationType,
                config.AutoRotationTypeDD,
                config.AutoRotationTypeFATE,
                config.ForceBossModPresetRegardlessOfRotation,
                GetManualPresetForZone(config))
            : string.Empty;

        return string.Join("|",
            config.ConfigureRotationPresetManually,
            manualPresetSignature,
            config.RotationPlugin,
            config.RotationPluginForay,
            config.BossModAI,
            config.PositionalInCombat,
            config.MaxAIDistance,
            config.LimitPct,
            config.RotationType,
            zoneService.CurrentZone,
            zoneService.InFate,
            GetBossModPresetForPlugin(config, GetSelectedRotationPluginName(config)),
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
		Plugin.Log.Information($"[FrenRider] GHOST IN THE MACHINE CLEANUP");
		SendCommand($"/xldisableplugin AutoDuty");  //The real ghost in the machine is gone finally.
		//a few more hehe. turn off all the default stoppers
		SendCommand($"/rotation Settings KeyBoardNoise false");
		SendCommand($"/rotation Settings AutoOffBetweenArea False");
		SendCommand($"/rotation Settings AutoOffCutScene False");
		SendCommand($"/rotation Settings AutoOffSwitchClass False");
		SendCommand($"/rotation Settings AutoOffWhenDead False");
		SendCommand($"/rotation Settings AutoOffWhenDutyCompleted False");
		SendCommand($"/rotation Settings AutoOffAfterCombatTime 6942069");
		SendCommand($"/rotation Settings ToggleAuto False");
		SendCommand($"/rotation Settings ToggleManual False");
        SendCommand("/rotation Settings DummyBoss False");
        SendCommand("/rotation Settings DisableTargetDummys True");
        ApplyBossModPreset(selectedPreset, reason);

        switch (pluginName)
        {
            case "BMR":
				SendCommand($"/rotation cancel");  //ghost in the machine 8. disabling RSR when we switch to bmr
                SetWrathAuto(false, $"{reason} because selected plugin is {pluginName}");
                break;
            case "VBM":
				SendCommand($"/rotation cancel");  //ghost in the machine 8. disabling RSR when we switch to vbm
                SetWrathAuto(false, $"{reason} because selected plugin is {pluginName}");
                break;
            case "RSR":
                SetWrathAuto(false, $"{reason} because selected plugin is {pluginName}");
				SendCommand($"/rotation Auto");  //ghost in the machine 8. disabling RSR when we switch to WRATH
                break;
            case "WRATH":
				SendCommand($"/rotation cancel");  //ghost in the machine 8. disabling RSR when we switch to WRATH
                break;
        }
    }

    private void ApplyBossModPreset(string presetName, string reason, bool installPresets = true)
    {
        if (!ShouldApplyPreset(presetName))
            return;

        if (installPresets)
            plugin.AutorotIpcService.CreatePresets(force: true);
        plugin.AutorotIpcService.ForcePreset(presetName);
        SendBmrPresetCommand(presetName, reason);
        SendVbmPresetCommand(presetName, reason);
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
                    SetWrathAuto(false, $"selected rotation plugin is {pluginName}");
                    break;
            }
        }
    }

    private void SetWrathAuto(bool enabled, string reason)
    {
        if (wrathAutoActive == enabled)
            return;

        SendCommand(enabled ? "/wrath auto on" : "/wrath auto off");
        wrathAutoActive = enabled;
        Plugin.Log.Information($"Combat: Wrath auto {(enabled ? "on" : "off")} after {reason}");
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
