using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ECommons;
using FrenRider.IPC;
using FrenRider.Services;
using FrenRider.Windows;

namespace FrenRider;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;
    [PluginService] internal static IToastGui ToastGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/frenrider";
    private const string AliasCommandName = "/fr";

    public Configuration Configuration { get; init; }
    public ConfigManager ConfigManager { get; init; }
    public FrenTracker FrenTracker { get; init; }
    public ZoneService ZoneService { get; init; }
    public FrenTeleportService FrenTeleportService { get; init; }
    public AdsDutyIpcService AdsDutyIpcService { get; init; }
    public AdsIntegrationService AdsIntegrationService { get; init; }
    public AdsUtilityIpcService AdsUtilityIpcService { get; init; }
    public AdsReflectionIpcService AdsReflectionIpcService { get; init; }
    public BossModActionTweaksService BossModActionTweaksService { get; init; }
    public BossModConflictWarningService BossModConflictWarningService { get; init; }
    public DaedalusTargetModeService DaedalusTargetModeService { get; init; }
    public ExternalAutomationCleanupService ExternalAutomationCleanupService { get; init; }
    public CoppeliaPowerlevelLeaseService CoppeliaPowerlevelLeaseService { get; init; }
    public AdsHyperFocusLeaseService AdsHyperFocusLeaseService { get; init; }
    public FollowService FollowService { get; init; }
    public MountService MountService { get; init; }
    public CombatService CombatService { get; init; }
    public AutomationService AutomationService { get; init; }
    public FormationService FormationService { get; init; }
    public AutorotIpcService AutorotIpcService { get; init; }
    public QuestionableIpcService QuestionableIpcService { get; init; }
    public PartyService PartyService { get; init; }
    public VideoPlaybackService VideoPlaybackService { get; init; }
    public DutyInteractService DutyInteractService { get; init; }
    public ExitBehaviourService ExitBehaviourService { get; init; }
    public FateSyncService FateSyncService { get; init; }
    public YesAlreadyIPC YesAlreadyIPC { get; init; }
    public CombatOnlyIPC CombatOnlyIPC { get; init; }
    public DadIPC DadIPC { get; init; }
    public AutoYesService AutoYesService { get; init; }
    public RespawnService RespawnService { get; init; }
    public AutoDutyDetectionService AutoDutyDetectionService { get; init; }
    public bool ECommonsAvailable { get; private set; }
    public string[] MountNames { get; private set; } = Array.Empty<string>();

    public readonly WindowSystem WindowSystem = new("FrenRider");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private MagiaMiniWindow MagiaMiniWindow { get; init; }
    private AutoDutyWarningWindow AutoDutyWarningWindow { get; init; }

    private IDtrBarEntry? dtrEntry;
    private bool wasLoggedIn;
    private int loginDetectionDelay;
    private bool wasPluginEnabled = false;
    private DateTime nextFrameworkHitchLogUtc = DateTime.MinValue;
    private double lastSlowUpdateMs;
    private string lastSlowUpdateSource = "none";
//	private readonly ICommandManager commandManager;

    public Plugin()
    {
        try
        {
            ECommonsMain.Init(PluginInterface, this);
            ECommonsAvailable = true;
            Log.Information("[FrenRider] ECommons initialized");
        }
        catch (Exception ex)
        {
            ECommonsAvailable = false;
            Log.Warning(ex, "[FrenRider] ECommons init failed; flying stuck escape disabled");
        }

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (Configuration.MigrateToCurrentVersion())
        {
            Configuration.Save();
            Log.Information($"[FrenRider] Migrated global configuration to v{Configuration.Version}");
        }

        ConfigManager = new ConfigManager(PluginInterface, Log);

        FrenTracker = new FrenTracker(this);
        ZoneService = new ZoneService();
        FrenTeleportService = new FrenTeleportService(this, FrenTracker, ZoneService);
        AdsDutyIpcService = new AdsDutyIpcService(PluginInterface, Log);
        AdsIntegrationService = new AdsIntegrationService(this, ZoneService, AdsDutyIpcService);
        AdsUtilityIpcService = new AdsUtilityIpcService(PluginInterface, Log);
        AdsReflectionIpcService = new AdsReflectionIpcService(this, PluginInterface, Log);
        AutorotIpcService = new AutorotIpcService(PluginInterface, Log);
        DaedalusTargetModeService = new DaedalusTargetModeService(PluginInterface, TargetManager, ToastGui, Log);
        BossModConflictWarningService = new BossModConflictWarningService(PluginInterface, ToastGui, Log);
        BossModActionTweaksService = new BossModActionTweaksService(PluginInterface, Log, AutorotIpcService);
        BossModActionTweaksService.ApplyDontMoveWhileCasting(Configuration.DontMoveWhileCasting);
        var externalAutomationCommandSender = new DalamudExternalAutomationCommandSender();
        var daedalusAutomationController = new AutorotDaedalusAutomationController(AutorotIpcService);
        ExternalAutomationCleanupService = new ExternalAutomationCleanupService(
            externalAutomationCommandSender,
            new BossModExternalAutomationSnapshotProvider(PluginInterface, Log),
            message => Log.Information(message),
            message => Log.Warning(message),
            new AutorotRsrCleanupController(AutorotIpcService, externalAutomationCommandSender),
            daedalusAutomationController);
        CoppeliaPowerlevelLeaseService = new CoppeliaPowerlevelLeaseService(this);
        FollowService = new FollowService(this, FrenTracker, ZoneService);
        MountService = new MountService(this, FrenTracker, ZoneService);
        QuestionableIpcService = new QuestionableIpcService(PluginInterface, Log);
        CombatService = new CombatService(this, FrenTracker, ZoneService, QuestionableIpcService);
        AdsHyperFocusLeaseService = new AdsHyperFocusLeaseService(this);
        AutomationService = new AutomationService(this, FrenTracker, ZoneService);
        FormationService = new FormationService(this, FrenTracker);
        PartyService = new PartyService(this, Log, GameGui);
        PartyService.Initialize();
        VideoPlaybackService = new VideoPlaybackService(Configuration, Log, ChatGui);
        DutyInteractService = new DutyInteractService(this, FrenTracker, ZoneService);
        ExitBehaviourService = new ExitBehaviourService(this, FrenTracker, ZoneService);
        FateSyncService = new FateSyncService(this, ZoneService);
        YesAlreadyIPC = new YesAlreadyIPC(Log);
        CombatOnlyIPC = new CombatOnlyIPC(PluginInterface, ConfigManager, Log);
        AutoYesService = new AutoYesService(this, Condition, Log);
        RespawnService = new RespawnService(this);
		
        // Initialize AutoDuty warning system
        AutoDutyWarningWindow = new AutoDutyWarningWindow(this, ChatGui, Log);
        AutoDutyDetectionService = new AutoDutyDetectionService(this, ChatGui, Framework, Log, AutoDutyWarningWindow);

        // Hook into FrenRider enabled state changes
        ConfigManager.OnFrenRiderEnabledChanged += OnFrenRiderEnabledChanged;
        DadIPC = new DadIPC(PluginInterface, ConfigManager, FrenTracker, Log);

        // Check for AutoDuty on plugin load if FrenRider is already enabled
        if (ConfigManager.GetActiveConfig().Enabled)
        {
            Log.Information("[FrenRider] Plugin loaded with FrenRider already enabled - checking for AutoDuty");
            OnFrenRiderEnabledChanged(true);
			//instead we stil just kill it outright.
			//commandManager?.ProcessCommand("/xldisableplugin AutoDuty");
        }

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        MagiaMiniWindow = new MagiaMiniWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(MagiaMiniWindow);
        WindowSystem.AddWindow(AutoDutyWarningWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Fren Rider main window."
        });

        CommandManager.AddHandler(AliasCommandName, new CommandInfo(OnAliasCommand)
        {
            HelpMessage = "Fren Rider: /fr [on|off|settings|s|mini|m|debug], or /fr to open the main window."
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // Load mount names from game data
        LoadMountNames();

        // DTR bar
        SetupDtrBar();

        // Login detection (deferred via framework update to avoid thread issues)
        ClientState.Login += OnLoginEvent;
        Framework.Update += OnFrameworkUpdate;

        // If already logged in at plugin load, defer detection to framework update
        if (ClientState.IsLoggedIn)
        {
            wasLoggedIn = true;
            loginDetectionDelay = 3;
        }

        var loadedVersion = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "unknown";
        Log.Information($"[FrenRider] Loaded version {loadedVersion} from {PluginInterface.AssemblyLocation.FullName}");
        Log.Information("===Fren Rider loaded!===");
    }

    public void Dispose()
    {
        FollowService.Dispose();

        Framework.Update -= OnFrameworkUpdate;
        ClientState.Login -= OnLoginEvent;

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        MagiaMiniWindow.Dispose();

        AutorotIpcService.Dispose();
        QuestionableIpcService.Dispose();
        CoppeliaPowerlevelLeaseService.Dispose();
        AdsHyperFocusLeaseService.Dispose();
        AdsDutyIpcService.Dispose();
        AdsUtilityIpcService.Dispose();
        AutomationService.Dispose();
        AdsReflectionIpcService.Dispose();
        PartyService.Dispose();
        VideoPlaybackService.Dispose();
        ExitBehaviourService.Dispose();
        YesAlreadyIPC.Dispose();
        CombatOnlyIPC.Dispose();
        DadIPC.Dispose();
        AutoYesService.Dispose();
        AutoDutyDetectionService.Dispose();

        dtrEntry?.Remove();

        CommandManager.RemoveHandler(AliasCommandName);
        CommandManager.RemoveHandler(CommandName);

        if (ECommonsAvailable)
        {
            try
            {
                ECommonsMain.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FrenRider] ECommons dispose failed");
            }

            ECommonsAvailable = false;
        }
    }

    private void OnFrenRiderEnabledChanged(bool enabled)
    {
        Log.Information($"[FrenRider] FrenRider enabled state changed to: {enabled}");
        BossModConflictWarningService.Update(enabled);
        
        if (enabled)
        {
            Log.Information("[FrenRider] Refreshing packaged BossMod presets on enable");
            AutorotIpcService.CreatePresets(force: true);

           // Trigger AutoDuty check when FrenRider is enabled
            Log.Information("[FrenRider] Triggering AutoDuty detection check");
            
            // Force an immediate detection check first
            AutoDutyDetectionService.ForceCheck();
            Log.Information($"[FrenRider] AutoDuty detected after enable check: {AutoDutyDetectionService.IsAutoDutyDetected()}");
			//instead we stil just kill it outright.
			//commandManager?.ProcessCommand("/xldisableplugin AutoDuty");
			//commandManager?.ProcessCommand("/echo hi");

            if (CombatService.PrepareForEnableCombatSetup())
            {
                CaptureExternalAutomationSnapshot("FrenRider enabled");

                Log.Information("[FrenRider] Applying one-time BossMod follow defaults on enable");
                CombatService.ApplyBossModFollowStartupDefaults();

                Log.Information("[FrenRider] Applying current BossMod preset selection");
                CombatService.ApplyPresetSelection("FrenRider enabled", installPresets: false);
            }
            else
            {
                Log.Information("[FrenRider][DutyAuthority] Skipped enable-time combat setup under QuestionableSolo authority.");
            }
        }
        else
        {
            CoppeliaPowerlevelLeaseService.HandleManualFrenRiderDisable();
            AdsHyperFocusLeaseService.HandleManualFrenRiderDisable();
            FollowService.CancelFlyingStuckRecovery("disabled");
            FollowService.PreemptFarChase("disabled");
            MountService.PreemptFarChase("disabled");
            RespawnService.ResetForDisable();
            AutoDutyDetectionService.HandleFrenRiderDisabled();
            ExternalAutomationCleanupService.Cleanup(
                ConfigManager.GetActiveConfig(),
                GetCleanupAccountId(),
                GetCleanupCharacterKey(),
                "FrenRider disabled");
            CombatService.ClearExternalAutomationRuntimeState("FrenRider disabled cleanup");
        }
    }

    internal void CaptureExternalAutomationSnapshot(string reason)
    {
        ExternalAutomationCleanupService.CaptureIfMissing(GetCleanupAccountId(), GetCleanupCharacterKey(), reason);
    }

    internal void MarkWrathAutoStartedByFrenRider(string reason)
    {
        ExternalAutomationCleanupService.MarkWrathAutoStarted(GetCleanupAccountId(), GetCleanupCharacterKey(), reason);
    }

    private string GetCleanupAccountId()
        => string.IsNullOrWhiteSpace(ConfigManager.CurrentAccountId) ? "unknown-account" : ConfigManager.CurrentAccountId;

    private string GetCleanupCharacterKey()
        => string.IsNullOrWhiteSpace(ConfigManager.ActiveCharacterKey) ? "inactive-character" : ConfigManager.ActiveCharacterKey;

    private void OnCommand(string command, string args)
    {
        MainWindow.Toggle();
    }

    private void OnAliasCommand(string command, string args)
    {
        var arg = args.Trim().ToLowerInvariant();
        if (arg == "on" || arg == "off")
        {
            ConfigManager.SetFrenRiderEnabled(arg == "on");
            Log.Information($"Fren Rider {(arg == "on" ? "enabled" : "disabled")} via /fr {arg}");
        }
        else if (arg == "settings" || arg == "s")
        {
            ConfigWindow.Toggle();
        }
        else if (arg == "mini" || arg == "m")
        {
            MagiaMiniWindow.Toggle();
        }
        else if (arg == "debug")
        {
            var config = ConfigManager.GetActiveConfig();
            config.DebugMode = !config.DebugMode;
            ConfigManager.SaveCurrentAccount();
            ReportToChatAndLog($"FrenRider debug controls: {(config.DebugMode ? "ON" : "OFF")}");
        }
        else if (arg == "testvideo")
        {
            Log.Information("[FrenRider] Testing video availability...");
            var available = VideoPlaybackService.CheckVideoAvailability();
            Log.Information($"[FrenRider] Videos available: {available}");
            
            var enablePath = VideoPlaybackService.GetEmbeddedVideoPath("1.mp4");
            var disablePath = VideoPlaybackService.GetEmbeddedVideoPath("2.mp4");
            Log.Information($"[FrenRider] Enable video path: {enablePath}");
            Log.Information($"[FrenRider] Disable video path: {disablePath}");
            
            if (!string.IsNullOrEmpty(enablePath))
            {
                Log.Information("[FrenRider] Playing test enable video...");
                _ = VideoPlaybackService.PlayVideo(enablePath);
            }
        }
        else if (arg == "testautoduty")
        {
            Log.Information("[FrenRider] Testing AutoDuty detection...");
            var isDetected = AutoDutyDetectionService.IsAutoDutyDetected();
            Log.Information($"[FrenRider] AutoDuty detected: {isDetected}");
            
            if (isDetected)
            {
                Log.Information("[FrenRider] AutoDuty detected - showing warning window");
                AutoDutyDetectionService.ForceShowWarning();
            }
            else
            {
                Log.Information("[FrenRider] AutoDuty not detected - cannot show warning window");
            }
        }
        else if (arg == "resetautoduty")
        {
            Log.Information("[FrenRider] Resetting AutoDuty detection state");
            AutoDutyDetectionService.ResetWarning();
            Log.Information("[FrenRider] AutoDuty detection state reset");
        }
        else
        {
            MainWindow.Toggle();
        }
    }

    private void OnLoginEvent()
    {
        // Don't run OnLogin here - Login event fires off main thread.
        // Instead, set a delay so OnFrameworkUpdate picks it up.
        loginDetectionDelay = 3;
    }

    private void OnLogin()
    {
        try
        {
            var charName = ObjectTable.LocalPlayer?.Name.ToString() ?? "";
            var worldName = ObjectTable.LocalPlayer?.HomeWorld.Value.Name.ToString() ?? "";
            if (!string.IsNullOrEmpty(charName) && !string.IsNullOrEmpty(worldName))
            {
                var characterKey = $"{charName}@{worldName}";
                var contentId = PlayerState.ContentId;
                Log.Information($"OnLogin: Character={characterKey}, ContentId={contentId:X16}");
                if (ConfigManager.TryReadLauncherAccountId(out var launcherAccountId))
                {
                    ConfigManager.EnsureAccountSelected(launcherAccountId, characterKey, charName);
                }
                else
                {
                    ConfigManager.EnsureAccountSelected(null, characterKey, charName);
                }

                ConfigManager.EnsureCharacterExists(charName, worldName);

                if (!string.IsNullOrWhiteSpace(ConfigManager.ActiveCharacterKey))
                {
                    Configuration.LastAccountId = ConfigManager.CurrentAccountId;
                    Configuration.Save();
                    Log.Information($"Character detected: {ConfigManager.ActiveCharacterKey} -> Account {ConfigManager.CurrentAccountId}");
                }
                else
                {
                    Log.Warning($"OnLogin: No active profile resolved for {charName}@{worldName}");
                }
            }
            else
            {
                ConfigManager.ClearActiveCharacter();
                Log.Warning($"OnLogin: Missing data - charName={charName}, worldName={worldName}");
            }
        }
        catch (Exception ex)
        {
            ConfigManager.ClearActiveCharacter();
            Log.Error($"Error during login detection: {ex.Message}");
        }
    }

    private void OnFrameworkUpdate(IFramework fw)
    {
        var updateStopwatch = Stopwatch.StartNew();
        var slowestSection = "none";
        var slowestMs = 0d;

        void Measure(string section, Action action)
        {
            var sectionStopwatch = Stopwatch.StartNew();
            action();
            sectionStopwatch.Stop();

            var elapsedMs = sectionStopwatch.Elapsed.TotalMilliseconds;
            if (elapsedMs > slowestMs)
            {
                slowestMs = elapsedMs;
                slowestSection = section;
            }
        }

        try
        {
            Measure("dtr", UpdateDtrBar);
            Measure("zone", ZoneService.Update);
            Measure("coppelia-powerlevel-lease", CoppeliaPowerlevelLeaseService.Update);
            Measure("ads-hyper-focus-lease", AdsHyperFocusLeaseService.Update);

            // Detect logout before any transition early-return so stale active state
            // cannot survive while the client is between areas.
            if (ClientState.IsLoggedIn && !wasLoggedIn)
            {
                wasLoggedIn = true;
                loginDetectionDelay = 3; // Wait a few frames for LocalPlayer to be ready
            }
            else if (!ClientState.IsLoggedIn && wasLoggedIn)
            {
                wasLoggedIn = false;
                loginDetectionDelay = 0;
                ConfigManager.ClearActiveCharacter();
            }

            if (IsAreaTransitionActive())
            {
                FrenTeleportService.ResetForAreaTransition();
                FollowService.ResetForAreaTransition();
                MountService.PreemptFarChase("area transition");
                RespawnService.ResetForAreaTransition();
                return;
            }

            if (loginDetectionDelay > 0)
            {
                loginDetectionDelay--;
                if (loginDetectionDelay == 0)
                    Measure("login", OnLogin);
            }

            // Update fren tracking
            Measure("fren-tracker", FrenTracker.Update);
            Measure("coppelia-powerlevel-lease", CoppeliaPowerlevelLeaseService.Update);
            Measure("ads-hyper-focus-lease", AdsHyperFocusLeaseService.Update);

            // Check for plugin enable/disable state changes
            var config = ConfigManager.GetActiveConfig();
            Measure("bossmod-conflict-warning", () => BossModConflictWarningService.Update(config?.Enabled == true));
            if (config != null)
            {
                Measure("config-state", () =>
                {
                    // YesAlready pause/unpause on enable/disable
                    if (config.Enabled && !YesAlreadyIPC.IsPaused)
                    {
                        YesAlreadyIPC.Pause();
                    }
                    else if (!config.Enabled && YesAlreadyIPC.IsPaused)
                    {
                        YesAlreadyIPC.Unpause();
                    }

                    if (Configuration.VideoNotificationsEnabled && config.Enabled != wasPluginEnabled)
                    {
                        Log.Debug($"[FrenRider] Video notifications enabled, state changed: {wasPluginEnabled} -> {config.Enabled}");

                        if (config.Enabled)
                        {
                            // Plugin was just enabled - play enable video
                            var enableVideoPath = VideoPlaybackService.GetEmbeddedVideoPath("1.mp4");
                            Log.Debug($"[FrenRider] Enable video path: {enableVideoPath}");
                            if (!string.IsNullOrEmpty(enableVideoPath))
                            {
                                Log.Information("[FrenRider] Playing enable video...");
                                _ = VideoPlaybackService.PlayVideo(enableVideoPath);
                            }
                            else
                            {
                                Log.Warning("[FrenRider] Enable video not found");
                            }
                        }
                        else
                        {
                            // Plugin was just disabled - play disable video
                            var disableVideoPath = VideoPlaybackService.GetEmbeddedVideoPath("2.mp4");
                            Log.Debug($"[FrenRider] Disable video path: {disableVideoPath}");
                            if (!string.IsNullOrEmpty(disableVideoPath))
                            {
                                Log.Information("[FrenRider] Playing disable video...");
                                _ = VideoPlaybackService.PlayVideo(disableVideoPath);
                            }
                            else
                            {
                                Log.Warning("[FrenRider] Disable video not found");
                            }
                        }
                        wasPluginEnabled = config.Enabled;
                    }
                    else if (!Configuration.VideoNotificationsEnabled)
                    {
                        // Reset tracking when video notifications are disabled (only when it changes from enabled to disabled)
                        if (wasPluginEnabled != config.Enabled)
                        {
                            wasPluginEnabled = config.Enabled;
                            Log.Debug("[FrenRider] Video notifications disabled, resetting tracking");
                        }
                    }
                });
            }

            // Resolve ADS/utility state, then establish combat authority before
            // ADS pauses FrenRider's movement and other duty systems.
            Measure("ads-integration", AdsIntegrationService.Update);
            Measure("ads-reflection", () => AdsReflectionIpcService.Update());
            Measure("utility-gate", AutomationService.UpdateUtilityGate);
            Measure("combat", CombatService.Update);

            Measure("fren-teleport", FrenTeleportService.Update);
            Measure("auto-yes", AutoYesService.Update);
            Measure("respawn", RespawnService.Update);
            Measure("fate-sync", FateSyncService.Update);
            Measure("follow", FollowService.Update);
            Measure("mount", MountService.Update);
            Measure("automation", AutomationService.Update);
            Measure("formation", FormationService.Update);
            Measure("party", PartyService.Update);
            Measure("duty-interact", DutyInteractService.Update);
            Measure("exit", ExitBehaviourService.Update);
        }
        finally
        {
            updateStopwatch.Stop();
            ReportFrameworkHitch(updateStopwatch.Elapsed.TotalMilliseconds, slowestSection, slowestMs);
        }
    }

    private static bool IsAreaTransitionActive()
        => Condition[ConditionFlag.BetweenAreas] || Condition[ConditionFlag.BetweenAreas51];

    private void ReportFrameworkHitch(double elapsedMs, string slowestSection, double slowestMs)
    {
        lastSlowUpdateMs = elapsedMs;
        lastSlowUpdateSource = slowestSection;
        if (elapsedMs < 100d)
            return;

        var now = DateTime.UtcNow;
        if (now < nextFrameworkHitchLogUtc)
            return;

        nextFrameworkHitchLogUtc = now.AddSeconds(5);
        var config = ConfigManager.GetActiveConfig();
        Log.Warning(
            "[FrenRider][HITCH] framework update slow elapsedMs={ElapsedMs:0.0}; slowSection={SlowSection}; slowSectionMs={SlowSectionMs:0.0}; transition={Transition}; enabled={Enabled}; zone={Zone}; territory={Territory}.",
            elapsedMs,
            slowestSection,
            slowestMs,
            IsAreaTransitionActive(),
            config?.Enabled ?? false,
            ZoneService.CurrentZone,
            ZoneService.TerritoryId);
    }

    public void SetupDtrBar()
    {
        try
        {
            dtrEntry = DtrBar.Get("Fren Rider");
            dtrEntry.Shown = Configuration.DtrBarEnabled;
            dtrEntry.Text = new SeString(new TextPayload("FR: Off"));
            dtrEntry.OnClick = (_) =>
            {
                var cfg = ConfigManager.GetActiveConfig();
                ConfigManager.SetFrenRiderEnabled(!cfg.Enabled);
            };
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to setup DTR bar: {ex.Message}");
        }
    }

    public void UpdateDtrBar()
    {
        if (dtrEntry == null) return;

        dtrEntry.Shown = Configuration.DtrBarEnabled;
        if (!Configuration.DtrBarEnabled) return;

        var config = ConfigManager.GetActiveConfig();

        // DTR modes: 0=text-only, 1=icon+text, 2=icon-only
        var iconEnabled = string.IsNullOrEmpty(Configuration.DtrIconEnabled) ? "\uE044" : Configuration.DtrIconEnabled;
        var iconDisabled = string.IsNullOrEmpty(Configuration.DtrIconDisabled) ? "\uE04C" : Configuration.DtrIconDisabled;
        var glyph = config.Enabled ? iconEnabled : iconDisabled;

        switch (Configuration.DtrBarMode)
        {
            case 1: // icon+text
                dtrEntry.Text = new SeString(new TextPayload($"{glyph} FR"));
                break;
            case 2: // icon-only
                dtrEntry.Text = new SeString(new TextPayload(glyph));
                break;
            default: // text-only
                var statusText = config.Enabled ? "FR: On" : "FR: Off";
                dtrEntry.Text = new SeString(new TextPayload(statusText));
                break;
        }

        dtrEntry.Tooltip = new SeString(new TextPayload(
            config.Enabled
                ? $"Fren Rider active - Following {config.FrenName}. Coppelia: {CoppeliaPowerlevelLeaseService.StatusText}. Cleanup: {ExternalAutomationCleanupService.StatusText}"
                : $"Fren Rider disabled - Click to toggle. Coppelia: {CoppeliaPowerlevelLeaseService.StatusText}. Cleanup: {ExternalAutomationCleanupService.StatusText}"));
    }

    private void LoadMountNames()
    {
        try
        {
            var names = new List<string> { "Mount Roulette" };
            var sheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Mount>();
            if (sheet != null)
            {
                foreach (var row in sheet)
                {
                    var name = row.Singular.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                        names.Add(name);
                }
            }
            names.Sort(1, names.Count - 1, StringComparer.OrdinalIgnoreCase);
            MountNames = names.ToArray();
            Log.Information($"Loaded {MountNames.Length} mount names from game data");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load mount names: {ex.Message}");
            MountNames = new[] { "Mount Roulette", "Company Chocobo" };
        }
    }

    /// <summary>
    /// Debug log that only fires when SpamPrinter is enabled in config.
    /// </summary>
    public void SpamLog(string message)
    {
        var config = ConfigManager.GetActiveConfig();
        if (config.SpamPrinter == 1)
            Log.Debug($"[SPAM] {message}");
    }

    public void ReportToChatAndLog(string message, bool isError = false)
    {
        if (isError)
        {
            ChatGui.PrintError(message);
            Log.Warning(message);
            return;
        }

        ChatGui.Print(message);
        Log.Information(message);
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
