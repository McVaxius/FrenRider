using System;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FrenRider.Models;
using Lumina.Excel.Sheets;

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
    private CombatSettingsSnapshot? lastObservedCombatSettings;
    private DadDungeonRsrAggroOwnership? dungeonRsrAggro;
    private bool pendingDungeonRsrAggroApply;
    private QuestingRsrAggroOwnership? questingRsrAggro;
    private QuestingRsrStopSelection? questingRsrStopSelection;
    private bool questingVbmHeld;
    private CombatSettingsSnapshot? pendingCombatSettings;
    private CombatSettingsSnapshot? lastAppliedCombatSettings;
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
        => IsQuestionableSoloAuthorityActive || plugin.AdsIntegrationService.IsSoloCombatHeld;

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
        ReleaseQuestingRsrAggroForDeparture(reason);
        questingRsrStopSelection = null;
        questingVbmHeld = false;
        if (mountedRotationSuppressed || wrathAutoActive)
            Plugin.Log.Information($"[FrenRider] Cleared local external automation runtime flags after {reason}.");

        mountedRotationSuppressed = false;
        mountedSuppressedPluginName = string.Empty;
        wrathAutoActive = false;
        lastRotationToggleMs = 0;
        wasInCombat = false;
        wasInDuty = false;
        lastAppliedCombatSettings = null;
        lastObservedCombatSettings = null;
        ResetCombatSettingsRefreshTracking();
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
        RefreshQuestingRsrAggro(config, inDuty);
        if (HoldQuestingVbm(config, GetSelectedRotationPluginName(config)))
            return false;

        // if (decision.ShouldForceCombatOff)
        //     ForceDutyCombatOff("QuestionableSolo duty authority");

        if (plugin.AdsIntegrationService.IsSoloCombatHeld)
        {
            HoldAdsSoloCombat(decision, Plugin.Condition[ConditionFlag.InCombat], inDuty);
            return false;
        }

        if (decision.Authority == DutyCombatAuthority.QuestionableSolo)
        {
            SetQuestionableSoloSuppressedState(Plugin.Condition[ConditionFlag.InCombat], inDuty);
            return false;
        }

        return !IsCombatSetupHeld(inDuty)
            && !DeferAdsInteractionVbmSetup(config, GetSelectedRotationPluginName(config));
    }

    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var inCombat = Plugin.Condition[ConditionFlag.InCombat];
        var inDuty = IsInDuty();
        var mountedOrMounting = Plugin.Condition[ConditionFlag.Mounted] || Plugin.Condition[ConditionFlag.Mounting71];
        var now = Environment.TickCount64;
        ObserveDungeonRsrAggro(config, inDuty);
        plugin.AutorotIpcService.ObserveOwnedBossModIdentity(GetSelectedRotationPluginName(config),
            plugin.ConfigManager.CurrentAccountId, plugin.ConfigManager.ActiveCharacterKey, config);

        if (!config.Enabled)
        {
            ClearExternalAutomationRuntimeState("plugin disabled");
            ResetCombatSettingsRefreshTracking();
            lastObservedCombatSettings = null;
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
        RefreshQuestingRsrAggro(config, inDuty);
        var authorityDecision = RefreshDutyCombatAuthority(
            config,
            inDuty,
            questionableSnapshot,
            frenRiderBootstrapAllowed: !plugin.CoppeliaPowerlevelLeaseService.IsLeaseActive
                && !plugin.AdsHyperFocusLeaseService.IsLeaseActive
                && !plugin.AutomationService.IsUtilityGateActive
                && (IsRotationDisabled(config)
                    || !inDuty
                    || !IsAdsInteractionVbmPauseActive(GetSelectedRotationPluginName(config))));

        // if (authorityDecision.ShouldForceCombatOff)
        //     ForceDutyCombatOff("QuestionableSolo duty authority");

        if (plugin.AdsIntegrationService.IsSoloCombatHeld)
        {
            HoldAdsSoloCombat(authorityDecision, inCombat, inDuty);
            return;
        }

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

        if (plugin.AdsHyperFocusLeaseService.IsLeaseActive)
        {
            HandleAdsHyperFocusLease(inCombat, inDuty);
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
            lastObservedCombatSettings = null;
            State = CombatState.OutOfCombat;
            StateDetail = "ADS utility active";
            ActivePreset = "";
            wasInCombat = inCombat;
            wasInDuty = inDuty;
            return;
        }

        LogFateCombatDecisionIfChanged(config, inCombat, inDuty, mountedOrMounting);

        if (HoldQuestingVbm(config, GetSelectedRotationPluginName(config)))
        {
            wasInCombat = false;
            wasInDuty = false;
            return;
        }

        if (HandleMountedRotationLifecycle(config, mountedOrMounting, inCombat, inDuty))
            return;

        if (questingVbmHeld && !IsCombatSetupHeld(inDuty)
            && (GetSelectedRotationPluginName(config) != "VBM" || plugin.AutorotIpcService.CanResumeOwnedVbm())
            && !IsAdsInteractionVbmPauseActive(GetSelectedRotationPluginName(config)))
        {
            questingVbmHeld = false;
            if (GetSelectedRotationPluginName(config) == "VBM" && !IsRotationDisabled(config))
            {
                if (inCombat || inDuty) ActivateRotation(config, ignoreCooldown: true);
                else ApplyPassiveRotationSettings(config, "quest automation stopped");
            }
        }

        if (!IsRotationDisabled(config)
            && lastBossModMovementUnlockSignature.EndsWith("|VBM paused", StringComparison.Ordinal))
        {
            var pluginName = GetSelectedRotationPluginName(config);
            ApplyBossModMovementUnlockOnce(pluginName, GetBossModPresetForPlugin(config, pluginName),
                "ADS interaction VBM pause release");
        }

        if (plugin.AdsIntegrationService.ShouldPauseDutySystems)
        {
            TrackCombatSettingsChanges(config, now);
            TryApplyPendingCombatSettingsRefresh(config, now, inCombat, inDuty);
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

        // ADS owns navigation; FrenRider still applies eligible combat-setting edits.
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
            frenRiderBootstrapAllowed,
            plugin.AdsIntegrationService.IsSoloCombatHeld));

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
        lastObservedCombatSettings = null;
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

        lastObservedCombatSettings = CaptureCombatSettings(config);
        Plugin.Log.Information(
            $"[FrenRider][DutyAuthority] FrenRider combat bootstrap completed once for duty; " +
            $"rotationDisabled={IsRotationDisabled(config)}; adsPause={plugin.AdsIntegrationService.ShouldPauseDutySystems}.");
    }

    private void SetQuestionableSoloSuppressedState(bool inCombat, bool inDuty)
    {
        ResetCombatSettingsRefreshTracking();
        lastObservedCombatSettings = null;
        wasInCombat = inCombat;
        wasInDuty = inDuty;
        State = CombatState.OutOfCombat;
        StateDetail = "QuestionableSolo authority; FrenRider combat suppressed";
        ActivePreset = "";
    }

    private void HoldAdsSoloCombat(DutyCombatAuthorityDecision decision, bool inCombat, bool inDuty)
    {
        if (decision.ShouldForceCombatOff)
            ForceDutyCombatOff("ADS solo handoff readiness hold");

        ResetCombatSettingsRefreshTracking();
        lastObservedCombatSettings = null;
        wasInCombat = inCombat;
        wasInDuty = inDuty;
        State = CombatState.OutOfCombat;
        StateDetail = $"Combat held: {plugin.AdsIntegrationService.StatusText}";
        ActivePreset = "";
    }

    private void ForceDutyCombatOff(string reason)
    {
        plugin.CaptureExternalAutomationSnapshot(reason);

        var rsrHandled = plugin.AutorotIpcService.TrySetRsrMode(AutorotIpcService.RsrStateCommandType.Off);
        var daedalusHandled = SetDaedalusEnabled(false, reason);
        plugin.AutorotIpcService.DisableOwnedBossModRuntime();
        foreach (var command in BuildQuestionableDutyCombatOffCommands(includeRsrFallback: !rsrHandled))
            SendCommand(command, allowWhileSuppressed: true);

        wrathAutoActive = false;
        lastActivePluginIdx = -1;
        lastRotationToggleMs = 0;
        lastAppliedCombatSettings = null;
        ActivePreset = "";

        mountedRotationSuppressed = false;
        mountedSuppressedPluginName = string.Empty;

        Plugin.Log.Information(
            rsrHandled
                ? $"[FrenRider][DutyAuthority] {reason}: forced BMR/VBM/RSR/Wrath off; RSR stopped via IPC; Daedalus {(daedalusHandled ? "stopped via IPC" : "IPC unavailable")}."
                : $"[FrenRider][DutyAuthority] {reason}: forced BMR/VBM/RSR/Wrath off; RSR fallback command sent; Daedalus {(daedalusHandled ? "stopped via IPC" : "IPC unavailable")}.");
    }

    private void ActivateRotation(CharacterConfig config, bool ignoreCooldown = false)
    {
        if (!config.Enabled || IsRotationDisabled(config) || ShouldSuppressFrenRiderCombatCommands)
            return;

        var pluginName = GetSelectedRotationPluginName(config);
        if (HoldQuestingVbm(config, pluginName) || pluginName == "VBM" && (IsSafetyCombatSetupHeld(IsInDuty())
            || questingVbmHeld && !plugin.AutorotIpcService.CanResumeOwnedVbm()))
            return;
        if (DeferAdsInteractionVbmSetup(config, pluginName))
            return;

        var now = Environment.TickCount64;
        if (!ignoreCooldown && now - lastRotationToggleMs < 2000) return; // Cooldown
        lastRotationToggleMs = now;

        // Select rotation plugin (different for foray)
        if (pluginName == "RSR" && (dungeonRsrAggro is not null || questingRsrAggro is not null || questingRsrStopSelection is not null
            || questionableIpcService.QuestAutomationActive) && !ApplyRsrAggro(config))
            return;
        ValidateCurrentManualPreset(config, pluginName);
        lastActivePluginIdx = Array.IndexOf(RotationPluginNames, pluginName);
        var bossModPreset = GetBossModPresetForPlugin(config, pluginName);
        ActivePreset = bossModPreset;

        // Disable other rotation plugins first
        plugin.CaptureExternalAutomationSnapshot("rotation activation");
        DisableOtherRotationPlugins(config);
        if (!ApplyBossModSafetyState(config, pluginName, bossModPreset, "activation"))
            return;

        // Send activation commands
        switch (pluginName)
        {
            case "RSR":
                var rsrModeName = ApplyRsrMode(config);
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
        lastAppliedCombatSettings = CaptureCombatSettings(config);
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
        if (!ApplyRsrAggro(config) && (dungeonRsrAggro is not null || questingRsrAggro is not null || questingRsrStopSelection is not null
            || questionableIpcService.QuestAutomationActive))
            return "Targeting unavailable";

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
        => SelectManualPresetForZone(config, zoneService.CurrentZone, zoneService.InFate);

    internal static string SelectManualPresetForZone(CharacterConfig config, ZoneType zone, bool inFate)
    {
        if (inFate && !config.IgnoreFates)
            return config.AutoRotationTypeFATE;

        return zone switch
        {
            ZoneType.DeepDungeon => config.AutoRotationTypeDD,
            _ => config.AutoRotationType,
        };
    }

    internal static int GetManualPresetSelector(CharacterConfig config, ZoneType zone, bool inFate)
        => inFate && !config.IgnoreFates ? 2 : zone == ZoneType.DeepDungeon ? 1 : 0;

    internal static string ReadManualPresetSelector(CharacterConfig config, int selector)
        => selector == 2 ? config.AutoRotationTypeFATE : selector == 1 ? config.AutoRotationTypeDD : config.AutoRotationType;

    internal static void WriteManualPresetSelector(CharacterConfig config, int selector, string value)
    {
        if (selector == 2) config.AutoRotationTypeFATE = value;
        else if (selector == 1) config.AutoRotationTypeDD = value;
        else config.AutoRotationType = value;
    }

    internal bool IsCurrentManualPresetSelector(CharacterConfig config, int selector)
        => ReferenceEquals(config, plugin.ConfigManager.GetActiveConfig())
            && selector == GetManualPresetSelector(config, zoneService.CurrentZone, zoneService.InFate);

    internal string GetConfiguredRotationProvider(CharacterConfig config) => GetSelectedRotationPluginName(config);

    private void ValidateCurrentManualPreset(CharacterConfig config, string pluginName)
    {
        if (!config.ConfigureRotationPresetManually)
            return;
        var selector = GetManualPresetSelector(config, zoneService.CurrentZone, zoneService.InFate);
        var saved = ReadManualPresetSelector(config, selector);
        var catalog = plugin.AutorotIpcService.ReadPresetCatalog(pluginName);
        var resolved = AutorotIpcService.ResolvePresetSelection(saved, catalog);
        if (string.Equals(saved, resolved, StringComparison.Ordinal))
            return;
        WriteManualPresetSelector(config, selector, resolved);
        plugin.ConfigManager.SaveCurrentAccount();
    }

    private string GetBossModPresetForPlugin(CharacterConfig config, string pluginName)
        => SelectBossModPresetForProvider(config, pluginName, zoneService.CurrentZone, zoneService.InFate,
            config.ConfigureRotationPresetManually ? string.Empty : GetManagedPresetRole());

    internal static string SelectBossModPresetForProvider(CharacterConfig config, string provider, ZoneType zone,
        bool inFate, string role)
        => config.ConfigureRotationPresetManually ? SelectManualPresetForZone(config, zone, inFate)
            : provider is "BMR" or "VBM" ? $"FRENRIDER - {role}" : $"passive - {role.ToLowerInvariant()}";

    private string GetManagedPresetRole()
    {
        var jobId = GetCurrentClassJobId();
        if (!jobId.HasValue)
            return ManagedPresetRoleRanged;

        return jobId.Value switch
        {
            1 or 3 or 19 or 21 or 32 or 37 => ManagedPresetRoleTank,
            2 or 4 or 20 or 22 or 29 or 30 or 34 or 39 or 41 or 43 => ManagedPresetRoleMelee,
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
        if (HoldQuestingVbm(config, pluginName) || pluginName == "VBM" && (IsSafetyCombatSetupHeld(IsInDuty())
            || questingVbmHeld && !plugin.AutorotIpcService.CanResumeOwnedVbm()))
            return;
        if (DeferAdsInteractionVbmSetup(config, pluginName))
            return;
        if (pluginName == "RSR" && (dungeonRsrAggro is not null || questingRsrAggro is not null || questingRsrStopSelection is not null
            || questionableIpcService.QuestAutomationActive) && !ApplyRsrAggro(config))
            return;
        ValidateCurrentManualPreset(config, pluginName);
        lastActivePluginIdx = Array.IndexOf(RotationPluginNames, pluginName);
        var bossModPreset = GetBossModPresetForPlugin(config, pluginName);
        ActivePreset = bossModPreset;
        plugin.CaptureExternalAutomationSnapshot($"rotation settings after {reason}");
        DisableOtherRotationPlugins(config);
        if (!ApplyBossModSafetyState(config, pluginName, bossModPreset, reason))
            return;

        switch (pluginName)
        {
            case "RSR":
                ApplyRsrAggro(config);
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

        lastAppliedCombatSettings = CaptureCombatSettings(config);
        Plugin.Log.Information($"Combat: Reapplied {pluginName} settings after {reason} with BossMod preset '{bossModPreset}'");
    }

    public void ApplyPresetSelection(string reason, bool installPresets = true)
    {
        if (plugin.AdsIntegrationService.IsSoloCombatHeld)
            return;
        var config = plugin.ConfigManager.GetActiveConfig();
        if (HoldQuestingVbm(config, GetSelectedRotationPluginName(config)))
            return;
        if (config.Enabled && IsCombatSetupHeld(IsInDuty()))
            return;
        var pluginName = GetSelectedRotationPluginName(config);
        if (DeferAdsInteractionVbmSetup(config, pluginName))
            return;
        if (config.Enabled)
            plugin.CaptureExternalAutomationSnapshot(reason);
        if (installPresets)
            plugin.AutorotIpcService.CreatePresets(force: true, rotationProvider: pluginName);

        if (ShouldSuppressFrenRiderCombatCommands)
            return;

        if (IsRotationDisabled(config))
            return;

        ValidateCurrentManualPreset(config, pluginName);
        lastActivePluginIdx = Array.IndexOf(RotationPluginNames, pluginName);
        var bossModPreset = GetBossModPresetForPlugin(config, pluginName);
        ActivePreset = bossModPreset;
        ApplyBossModPreset(pluginName, bossModPreset, reason, installPresets: false);
    }

    public void ApplyBossModFollowStartupDefaults()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        if (HoldQuestingVbm(config, GetSelectedRotationPluginName(config)))
            return;
        if (IsCombatSetupHeld(IsInDuty()))
            return;

        plugin.CaptureExternalAutomationSnapshot("BossMod follow startup defaults");
        SendCommand("/bmrai followoutofcombat off");
        SendCommand("/cbt disable AutoFollow");
        SendCommand("/bmrai followcombat off");
        if (!IsAdsInteractionVbmPauseActive("VBM"))
            SendCommand("/vbmai follow Slot1");
    }

    private bool IsCombatSetupHeld(bool inDuty)
        => ShouldSuppressFrenRiderCombatCommands || IsSafetyCombatSetupHeld(inDuty);

    private bool IsSafetyCombatSetupHeld(bool inDuty)
        => plugin.AutomationService.IsUtilityGateActive
            || plugin.CoppeliaPowerlevelLeaseService.IsLeaseActive || plugin.AdsHyperFocusLeaseService.IsLeaseActive
            || plugin.PhoenixDownRecoveryService.HoldActions
            || !inDuty && (Plugin.Condition[ConditionFlag.Mounted] || Plugin.Condition[ConditionFlag.Mounting71]);

    internal static bool ShouldHoldQuestingVbm(bool enabled, string provider, bool questActive, bool inDuty, bool inCombat)
        => enabled && provider == "VBM" && questActive && !inDuty && !inCombat;

    private bool HoldQuestingVbm(CharacterConfig config, string provider)
    {
        questionableIpcService.Refresh();
        if (!ShouldHoldQuestingVbm(config.Enabled, provider, questionableIpcService.QuestAutomationActive,
                IsInDuty(), Plugin.Condition[ConditionFlag.InCombat]))
            return false;
        plugin.CaptureExternalAutomationSnapshot("questing idle VBM pause");
        var confirmed = plugin.AutorotIpcService.PauseOwnedVbm(command => TrySendCommand(command));
        questingVbmHeld = true;
        lastRotationToggleMs = 0;
        lastAppliedCombatSettings = null;
        lastObservedCombatSettings = null;
        ResetCombatSettingsRefreshTracking();
        State = CombatState.OutOfCombat;
        ActivePreset = string.Empty;
        StateDetail = confirmed ? "Questing idle; VBM paused" : "Questing idle; VBM pause unconfirmed";
        return true;
    }

    private bool IsAdsInteractionVbmPauseActive(string pluginName)
    {
        if (!string.Equals(pluginName, "VBM", StringComparison.OrdinalIgnoreCase))
            return false;

        var identity = AdsIntegrationService.ReadLiveDutyIdentity();
        plugin.AdsDutyIpcService.Refresh(IsInDuty(), identity.TerritoryTypeId,
            identity.ContentFinderConditionId, force: true);
        return plugin.AdsDutyIpcService.IsInteractionVbmPauseActive;
    }

    private bool DeferAdsInteractionVbmSetup(CharacterConfig config, string pluginName)
    {
        if (!IsAdsInteractionVbmPauseActive(pluginName))
            return false;

        DeferCombatSettingsRefresh(config);
        return true;
    }

    private void DeferCombatSettingsRefresh(CharacterConfig config)
    {
        if (config.Enabled && !IsRotationDisabled(config))
        {
            lastAppliedCombatSettings = null;
            pendingCombatSettings = CaptureCombatSettings(config);
            pendingCombatSettingsRefreshMs = Environment.TickCount64 + CombatSettingsRefreshDebounceMs;
        }
        StateDetail = "ADS interaction active; VBM setup deferred";
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
        lastObservedCombatSettings = null;
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
        if (HoldQuestingVbm(config, pluginName))
            return;

        if (config.Enabled && !IsRotationDisabled(config) && config.BossModAI != 1
            && IsAdsInteractionVbmPauseActive(pluginName))
            return;

        switch (pluginName)
        {
            case "BMR":
                if (!ApplyConfiguredBossModAiState(config, pluginName, $"mounted lifecycle restore ({reason})"))
                    return;
                break;
            case "VBM":
                if (!ApplyConfiguredBossModAiState(config, pluginName, $"mounted lifecycle restore ({reason})"))
                    return;
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

        lastObservedCombatSettings = CaptureCombatSettings(config);
    }

    private void TrackCombatSettingsChanges(CharacterConfig config, long now)
    {
        var settings = CaptureCombatSettings(config);
        if (settings == lastObservedCombatSettings)
            return;

        lastObservedCombatSettings = settings;
        pendingCombatSettings = settings;
        pendingCombatSettingsRefreshMs = now + CombatSettingsRefreshDebounceMs;
    }

    private void TryApplyPendingCombatSettingsRefresh(CharacterConfig config, long now, bool inCombat, bool inDuty)
    {
        if (HoldQuestingVbm(config, GetSelectedRotationPluginName(config)))
            return;
        if (pendingCombatSettingsRefreshMs == 0 || now < pendingCombatSettingsRefreshMs)
            return;

        var settings = CaptureCombatSettings(config);
        if (settings != pendingCombatSettings)
            return;

        if (IsRotationDisabled(config))
        {
            ResetCombatSettingsRefreshTracking();
            lastObservedCombatSettings = settings;
            if (lastActivePluginIdx >= 0)
                DeactivateRotation(config);
            lastAppliedCombatSettings = settings;
            return;
        }

        if (IsAdsInteractionVbmPauseActive(settings.Provider))
        {
            if (settings.BossModAI == 1 && lastAppliedCombatSettings?.BossModAI != 1)
            {
                ApplyConfiguredBossModAiState(config, settings.Provider, "BossMod AI selection change");
                if (lastAppliedCombatSettings is { } applied)
                    lastAppliedCombatSettings = applied with { BossModAI = 1 };
                pendingCombatSettingsRefreshMs = now + CombatSettingsRefreshDebounceMs;
            }
            StateDetail = "ADS interaction active; VBM settings pending";
            return;
        }

        ResetCombatSettingsRefreshTracking();
        lastObservedCombatSettings = settings;

        if (RequiresCombatActivation(lastAppliedCombatSettings, settings))
        {
            if (inDuty || inCombat)
                ActivateRotation(config, ignoreCooldown: true);
            else
                ApplyPassiveRotationSettings(config, "Combat / AI config change");
            return;
        }
        var previous = lastAppliedCombatSettings!;
        if (previous.Preset != settings.Preset)
        {
            ValidateCurrentManualPreset(config, settings.Provider);
            var preset = GetBossModPresetForPlugin(config, settings.Provider);
            if (!ApplyBossModPreset(settings.Provider, preset, "preset selection change", installPresets: false))
                return;
        }
        if (previous.BossModAI != settings.BossModAI)
        {
            if (!ApplyConfiguredBossModAiState(config, settings.Provider, "BossMod AI selection change"))
                return;
        }
        if (settings.Provider == "RSR")
        {
            if (previous.RsrAggroType != settings.RsrAggroType || pendingDungeonRsrAggroApply)
                if (!ApplyRsrAggro(config)) return;
            if (previous.Positional != settings.Positional)
                SetPositional(config, settings.Provider);
        }
        if (settings.Provider == "DAEDALUS" && previous.DaedalusTargetMode != settings.DaedalusTargetMode)
            plugin.DaedalusTargetModeService.Apply(config.DaedalusTargetMode, notifyUser: false);
        lastAppliedCombatSettings = CaptureCombatSettings(config);
    }

    private void ResetCombatSettingsRefreshTracking()
    {
        pendingCombatSettingsRefreshMs = 0;
        pendingCombatSettings = null;
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

    private CombatSettingsSnapshot CaptureCombatSettings(CharacterConfig config)
        => new(config, GetSelectedRotationPluginName(config),
            GetBossModPresetForPlugin(config, GetSelectedRotationPluginName(config)), config.BossModAI,
            config.RotationType, GetEffectiveRsrAggro(config), config.PositionalInCombat, config.DaedalusTargetMode);

    internal static bool RequiresCombatActivation(CombatSettingsSnapshot? previous, CombatSettingsSnapshot current)
        => previous is null || !ReferenceEquals(previous.Profile, current.Profile)
            || previous.Provider != current.Provider || previous.RotationType != current.RotationType;

    private bool ApplyRsrAggro(CharacterConfig config)
    {
        if (ShouldSuppressFrenRiderCombatCommands || IsRotationDisabled(config))
            return false;
        if (dungeonRsrAggro is { } owned)
        {
            if (IsCombatSetupHeld(IsInDuty()) || !MatchesDungeonRsrOwner(owned, config)
                || !TryReadDungeonScope(owned.ContentFinderConditionId, out var territory)
                || territory != owned.TerritoryTypeId)
                return false;
            var applied = plugin.AutorotIpcService.ApplyDungeonRsrAggro(owned.Live);
            if (applied)
                pendingDungeonRsrAggroApply = false;
            if (!applied)
                Plugin.Log.Warning($"[FrenRider][DAD] {plugin.AutorotIpcService.LastStatus}");
            return applied;
        }
        if (questionableIpcService.QuestAutomationActive || questingRsrAggro is not null || questingRsrStopSelection is not null)
            return RefreshQuestingRsrAggro(config, IsInDuty());
        var hostileType = ResolveRsrTargetHostileType(GetEffectiveRsrAggro(config));
        return plugin.AutorotIpcService.TrySetRsrHostileType(hostileType);
    }

    internal bool AcquireDungeonRsrAggro(string runId, uint expectedContentFinderConditionId)
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var character = plugin.ConfigManager.QuestionableCharacterIdentity;
        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrEmpty(character)
            || GetSelectedRotationPluginName(config) != "RSR" || IsRotationDisabled(config)
            || !TryReadDungeonScope(expectedContentFinderConditionId, out var territory))
        {
            Plugin.Log.Warning("[FrenRider][DAD] Dungeon RSR targeting acquisition rejected: current character, provider or four-player dungeon scope is unavailable.");
            return false;
        }
        if (dungeonRsrAggro is { } existing)
            return existing.RunId == runId && existing.ContentFinderConditionId == expectedContentFinderConditionId
                && existing.TerritoryTypeId == territory && MatchesDungeonRsrOwner(existing, config);
        if (!ReleaseQuestingRsrAggroForDeparture("DAD targeting acquisition")
            || !plugin.AutorotIpcService.TryCaptureDungeonRsrAggro(out var live))
        {
            Plugin.Log.Warning($"[FrenRider][DAD] {plugin.AutorotIpcService.LastStatus}");
            return false;
        }
        dungeonRsrAggro = new DadDungeonRsrAggroOwnership(runId, character, config,
            expectedContentFinderConditionId, territory, config.RsrAggroType, live!);
        pendingDungeonRsrAggroApply = true;
        lastObservedCombatSettings = null;
        if (config.Enabled && !IsCombatSetupHeld(IsInDuty()))
            ApplyRsrAggro(config);
        Plugin.Log.Information($"[FrenRider][DAD] {plugin.AutorotIpcService.LastStatus}");
        return true;
    }

    internal bool ReleaseDungeonRsrAggro(string runId)
    {
        if (dungeonRsrAggro is null)
            return true;
        if (dungeonRsrAggro.RunId != runId
            || dungeonRsrAggro.CharacterIdentity != plugin.ConfigManager.QuestionableCharacterIdentity)
            return false;
        return ReleaseDungeonRsrAggroForDeparture("matching DAD run release");
    }

    internal bool ReleaseDungeonRsrAggroForDeparture(string reason, bool preserveNewerSelection = false)
    {
        if (dungeonRsrAggro is not { } owned)
            return true;
        dungeonRsrAggro = null;
        pendingDungeonRsrAggroApply = false;
        lastObservedCombatSettings = null;
        if (owned.CharacterIdentity != plugin.ConfigManager.QuestionableCharacterIdentity)
        {
            Plugin.Log.Warning($"[FrenRider][DAD] Dungeon RSR targeting restoration is unavailable: the matching character is no longer readable ({reason}).");
            return false;
        }
        var restored = plugin.AutorotIpcService.ReleaseDungeonRsrAggro(owned.Live, preserveNewerSelection);
        if (!preserveNewerSelection && lastAppliedCombatSettings is { } applied
            && ReferenceEquals(applied.Profile, owned.Profile))
            lastAppliedCombatSettings = applied with { RsrAggroType = owned.Profile.RsrAggroType };
        if (restored)
            Plugin.Log.Information($"[FrenRider][DAD] {plugin.AutorotIpcService.LastStatus} ({reason})");
        else
            Plugin.Log.Warning($"[FrenRider][DAD] {plugin.AutorotIpcService.LastStatus} ({reason})");
        return restored;
    }

    private int GetEffectiveRsrAggro(CharacterConfig config)
        => dungeonRsrAggro is { } dad && MatchesDungeonRsrOwner(dad, config)
            ? (int)AutorotIpcService.RsrTargetHostileType.AllTargetsCanAttack
            : ResolveQuestingRsrAggro(config, questionableIpcService.QuestAutomationActive, IsInDuty());

    internal static int ResolveQuestingRsrAggro(CharacterConfig config, bool active, bool inDuty)
        => active ? (int)(inDuty ? AutorotIpcService.RsrTargetHostileType.AllTargetsCanAttack
                : AutorotIpcService.RsrTargetHostileType.TargetsHaveTarget)
            : config.RotationType == RotationTypePreviouslyEngagedTargets
                ? (int)AutorotIpcService.RsrTargetHostileType.TargetsHaveTarget : config.RsrAggroType;

    private bool RefreshQuestingRsrAggro(CharacterConfig config, bool inDuty)
    {
        var account = plugin.ConfigManager.CurrentAccountId;
        var character = plugin.ConfigManager.QuestionableCharacterIdentity;
        var active = questionableIpcService.QuestAutomationActive;
        if (questingRsrStopSelection is { } stopped)
        {
            if (!active && stopped.Account == account && stopped.Character == character
                && ReferenceEquals(stopped.Profile, config) && stopped.Aggro == config.RsrAggroType
                && stopped.Mode == config.RotationType && GetSelectedRotationPluginName(config) == "RSR")
            {
                if (!plugin.AutorotIpcService.TryObserveRsrAggroOwner(stopped.Live, out var same)) return false;
                if (same) return stopped.Confirmed;
            }
            questingRsrStopSelection = null;
        }
        if (questingRsrAggro is { } previous && (previous.Account != account || previous.Character != character
            || !ReferenceEquals(previous.Profile, config) || GetSelectedRotationPluginName(config) != "RSR"))
            ReleaseQuestingRsrAggroForDeparture("questing targeting identity departure");
        if (questingRsrAggro is { } current)
        {
            if (!plugin.AutorotIpcService.TryObserveRsrAggroOwner(current.Live, out var matches)) return false;
            if (!matches) ReleaseQuestingRsrAggroForDeparture("native RSR targeting departure");
        }
        if (!config.Enabled || GetSelectedRotationPluginName(config) != "RSR" || string.IsNullOrEmpty(character)
            || dungeonRsrAggro is not null || plugin.AdsIntegrationService.IsSoloCombatHeld
            || IsSafetyCombatSetupHeld(inDuty) || plugin.AdsDutyIpcService.IsInteractionVbmPauseActive)
            return false;
        if (!active && questingRsrAggro is null) return true;
        var target = ResolveRsrTargetHostileType(ResolveQuestingRsrAggro(config, active, inDuty));
        if (!active && questingRsrAggro is { } finished)
        {
            if (!plugin.AutorotIpcService.StopOwnedRsrAggro(finished.Live, target, out var confirmed)) return false;
            // This remembers stop intent only. It is never used to restore the pre-quest value.
            questingRsrStopSelection = new(account, character, config, config.RsrAggroType, config.RotationType, confirmed, finished.Live);
            questingRsrAggro = null;
            ResetCombatSettingsRefreshTracking();
            lastObservedCombatSettings = CaptureCombatSettings(config);
            if (confirmed && lastAppliedCombatSettings is { } applied)
                lastAppliedCombatSettings = applied with { RsrAggroType = (int)target };
            return confirmed;
        }
        if (questingRsrAggro is null)
        {
            if (!plugin.AutorotIpcService.TryCaptureRsrAggro(target, out var live)) return false;
            questingRsrAggro = new(account, character, config, live!);
        }
        return plugin.AutorotIpcService.ApplyOwnedRsrAggro(questingRsrAggro.Live, target);
    }

    internal bool ReleaseQuestingRsrAggroForDeparture(string reason)
    {
        if (questingRsrAggro is not { } owned) return true;
        questingRsrAggro = null;
        if (owned.Account != plugin.ConfigManager.CurrentAccountId
            || owned.Character != plugin.ConfigManager.QuestionableCharacterIdentity)
            return false;
        return plugin.AutorotIpcService.ReleaseOwnedRsrAggro(owned.Live);
    }

    private bool MatchesDungeonRsrOwner(DadDungeonRsrAggroOwnership owned, CharacterConfig config)
        => owned.Matches(plugin.ConfigManager.QuestionableCharacterIdentity, config);

    private void ObserveDungeonRsrAggro(CharacterConfig config, bool inDuty)
    {
        if (dungeonRsrAggro is not { } owned)
            return;
        if (!MatchesDungeonRsrOwner(owned, config) || GetSelectedRotationPluginName(config) != "RSR"
            || IsRotationDisabled(config))
        {
            ReleaseDungeonRsrAggroForDeparture("effective character/profile/selection departure",
                preserveNewerSelection: ReferenceEquals(owned.Profile, config) && owned.SavedAggroType != config.RsrAggroType);
            return;
        }
        var identity = AdsIntegrationService.ReadLiveDutyIdentity();
        if (!inDuty && identity.ContentFinderConditionId == 0 || identity.ContentFinderConditionId != 0
            && (identity.ContentFinderConditionId != owned.ContentFinderConditionId || identity.TerritoryTypeId != owned.TerritoryTypeId))
            ReleaseDungeonRsrAggroForDeparture("dungeon exit or replacement");
    }

    private static bool TryReadDungeonScope(uint expectedContentFinderConditionId, out uint territory)
    {
        territory = 0;
        try
        {
            if (expectedContentFinderConditionId == 0 || !Plugin.ClientState.IsLoggedIn
                || Plugin.Condition[ConditionFlag.LoggingOut]
                || Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51])
                return false;
            var identity = AdsIntegrationService.ReadLiveDutyIdentity();
            var row = Plugin.DataManager.GetExcelSheet<ContentFinderCondition>()?.GetRowOrDefault(expectedContentFinderConditionId);
            if (row is not { } duty)
                return false;
            var memberType = duty.ContentMemberType.ValueNullable;
            if (identity.ContentFinderConditionId != expectedContentFinderConditionId || identity.TerritoryTypeId == 0
                || identity.TerritoryTypeId != Plugin.ClientState.TerritoryType || duty.TerritoryType.RowId != identity.TerritoryTypeId
                || !IsFourPlayerDungeon(duty.ContentType.RowId, memberType?.MembersPerParty ?? 0, memberType?.PartyCount ?? 0))
                return false;
            territory = identity.TerritoryTypeId;
            return true;
        }
        catch { return false; }
    }

    internal static bool IsFourPlayerDungeon(uint contentType, int membersPerParty, int partyCount)
        => contentType == 2 && membersPerParty == 4 && partyCount == 1;

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

    private bool ApplyBossModSafetyState(CharacterConfig config, string pluginName, string selectedPreset, string reason)
    {
        if (ShouldSuppressFrenRiderCombatCommands || DeferAdsInteractionVbmSetup(config, pluginName))
            return false;

        ApplyBossModDefaultSettingsOnce(pluginName, selectedPreset, reason);
        ApplyBossModMovementUnlockOnce(pluginName, selectedPreset, reason);
        if (!ApplyBossModPreset(pluginName, selectedPreset, reason))
            return false;

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

        return ApplyConfiguredBossModAiState(config, pluginName, reason);
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
        if (plugin.PhoenixDownRecoveryService.HoldMovement)
            return;
        var signature = BuildBossModSafetySignature(pluginName, selectedPreset);
        if (string.Equals(signature, lastBossModMovementUnlockSignature, StringComparison.Ordinal))
            return;

        var vbmPaused = IsAdsInteractionVbmPauseActive("VBM");
        var partialSignature = signature + "|VBM paused";
        if (!string.Equals(partialSignature, lastBossModMovementUnlockSignature, StringComparison.Ordinal))
        {
            plugin.CaptureExternalAutomationSnapshot("BossMod movement unlock");
            SendCommand("/bmrai forbidmovement off");
        }
        if (vbmPaused)
        {
            lastBossModMovementUnlockSignature = partialSignature;
            return;
        }

        lastBossModMovementUnlockSignature = signature;
        SendCommand("/vbmai forbidmovement off");
        Plugin.Log.Information($"[FrenRider] Sent one-shot BossMod movement unlock after {reason}.");
    }

    private string BuildBossModSafetySignature(string pluginName, string selectedPreset)
        => string.Join("|", pluginName, selectedPreset, zoneService.TerritoryId, zoneService.CurrentZone);

    private bool ApplyBossModPreset(string pluginName, string presetName, string reason, bool installPresets = true)
    {
        if (ShouldSuppressFrenRiderCombatCommands || !ShouldApplyPreset(presetName))
            return true;

        var config = plugin.ConfigManager.GetActiveConfig();
        if (HoldQuestingVbm(config, pluginName) || pluginName == "VBM" && questingVbmHeld
            && !plugin.AutorotIpcService.CanResumeOwnedVbm())
            return false;
        if (DeferAdsInteractionVbmSetup(config, pluginName))
            return false;
        if (config.Enabled)
            plugin.CaptureExternalAutomationSnapshot($"preset selection after {reason}");
        if (installPresets)
            plugin.AutorotIpcService.CreatePresets(force: true, rotationProvider: pluginName);
        var applied = config.Enabled ? plugin.AutorotIpcService.ApplyOwnedBossModPreset(presetName)
            : plugin.AutorotIpcService.ApplyUnownedBossModPreset(pluginName, presetName);
        if (applied)
            ActivePreset = presetName;
        else
        {
            ActivePreset = string.Empty;
            Plugin.Log.Warning($"Combat: BossMod preset retained after {reason}: {plugin.AutorotIpcService.LastStatus}");
        }
        return true;
    }

    private bool ApplyConfiguredBossModAiState(CharacterConfig config, string pluginName, string reason)
    {
        if (HoldQuestingVbm(config, pluginName) || pluginName == "VBM" && questingVbmHeld
            && !plugin.AutorotIpcService.CanResumeOwnedVbm())
            return false;
        if (config.BossModAI != 1 && (!config.Enabled || IsRotationDisabled(config)))
            return true;

        var vbmPaused = config.BossModAI != 1 && IsAdsInteractionVbmPauseActive(pluginName);
        var commands = BuildBossModAiCommands(config.BossModAI, pluginName, vbmPaused);
        if (vbmPaused)
        {
            DeferCombatSettingsRefresh(config);
            return false;
        }
        if (commands.Length == 0)
            return true;

        plugin.CaptureExternalAutomationSnapshot("BossMod AI state change");
        if (config.BossModAI != 1 && GetBossModPresetProvider(pluginName) == "BMR")
            plugin.AutorotIpcService.ApplyOwnedPreferredDistance(command => TrySendCommand(command), 1.5);
        foreach (var command in commands)
            SendCommand(command);

        Plugin.Log.Information($"Combat: BossMod AI {DescribeBossModAiSetting(config.BossModAI)} for {pluginName} after {reason}");
        return true;
    }

    internal static string[] BuildBossModAiCommands(int bossModAI, string pluginName,
        bool interactionVbmPauseActive = false)
    {
        if (bossModAI == 1)
            return new[] { "/bmrai off", "/vbmai off" };

        if (interactionVbmPauseActive && string.Equals(pluginName, "VBM", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();

        return string.Equals(pluginName, "VBM", StringComparison.OrdinalIgnoreCase)
            ? new[] { "/vbmai on" }
            : new[] { "/bmrai forbidactions off", "/bmrai on" };
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

    internal static string[] BuildAdsHyperFocusCombatCommands(bool includeRsrFallback = true)
    {
        return includeRsrFallback
            ? new[] { "/bmrai off", "/vbmai off", "/wrath auto off", "/rotation manual" }
            : new[] { "/bmrai off", "/vbmai off", "/wrath auto off" };
    }

    private static string DescribeBossModAiSetting(int bossModAI)
        => bossModAI == 1 ? "off" : "on";

    private void HandleCoppeliaPowerlevelLease(CharacterConfig config, bool inCombat, bool inDuty)
    {
        ResetCombatSettingsRefreshTracking();
        lastObservedCombatSettings = null;
        State = CombatState.OutOfCombat;
        StateDetail = "Coppelia PowerlevelBot lease active";
        ActivePreset = "";
        wasInCombat = inCombat;
        wasInDuty = inDuty;

        if (!plugin.CoppeliaPowerlevelLeaseService.TryClaimCombatSuppression())
            return;

        lastAppliedCombatSettings = null;
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

    internal void ActivateAdsHyperFocusLease()
    {
        if (plugin.AdsIntegrationService.IsSoloCombatHeld
            || !plugin.AdsHyperFocusLeaseService.IsLeaseActive
            || !plugin.AdsHyperFocusLeaseService.TryClaimCombatActivation())
            return;

        ResetCombatSettingsRefreshTracking();
        lastObservedCombatSettings = null;
        lastAppliedCombatSettings = null;
        plugin.CaptureExternalAutomationSnapshot("ADS Hyper Focus lease");

        foreach (var command in BuildAdsHyperFocusCombatCommands(includeRsrFallback: false))
            SendCommand(command, allowWhileSuppressed: true);
        var daedalusHandled = SetDaedalusEnabled(false, "ADS Hyper Focus lease");
        if (!plugin.AutorotIpcService.TrySetRsrMode(AutorotIpcService.RsrStateCommandType.Manual))
            SendCommand("/rotation manual", allowWhileSuppressed: true);

        mountedRotationSuppressed = false;
        mountedSuppressedPluginName = string.Empty;
        wrathAutoActive = false;
        lastActivePluginIdx = Array.IndexOf(RotationPluginNames, "RSR");
        lastRotationToggleMs = Environment.TickCount64;
        State = CombatState.InCombat;
        StateDetail = "ADS Hyper Focus lease active; RSR Manual";
        ActivePreset = string.Empty;
        Plugin.Log.Information(
            $"[FrenRider][AdsHyperFocus] Activated RSR Manual and stopped BMR/VBM/Wrath; Daedalus {(daedalusHandled ? "stopped via IPC" : "IPC unavailable")}.");
    }

    internal void RestoreConfiguredCombatAfterAdsHyperFocusLease(string reason)
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        if (!plugin.AutorotIpcService.TrySetRsrMode(AutorotIpcService.RsrStateCommandType.Off))
            SendCommand("/rotation cancel", allowWhileSuppressed: true);

        lastActivePluginIdx = -1;
        lastRotationToggleMs = 0;
        ActivePreset = string.Empty;

        var inDuty = IsInDuty();
        var adsOwnsDuty = plugin.AdsIntegrationService.IsHandoffPending
            || plugin.AdsIntegrationService.IsControllingDuty;
        if (!config.Enabled || !inDuty || !adsOwnsDuty)
        {
            State = CombatState.OutOfCombat;
            StateDetail = !config.Enabled
                ? "Disabled"
                : !inDuty
                    ? "ADS Hyper Focus ended outside duty"
                    : "ADS Hyper Focus ended without ADS duty ownership";
            return;
        }

        ClearExternalAutomationRuntimeState($"ADS Hyper Focus {reason}");
        ResetCombatSettingsRefreshTracking();
        lastObservedCombatSettings = null;
        State = CombatState.OutOfCombat;
        StateDetail = "ADS Hyper Focus ended; combat bootstrap pending";
        Plugin.Log.Information($"[FrenRider][AdsHyperFocus] Re-armed configured combat bootstrap after {reason}.");
    }

    private void HandleAdsHyperFocusLease(bool inCombat, bool inDuty)
    {
        ResetCombatSettingsRefreshTracking();
        lastObservedCombatSettings = null;
        State = CombatState.InCombat;
        StateDetail = "ADS Hyper Focus lease active; RSR Manual";
        ActivePreset = string.Empty;
        wasInCombat = inCombat;
        wasInDuty = inDuty;
        ActivateAdsHyperFocusLease();
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

    private void SendCommand(string command, bool allowWhileSuppressed = false)
        => plugin.AutorotIpcService.SendBossModAiCommand(command, value => TrySendCommand(value, allowWhileSuppressed));

    private unsafe bool TrySendCommand(string command, bool allowWhileSuppressed = false)
    {
        if (ShouldSuppressFrenRiderCombatCommands && !allowWhileSuppressed)
            return false;

        try
        {
            var uiModule = UIModule.Instance();
            if (uiModule == null)
            {
                Plugin.Log.Error($"Combat command failed [{command}]: UIModule is null");
                return false;
            }

            var bytes = Encoding.UTF8.GetBytes(command);
            var utf8String = Utf8String.FromSequence(bytes);
            uiModule->ProcessChatBoxEntry(utf8String, nint.Zero);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Combat command failed [{command}]: {ex.Message}");
            return false;
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

internal sealed record CombatSettingsSnapshot(CharacterConfig Profile, string Provider, string Preset,
    int BossModAI, int RotationType, int RsrAggroType, int Positional, DaedalusTargetMode DaedalusTargetMode);

internal sealed record QuestingRsrAggroOwnership(string Account, string Character, CharacterConfig Profile,
    DungeonRsrLiveOwnership Live);

internal sealed record QuestingRsrStopSelection(string Account, string Character, CharacterConfig Profile,
    int Aggro, int Mode, bool Confirmed, DungeonRsrLiveOwnership Live);

internal sealed record DadDungeonRsrAggroOwnership(string RunId, string CharacterIdentity, CharacterConfig Profile,
    uint ContentFinderConditionId, uint TerritoryTypeId, int SavedAggroType, DungeonRsrLiveOwnership Live)
{
    internal bool Matches(string characterIdentity, CharacterConfig profile)
        => CharacterIdentity == characterIdentity && ReferenceEquals(Profile, profile) && SavedAggroType == profile.RsrAggroType;

    internal int ResolveSelection(string characterIdentity, CharacterConfig profile)
        => Matches(characterIdentity, profile) ? (int)AutorotIpcService.RsrTargetHostileType.AllTargetsCanAttack : profile.RsrAggroType;
}
