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
    private readonly QuestionableIpcService questionableIpcService;
    private readonly DutyCombatAuthorityPolicy dutyCombatAuthorityPolicy = new();

    private bool wasInCombat;
    private bool wasInDuty;
    private long lastRotationToggleMs;
    private int lastActivePluginIdx = -1;
    private long pendingCombatSettingsRefreshMs;
    private string lastObservedCombatSettingsSignature = string.Empty;
    private string pendingCombatSettingsSignature = string.Empty;
    private string lastBossModDefaultSettingsSignature = string.Empty;
    private string lastBossModMovementUnlockSignature = string.Empty;
    private bool mountedRotationSuppressed;
    private string mountedSuppressedPluginName = string.Empty;
    private bool wrathAutoActive;
    private uint lastWarnedManagedPresetJobId = uint.MaxValue;
    private bool warnedMissingManagedPresetJob;

    private static readonly string[] RotationPluginNames = { "BMR", "VBM", "RSR", "WRATH", "DAEDALUS" };
    private const long CombatSettingsRefreshDebounceMs = 400;
    private const string ManagedPresetRoleTank = "TANK";
    private const string ManagedPresetRoleMelee = "MELEE";
    private const string ManagedPresetRoleRanged = "RANGED";

    public CombatState State { get; private set; } = CombatState.OutOfCombat;
    public string StateDetail { get; private set; } = "";
    public string ActivePreset { get; private set; } = "";
    public bool WrathAutoActive => wrathAutoActive;
    internal DutyCombatAuthority DutyAuthority => dutyCombatAuthorityPolicy.Authority;
    internal bool IsQuestionableSoloAuthorityActive
        => dutyCombatAuthorityPolicy.Authority == DutyCombatAuthority.QuestionableSolo;
    private bool ShouldSuppressFrenRiderCombatCommands
        => IsQuestionableSoloAuthorityActive;

    public CombatService(
        Plugin plugin,
        FrenTracker tracker,
        ZoneService zoneService,
        QuestionableIpcService questionableIpcService)
    {
        this.plugin = plugin;
        this.tracker = tracker;
        this.zoneService = zoneService;
        this.questionableIpcService = questionableIpcService;
    }

    public void ClearExternalAutomationRuntimeState(string reason)
    {
        if (mountedRotationSuppressed || wrathAutoActive)
            Plugin.Log.Information($"[FrenRider] Cleared local external automation runtime flags after {reason}.");

        mountedRotationSuppressed = false;
        mountedSuppressedPluginName = string.Empty;
        wrathAutoActive = false;
        lastRotationToggleMs = 0;
        wasInCombat = false;
        wasInDuty = false;
        LogDutyAuthorityTransition(dutyCombatAuthorityPolicy.Reset(reason), null, false);
    }

    public bool PrepareForEnableCombatSetup()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var inDuty = IsInDuty();
        var decision = RefreshDutyCombatAuthority(
            config,
            inDuty,
            questionableIpcService.Refresh(force: true),
            frenRiderBootstrapAllowed: false);

        // if (decision.ShouldForceCombatOff)
        //     ForceQuestionableSoloCombatOff();

        if (decision.Authority != DutyCombatAuthority.QuestionableSolo)
            return true;

        SetQuestionableSoloSuppressedState(Plugin.Condition[ConditionFlag.InCombat], inDuty);
        return false;
    }

    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var inCombat = Plugin.Condition[ConditionFlag.InCombat];
        var inDuty = IsInDuty();
        var mountedOrMounting = Plugin.Condition[ConditionFlag.Mounted] || Plugin.Condition[ConditionFlag.Mounting71];
        var now = Environment.TickCount64;

        if (!config.Enabled)
        {
            ClearExternalAutomationRuntimeState("plugin disabled");
            ResetCombatSettingsRefreshTracking();
            lastObservedCombatSettingsSignature = string.Empty;
            lastBossModDefaultSettingsSignature = string.Empty;
            lastBossModMovementUnlockSignature = string.Empty;
            //if (wasInCombat) DeactivateRotation(config);
			//Plugin.Log.Information($"Combat: stopped FrenRider GHOST IN THE MACHINE 5 attemting to deactivate rotations after combat like an idiot");
            //debug/code review this is called every frame and could be an issue
            State = CombatState.OutOfCombat;
            StateDetail = "Disabled";
            return;
        }

        var questionableSnapshot = questionableIpcService.Refresh();
        var authorityDecision = RefreshDutyCombatAuthority(
            config,
            inDuty,
            questionableSnapshot,
            frenRiderBootstrapAllowed: !plugin.CoppeliaPowerlevelLeaseService.IsLeaseActive
                && !plugin.AutomationService.IsUtilityGateActive);

        // if (authorityDecision.ShouldForceCombatOff)
        //     ForceQuestionableSoloCombatOff();

        if (authorityDecision.Authority == DutyCombatAuthority.QuestionableSolo)
        {
            SetQuestionableSoloSuppressedState(inCombat, inDuty);
            return;
        }

        if (plugin.CoppeliaPowerlevelLeaseService.IsLeaseActive)
        {
            HandleCoppeliaPowerlevelLease(config, inCombat, inDuty);
            return;
        }

        if (authorityDecision.ShouldBootstrapFrenRider)
        {
            BootstrapFrenRiderDutyCombat(config, inCombat);
            return;
        }

        if (plugin.AutomationService.IsUtilityGateActive)
        {
            ResetCombatSettingsRefreshTracking();
            lastObservedCombatSettingsSignature = string.Empty;
            State = CombatState.OutOfCombat;
            StateDetail = "ADS utility active";
            ActivePreset = "";
            wasInCombat = inCombat;
            wasInDuty = inDuty;
            return;
        }

        LogFateCombatDecisionIfChanged(config, inCombat, inDuty, mountedOrMounting);

        if (HandleMountedRotationLifecycle(config, mountedOrMounting, inCombat, inDuty))
            return;

        if (plugin.AdsIntegrationService.ShouldPauseDutySystems)
        {
            ResetCombatSettingsRefreshTracking();
            lastObservedCombatSettingsSignature = string.Empty;
            State = IsRotationDisabled(config) ? CombatState.OutOfCombat : CombatState.InCombat;
            StateDetail = IsRotationDisabled(config)
                ? "ADS duty ownership active; FrenRider rotation disabled"
                : plugin.AdsIntegrationService.IsHandoffPending
                    ? "ADS handoff pending; FrenRider combat authoritative"
                    : "ADS active; FrenRider combat authoritative";
            if (IsRotationDisabled(config))
                ActivePreset = "";
            wasInCombat = inCombat;
            wasInDuty = inDuty;
            return;
        }

        // ADS-owned duties return above after their one per-duty combat bootstrap.
        if (zoneService.ZoneChanged)
        {
            HandleZoneTransition(config, inCombat, inDuty);
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

    private DutyCombatAuthorityDecision RefreshDutyCombatAuthority(
        CharacterConfig config,
        bool inDuty,
        QuestionableRunningSnapshot questionableSnapshot,
        bool frenRiderBootstrapAllowed)
    {
        var questionableRunningOrRecent = questionableSnapshot.IsRunning
            || questionableIpcService.WasRunningWithin(QuestionableIpcService.RecentRunningHold);
        var boundByDuty95 = Plugin.Condition[ConditionFlag.BoundByDuty95];
        var dutyCategory = plugin.AdsIntegrationService.GetCurrentDutyCategory();
        var adsDutyHandoffActive = plugin.AdsIntegrationService.IsHandoffPending
            || plugin.AdsIntegrationService.IsControllingDuty;
        var decision = dutyCombatAuthorityPolicy.Update(new DutyCombatAuthorityInput(
            config.Enabled,
            inDuty,
            boundByDuty95,
            dutyCategory,
            adsDutyHandoffActive,
            questionableRunningOrRecent,
            frenRiderBootstrapAllowed));

        LogDutyAuthorityTransition(decision, dutyCategory, boundByDuty95);
        return decision;
    }

    private static void LogDutyAuthorityTransition(
        DutyCombatAuthorityDecision decision,
        AdsDutyCategory? dutyCategory,
        bool boundByDuty95)
    {
        if (!decision.AuthorityChanged)
            return;

        var category = dutyCategory is { } value
            ? AdsDutyCategoryCatalog.GetLabel(value)
            : "Unknown";
        Plugin.Log.Information(
            $"[FrenRider][DutyAuthority] {decision.PreviousAuthority} -> {decision.Authority}; " +
            $"category={category}; BoundByDuty95={boundByDuty95}; reason={decision.Reason}.");
    }

    private void BootstrapFrenRiderDutyCombat(CharacterConfig config, bool inCombat)
    {
        ResetCombatSettingsRefreshTracking();
        lastObservedCombatSettingsSignature = string.Empty;
        wasInCombat = inCombat;
        wasInDuty = true;

        if (mountedRotationSuppressed)
        {
            mountedRotationSuppressed = false;
            mountedSuppressedPluginName = string.Empty;
            Plugin.Log.Information("[FrenRider][DutyAuthority] Duty bootstrap superseded mounted rotation suppression.");
        }

        lastRotationToggleMs = 0;
        if (IsRotationDisabled(config))
        {
            State = CombatState.OutOfCombat;
            StateDetail = "FrenRider duty authority; rotation disabled";
            ActivePreset = "";
        }
        else
        {
            State = CombatState.EnteringCombat;
            ActivateRotation(config, ignoreCooldown: true);
        }

        lastObservedCombatSettingsSignature = BuildCombatSettingsSignature(config);
        Plugin.Log.Information(
            $"[FrenRider][DutyAuthority] FrenRider combat bootstrap completed once for duty; " +
            $"rotationDisabled={IsRotationDisabled(config)}; adsPause={plugin.AdsIntegrationService.ShouldPauseDutySystems}.");
    }

    private void SetQuestionableSoloSuppressedState(bool inCombat, bool inDuty)
    {
        ResetCombatSettingsRefreshTracking();
        lastObservedCombatSettingsSignature = string.Empty;
        wasInCombat = inCombat;
        wasInDuty = inDuty;
        State = CombatState.OutOfCombat;
        StateDetail = "QuestionableSolo authority; FrenRider combat suppressed";
        ActivePreset = "";
    }

    private void ForceQuestionableSoloCombatOff()
    {
        plugin.CaptureExternalAutomationSnapshot("QuestionableSolo duty authority");

        var rsrHandled = plugin.AutorotIpcService.TrySetRsrMode(AutorotIpcService.RsrStateCommandType.Off);
        var daedalusHandled = SetDaedalusEnabled(false, "QuestionableSolo duty authority");
        foreach (var command in BuildQuestionableDutyCombatOffCommands(includeRsrFallback: !rsrHandled))
            SendCommand(command, allowDuringQuestionableSolo: true);

        wrathAutoActive = false;
        lastActivePluginIdx = -1;
        lastRotationToggleMs = 0;
        ActivePreset = "";

        if (mountedRotationSuppressed)
            Plugin.Log.Information("[FrenRider][Questionable] Mounted rotation suppression handed off to QuestionableSolo authority.");
        mountedRotationSuppressed = false;
        mountedSuppressedPluginName = string.Empty;

        Plugin.Log.Information(
            rsrHandled
                ? $"[FrenRider][Questionable] Initial QuestionableSolo shutdown forced BMR/VBM/RSR/Wrath off; RSR stopped via IPC; Daedalus {(daedalusHandled ? "stopped via IPC" : "IPC unavailable")}."
                : $"[FrenRider][Questionable] Initial QuestionableSolo shutdown forced BMR/VBM/RSR/Wrath off; RSR fallback command sent; Daedalus {(daedalusHandled ? "stopped via IPC" : "IPC unavailable")}.");
    }

    private void ActivateRotation(CharacterConfig config, bool ignoreCooldown = false)
    {
        if (ShouldSuppressFrenRiderCombatCommands)
            return;

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
        plugin.CaptureExternalAutomationSnapshot("rotation activation");
        DisableOtherRotationPlugins(config);
        ApplyBossModSafetyState(config, pluginName, bossModPreset, "activation");

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
            case "DAEDALUS":
                var daedalusHandled = SetDaedalusEnabled(true, "activation");
                if (daedalusHandled)
                    plugin.DaedalusTargetModeService.Apply(config.DaedalusTargetMode, notifyUser: false);
                StateDetail = $"{pluginName} {(daedalusHandled ? "active" : "unavailable")}" +
                    (string.IsNullOrEmpty(bossModPreset) ? "" : $" [{bossModPreset}]");
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
            case "DAEDALUS":
                SetDaedalusEnabled(false, "deactivation");
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
        if (config.RotationType == RotationTypeNone)
            return "None";

        var stateCommand = ResolveRsrStateCommandType(config.RotationType);
        var hostileType = config.RotationType == RotationTypePreviouslyEngagedTargets
            ? AutorotIpcService.RsrTargetHostileType.TargetsHaveTarget
            : ResolveRsrTargetHostileType(config.RsrAggroType);
        plugin.AutorotIpcService.TrySetRsrHostileType(hostileType);

        switch (config.RotationType)
        {
            case RotationTypeManual:
                if (!plugin.AutorotIpcService.TrySetRsrMode(stateCommand))
                    SendCommand("/rotation manual");
                return "Manual";

            case RotationTypeAutoSupport:
                plugin.AutorotIpcService.TrySetRsrSupportTargeting(true);
                if (!plugin.AutorotIpcService.TrySetRsrMode(stateCommand))
                    SendCommand("/rotation auto on");
                return "Support";

            case RotationTypePreviouslyEngagedTargets:
                if (!plugin.AutorotIpcService.TrySetRsrMode(stateCommand))
                    SendCommand("/rotation auto on");
                return "Auto";

            case RotationTypeAuto:
            default:
                if (!plugin.AutorotIpcService.TrySetRsrMode(stateCommand))
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
            "RSR" or "WRATH" or "DAEDALUS" when config.ForceBossModPresetRegardlessOfRotation => manualPreset,
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
        if (ShouldSuppressFrenRiderCombatCommands)
            return;

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
        if (ShouldSuppressFrenRiderCombatCommands || IsRotationDisabled(config))
            return;

        var pluginName = GetSelectedRotationPluginName(config);
        lastActivePluginIdx = Array.IndexOf(RotationPluginNames, pluginName);
        var bossModPreset = GetBossModPresetForPlugin(config, pluginName);
        var manualPreset = GetManualPresetForZone(config);
        ActivePreset = bossModPreset;
        plugin.CaptureExternalAutomationSnapshot($"rotation settings after {reason}");
        DisableOtherRotationPlugins(config);
        ApplyBossModSafetyState(config, pluginName, bossModPreset, reason);

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
            case "DAEDALUS":
                if (SetDaedalusEnabled(true, reason))
                    plugin.DaedalusTargetModeService.Apply(config.DaedalusTargetMode, notifyUser: false);
                break;
            case "BMR":
            case "VBM":
                break;
        }

        Plugin.Log.Information($"Combat: Reapplied {pluginName} settings after {reason} with BossMod preset '{bossModPreset}'");
    }

    public void ApplyPresetSelection(string reason, bool installPresets = true)
    {
        if (installPresets)
            plugin.AutorotIpcService.CreatePresets(force: true);

        if (ShouldSuppressFrenRiderCombatCommands)
            return;

        var config = plugin.ConfigManager.GetActiveConfig();
        if (IsRotationDisabled(config))
            return;

        var pluginName = GetSelectedRotationPluginName(config);
        lastActivePluginIdx = Array.IndexOf(RotationPluginNames, pluginName);
        var bossModPreset = GetBossModPresetForPlugin(config, pluginName);
        ActivePreset = bossModPreset;
        ApplyBossModPreset(pluginName, bossModPreset, reason, installPresets: false);
    }

    public void ApplyBossModFollowStartupDefaults()
    {
        if (ShouldSuppressFrenRiderCombatCommands)
            return;

        plugin.CaptureExternalAutomationSnapshot("BossMod follow startup defaults");
        SendCommand("/bmrai followoutofcombat off");
        SendCommand("/cbt disable AutoFollow");
        SendCommand("/bmrai followcombat off");
        SendCommand("/vbmai follow Slot1");
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
        plugin.CaptureExternalAutomationSnapshot("mounted rotation suppression");

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
            case "DAEDALUS":
                SetDaedalusEnabled(false, "mounted rotation suppression");
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
                ApplyConfiguredBossModAiState(config, pluginName, $"mounted lifecycle restore ({reason})");
                break;
            case "VBM":
                ApplyConfiguredBossModAiState(config, pluginName, $"mounted lifecycle restore ({reason})");
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
            config.DaedalusTargetMode,
            config.BossModAI,
            config.PositionalInCombat,
            config.MaxAIDistance,
            config.LimitPct,
            config.RotationType,
            config.RsrAggroType,
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

        return ResolveRotationPluginName(pluginIdx);
    }

    private void ApplyBossModSafetyState(CharacterConfig config, string pluginName, string selectedPreset, string reason)
    {
        if (ShouldSuppressFrenRiderCombatCommands)
            return;

        ApplyBossModDefaultSettingsOnce(pluginName, selectedPreset, reason);
        ApplyBossModMovementUnlockOnce(pluginName, selectedPreset, reason);
        ApplyBossModPreset(pluginName, selectedPreset, reason);

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
            case "DAEDALUS":
                SendCommand("/rotation cancel");
                SetWrathAuto(false, $"{reason} because selected plugin is {pluginName}");
                break;
        }

        ApplyConfiguredBossModAiState(config, pluginName, reason);
    }

    private void ApplyBossModDefaultSettingsOnce(string pluginName, string selectedPreset, string reason)
    {
        var signature = BuildBossModSafetySignature(pluginName, selectedPreset);
        if (string.Equals(signature, lastBossModDefaultSettingsSignature, StringComparison.Ordinal))
            return;

        lastBossModDefaultSettingsSignature = signature;
        Plugin.Log.Information($"[FrenRider] Applying BossMod/rotation defaults after {reason}.");
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
        SendCommand("/rotation Settings AutoUseTrueNorth False"); //suggested by matsuuzo
        SendCommand("/rotation Settings BmrSafetyCheckAuto True");
        SendCommand("/rotation Settings BmrSafetyCheckIntercept True");
    }

    private void ApplyBossModMovementUnlockOnce(string pluginName, string selectedPreset, string reason)
    {
        var signature = BuildBossModSafetySignature(pluginName, selectedPreset);
        if (string.Equals(signature, lastBossModMovementUnlockSignature, StringComparison.Ordinal))
            return;

        lastBossModMovementUnlockSignature = signature;
        plugin.CaptureExternalAutomationSnapshot("BossMod movement unlock");
        SendCommand("/bmrai forbidmovement off");
        SendCommand("/vbmai forbidmovement off");
        Plugin.Log.Information($"[FrenRider] Sent one-shot BossMod movement unlock after {reason}.");
    }

    private string BuildBossModSafetySignature(string pluginName, string selectedPreset)
        => string.Join("|", pluginName, selectedPreset, zoneService.TerritoryId, zoneService.CurrentZone);

    private void ApplyBossModPreset(string pluginName, string presetName, string reason, bool installPresets = true)
    {
        if (ShouldSuppressFrenRiderCombatCommands || !ShouldApplyPreset(presetName))
            return;

        if (installPresets)
            plugin.AutorotIpcService.CreatePresets(force: true);
        plugin.AutorotIpcService.ForcePreset(presetName);
        foreach (var command in BuildBossModPresetCommands(pluginName, presetName))
            SendCommand(command);
        Plugin.Log.Information($"Combat: Sent {GetBossModPresetProvider(pluginName)} preset command for '{presetName}' after {reason}");
    }

    private void ApplyConfiguredBossModAiState(CharacterConfig config, string pluginName, string reason)
    {
        var commands = BuildBossModAiCommands(config.BossModAI, pluginName);
        if (commands.Length == 0)
            return;

        plugin.CaptureExternalAutomationSnapshot("BossMod AI state change");
        foreach (var command in commands)
            SendCommand(command);

        Plugin.Log.Information($"Combat: BossMod AI {DescribeBossModAiSetting(config.BossModAI)} for {pluginName} after {reason}");
    }

    internal static string[] BuildBossModAiCommands(int bossModAI, string pluginName)
    {
        if (bossModAI == 1)
            return new[] { "/bmrai off", "/vbmai off" };

        return string.Equals(pluginName, "VBM", StringComparison.OrdinalIgnoreCase)
            ? new[] { "/vbmai on" }
            : new[] { "/bmrai on" };
    }

    internal static string[] BuildBossModPresetCommands(string pluginName, string presetName)
    {
        if (!ShouldApplyPreset(presetName))
            return Array.Empty<string>();

        return string.Equals(pluginName, "VBM", StringComparison.OrdinalIgnoreCase)
            ? new[] { $"/vbm ar set {presetName}" }
            : new[] { $"/bmrai setpresetname {presetName}" };
    }

    internal static string ResolveRotationPluginName(int pluginIdx)
    {
        return pluginIdx >= 0 && pluginIdx < RotationPluginNames.Length
            ? RotationPluginNames[pluginIdx]
            : "RSR";
    }

    internal static string[] BuildQuestionableDutyCombatOffCommands(bool includeRsrFallback = true)
    {
        return includeRsrFallback
            ? new[] { "/bmrai off", "/vbmai off", "/rotation cancel", "/wrath auto off" }
            : new[] { "/bmrai off", "/vbmai off", "/wrath auto off" };
    }

    internal static AutorotIpcService.RsrStateCommandType ResolveRsrStateCommandType(int rotationType)
    {
        return rotationType switch
        {
            RotationTypeManual => AutorotIpcService.RsrStateCommandType.Manual,
            RotationTypeAutoSupport => AutorotIpcService.RsrStateCommandType.Henched,
            RotationTypePreviouslyEngagedTargets => AutorotIpcService.RsrStateCommandType.Auto,
            _ => AutorotIpcService.RsrStateCommandType.Auto,
        };
    }

    internal static AutorotIpcService.RsrTargetHostileType ResolveRsrTargetHostileType(int aggroType)
    {
        return aggroType switch
        {
            1 => AutorotIpcService.RsrTargetHostileType.TargetsHaveTarget,
            2 => AutorotIpcService.RsrTargetHostileType.AllTargetsWhenSoloInDuty,
            3 => AutorotIpcService.RsrTargetHostileType.AllTargetsWhenSolo,
            4 => AutorotIpcService.RsrTargetHostileType.SoloDeepDungeonSmart,
            _ => AutorotIpcService.RsrTargetHostileType.AllTargetsCanAttack,
        };
    }

    internal static bool ShouldActivateConfiguredRotation(int rotationType)
        => rotationType != RotationTypeNone;

    internal static string[] BuildCoppeliaPowerlevelCombatOffCommands(bool includeRsrFallback = true)
    {
        return includeRsrFallback
            ? new[] { "/bmrai off", "/vbmai off", "/rotation cancel", "/wrath auto off" }
            : new[] { "/bmrai off", "/vbmai off", "/wrath auto off" };
    }

    private static string DescribeBossModAiSetting(int bossModAI)
        => bossModAI == 1 ? "off" : "on";

    private void HandleCoppeliaPowerlevelLease(CharacterConfig config, bool inCombat, bool inDuty)
    {
        ResetCombatSettingsRefreshTracking();
        lastObservedCombatSettingsSignature = string.Empty;
        State = CombatState.OutOfCombat;
        StateDetail = "Coppelia PowerlevelBot lease active";
        ActivePreset = "";
        wasInCombat = inCombat;
        wasInDuty = inDuty;

        if (!plugin.CoppeliaPowerlevelLeaseService.TryClaimCombatSuppression())
            return;

        plugin.CaptureExternalAutomationSnapshot("Coppelia PowerlevelBot lease");
        var rsrHandled = plugin.AutorotIpcService.TrySetRsrMode(AutorotIpcService.RsrStateCommandType.Off);
        var daedalusHandled = SetDaedalusEnabled(false, "Coppelia PowerlevelBot lease");
        foreach (var command in BuildCoppeliaPowerlevelCombatOffCommands(includeRsrFallback: !rsrHandled))
            SendCommand(command);

        mountedRotationSuppressed = false;
        mountedSuppressedPluginName = string.Empty;
        wrathAutoActive = false;
        lastActivePluginIdx = -1;
        lastRotationToggleMs = 0;
        Plugin.Log.Information(
            rsrHandled
                ? $"[FrenRider][CoppeliaPowerlevel] Forced BMR/VBM/RSR/Wrath off; RSR stopped via IPC; Daedalus {(daedalusHandled ? "stopped via IPC" : "IPC unavailable")}."
                : $"[FrenRider][CoppeliaPowerlevel] Forced BMR/VBM/RSR/Wrath off; RSR fallback command sent; Daedalus {(daedalusHandled ? "stopped via IPC" : "IPC unavailable")}.");
    }

    private void DisableOtherRotationPlugins(CharacterConfig config)
    {
        // Only disable conflicting rotation engines. BossMod AI state is applied from the user's setting.
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
                case "DAEDALUS":
                    SetDaedalusEnabled(false, $"selected rotation plugin is {pluginName}");
                    break;
            }
        }
    }

    private bool SetDaedalusEnabled(bool enabled, string reason)
    {
        var handled = plugin.AutorotIpcService.TrySetDaedalusEnabled(enabled);
        if (handled)
            Plugin.Log.Information($"Combat: Daedalus {(enabled ? "enabled" : "disabled")} after {reason}");
        else
            Plugin.Log.Debug($"Combat: Daedalus SetEnabled IPC unavailable after {reason}");

        return handled;
    }

    private static string GetBossModPresetProvider(string pluginName)
        => string.Equals(pluginName, "VBM", StringComparison.OrdinalIgnoreCase) ? "VBM" : "BMR";

    private void SetWrathAuto(bool enabled, string reason)
    {
        if (ShouldSuppressFrenRiderCombatCommands || wrathAutoActive == enabled)
            return;

        SendCommand(enabled ? "/wrath auto on" : "/wrath auto off");
        wrathAutoActive = enabled;
        if (enabled)
            plugin.MarkWrathAutoStartedByFrenRider(reason);
        Plugin.Log.Information($"Combat: Wrath auto {(enabled ? "on" : "off")} after {reason}");
    }

    private void CheckLimitBreak(CharacterConfig config)
    {
        // LB automation: send LB command when HP threshold reached
        // This is a stub — actual implementation needs target HP checking
        // config.LimitPct: percentage threshold (-1 = disabled)
        // Future: check target's HP % and send /ac "Limit Break" when below threshold
    }

    private unsafe void SendCommand(string command, bool allowDuringQuestionableSolo = false)
    {
        if (ShouldSuppressFrenRiderCombatCommands && !allowDuringQuestionableSolo)
            return;

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
        return !ShouldActivateConfiguredRotation(config.RotationType);
    }

    private static bool IsInDuty()
        => Plugin.Condition[ConditionFlag.BoundByDuty]
            || Plugin.Condition[ConditionFlag.BoundByDuty56]
            || Plugin.Condition[ConditionFlag.BoundByDuty95];

    private static string FormatCommandArgument(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        return value.IndexOfAny([' ', '\t', '"']) >= 0
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }
}
