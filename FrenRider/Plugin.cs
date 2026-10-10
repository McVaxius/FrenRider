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
using FrenRider.Models;
using FrenRider.Services;
using FrenRider.Windows;
using AethertekUI;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace FrenRider;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IUnlockState UnlockState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

    internal Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap OriginalIcon
        => TextureProvider.GetFromFile(System.IO.Path.Combine(
            PluginInterface.AssemblyLocation.DirectoryName ?? "", "icon.png")).GetWrapOrEmpty();
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
    internal BeastCaptureService BeastCaptureService { get; init; }
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
    internal PhoenixDownRecoveryService PhoenixDownRecoveryService { get; init; }
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
    private bool loggingOutCleanupDone;
    private int loginDetectionDelay;
    private bool wasPluginEnabled = false;
    private readonly ChocoboExplorationService chocoboExploration = new();
    private int chocoboRequestGeneration;
    private readonly ChocoboSkillService chocoboSkills;
    private int chocoboSkillRequestGeneration;
    private readonly ChocoboFoodService chocoboFood;
    private int chocoboFoodRequestGeneration;
    private readonly ChocoboPurchaseService chocoboPurchase;
    private int chocoboPurchaseRequestGeneration;
    private DateTime nextFrameworkHitchLogUtc = DateTime.MinValue;
    private double lastSlowUpdateMs;
    private string lastSlowUpdateSource = "none";
    private FrenRiderFonts uiFonts = null!;
    private UiText uiText = null!;
    private AethertekUI.Dalamud.MaterialTextHost? shapedText;
    private MaterialTheme uiTheme = null!;
    private MaterialOptions<string> languageOptions = null!;
    private string appliedLanguage = "";
    private uint appliedAccent;
    private Vector3 accentDraft;
    private int checkedFontGeneration = -1;
    private int checkedHindiGeneration = -1;
    private bool fontIssueLogged;
    private readonly MaterialWindowFold fontStatusFold = new();
    private readonly MaterialWindowDecorations fontStatusDecorations = new();
    private readonly MaterialWindowOpacity fontStatusOpacity = new();
    private readonly Action<ImGuiWindowPtr> prepareFontStatusDecorations;
//	private readonly ICommandManager commandManager;

    public Plugin()
    {
        prepareFontStatusDecorations = fontStatusDecorations.Prepare;
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
        chocoboExploration.InitializeReload(Configuration.ChocoboProbeAfterReload,
            ClientState.IsLoggedIn ? PlayerState.ContentId : 0);

        FrenTracker = new FrenTracker(this);
        ZoneService = new ZoneService();
        FrenTeleportService = new FrenTeleportService(this, FrenTracker, ZoneService);
        AdsDutyIpcService = new AdsDutyIpcService(PluginInterface, Log);
        AdsIntegrationService = new AdsIntegrationService(this, AdsDutyIpcService);
        AdsUtilityIpcService = new AdsUtilityIpcService(PluginInterface, Log);
        AdsReflectionIpcService = new AdsReflectionIpcService(this, PluginInterface, Log);
        AutorotIpcService = new AutorotIpcService(PluginInterface, Log);
        DaedalusTargetModeService = new DaedalusTargetModeService(PluginInterface, TargetManager, ToastGui, Log);
        BossModConflictWarningService = new BossModConflictWarningService(PluginInterface, ToastGui, Log);
        BossModActionTweaksService = new BossModActionTweaksService(this);
        BossModActionTweaksService.ApplyDontMoveWhileCasting(Configuration.DontMoveWhileCasting);
        var externalAutomationCommandSender = new DalamudExternalAutomationCommandSender();
        var daedalusAutomationController = new AutorotDaedalusAutomationController(AutorotIpcService);
        ExternalAutomationCleanupService = new ExternalAutomationCleanupService(
            externalAutomationCommandSender,
            new BossModExternalAutomationSnapshotProvider(PluginInterface, Log),
            message => Log.Information(message),
            message => Log.Warning(message),
            new AutorotRsrCleanupController(AutorotIpcService, externalAutomationCommandSender),
            daedalusAutomationController,
            AutorotIpcService);
        CoppeliaPowerlevelLeaseService = new CoppeliaPowerlevelLeaseService(this);
        FollowService = new FollowService(this, FrenTracker, ZoneService);
        MountService = new MountService(this, FrenTracker, ZoneService);
        QuestionableIpcService = new QuestionableIpcService(PluginInterface, Log);
        CombatService = new CombatService(this, FrenTracker, ZoneService, QuestionableIpcService);
        BeastCaptureService = new BeastCaptureService(this);
        AdsHyperFocusLeaseService = new AdsHyperFocusLeaseService(this);
        AutomationService = new AutomationService(this, FrenTracker, ZoneService);
        CompanionSnapshot? ownedSkillBefore = null;
        ChocoboSkillStep? ownedSkillStep = null;
        chocoboSkills = new ChocoboSkillService(CanAllocateChocoboSkills,
            () => PlayerState.ContentId,
            () => ConfigManager.TryGetActiveConfig(out var active) ? active : null,
            () => GameHelpers.TryReadCompanion(out var info) ? info : null,
            (before, step) =>
            {
                var dispatched = GameHelpers.TryLearnCompanionSkill(before, step);
                if (dispatched) { ownedSkillBefore = before; ownedSkillStep = step; }
                return dispatched;
            },
            message => Log.Information($"[FrenRider][ChocoboSkills] {message}"),
            () => Environment.TickCount64,
            prepare: () =>
            {
                ownedSkillBefore = null;
                ownedSkillStep = null;
                return chocoboExploration.PrepareSkills();
            },
            prepared: () => chocoboExploration.SkillsReady,
            release: () =>
            {
                try
                {
                    if (ownedSkillBefore.HasValue && ownedSkillStep.HasValue
                        && chocoboExploration.OwnsReadySkillsWindow && CanAllocateChocoboSkills())
                        GameHelpers.RespondCompanionSkillConfirmation(ownedSkillBefore.Value, ownedSkillStep.Value, accept: false);
                }
                finally
                {
                    ownedSkillBefore = null;
                    ownedSkillStep = null;
                    chocoboExploration.Stop("skill allocation released its Skills window");
                }
            },
            confirm: (before, step) => GameHelpers.RespondCompanionSkillConfirmation(before, step));
        chocoboFood = new ChocoboFoodService(CanFeedChocobo,
            () => PlayerState.ContentId,
            () => ConfigManager.TryGetActiveConfig(out var active) ? active : null,
            item => GameHelpers.TryReadCompanionFood(item, out var state) ? state : null,
            GameHelpers.TryFeedCompanion,
            message => Log.Information($"[FrenRider][ChocoboFood] {message}"),
            () => Environment.TickCount64);
        chocoboPurchase = new ChocoboPurchaseService(CanPurchaseChocobo,
            () => PlayerState.ContentId,
            () => ConfigManager.TryGetActiveConfig(out var active) ? active : null,
            GameHelpers.GetCompanionSupplyStock, GameHelpers.GetInventoryGil,
            request =>
            {
                // Capability inspection is input-free. Missing/older ADS rejects before any local hold cleanup.
                try
                {
                    if (!ChocoboPurchaseService.SupportsTravelPolicy(PluginInterface
                        .GetIpcSubscriber<string>("ADS.GetCapabilitiesJson").InvokeFunc())) return false;
                }
                catch { return false; }
                FollowService.SuspendForTravel();
                MountService.PreemptFarChase("ADS companion purchase");
                return PluginInterface.GetIpcSubscriber<string, bool>("ADS.StartCurrencyShopPurchase")
                    .InvokeFunc(request.ToJson());
            },
            () => CompanionAdsPurchaseStatus.Parse(PluginInterface.GetIpcSubscriber<string>("ADS.GetShopPurchaseStatusJson").InvokeFunc()),
            operationId => PluginInterface.GetIpcSubscriber<string, bool>("ADS.CancelShopPurchase").InvokeFunc(operationId),
            message => Log.Information($"[FrenRider][ChocoboPurchase] {message}"),
            () => Environment.TickCount64);
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
        PhoenixDownRecoveryService = new PhoenixDownRecoveryService(new NativePhoenixRecoveryRuntime(this));
		
        // Initialize AutoDuty warning system
        AutoDutyWarningWindow = new AutoDutyWarningWindow(this, ChatGui, Log);
        AutoDutyDetectionService = new AutoDutyDetectionService(this, ChatGui, Framework, Log, AutoDutyWarningWindow);

        // Hook into FrenRider enabled state changes
        ConfigManager.OnEffectiveProfileChanging += EndCombatSettingsSession;
        ConfigManager.OnEffectiveProfileChanging += CancelChocoboSkillSession;
        ConfigManager.OnEffectiveProfileChanging += CancelChocoboFoodSession;
        ConfigManager.OnEffectiveProfileChanging += CancelChocoboPurchaseSession;
        ConfigManager.OnFrenRiderEnabledChanged += OnFrenRiderEnabledChanged;
        DadIPC = new DadIPC(PluginInterface, ConfigManager, FrenTracker, CombatService, Log);

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
            HelpMessage = "Fren Rider: /fr [on|off|settings|s|mini|m|debug|testchocobo]; testchocobo opens reload-test controls; testchocobo on/off selects reload testing; testchocobo run/stop runs or stops discovery."
        });

        ApplyAppearance();
        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // Load mount names from game data
        LoadMountNames();

        // DTR bar
        SetupDtrBar();

        // Login detection (deferred via framework update to avoid thread issues)
        ClientState.Login += OnLoginEvent;
        ToastGui.ErrorToast += BossModActionTweaksService.OnErrorToast;
        Framework.Update += OnFrameworkUpdate;
        DutyState.DutyCompleted += OnDutyCompleted;

        // If already logged in at plugin load, defer detection to framework update
        if (ClientState.IsLoggedIn)
        {
            wasLoggedIn = true;
            loginDetectionDelay = 3;
        }

        var loadedVersion = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "unknown";
        Log.Information($"[FrenRider] Loaded version {loadedVersion} from {PluginInterface.AssemblyLocation.FullName}");
        Log.Information("===Fren Rider loaded!===");
        Log.Information("[FrenRider][UI] build=devhub-I503-I510-I512-20261009-03; companion food effect tooltips; separate XA Slave log UI shortcut; tight compact list rows");
        Log.Information($"[FrenRider][ChocoboProbe] available build={ChocoboExplorationService.BuildMarker}; command=/fr testchocobo");
        Log.Information("[FrenRider][ChocoboPurchase] available build=I496-food-03; manual=true; main-mini-stock-controls=true; gil-cap-setting=false; travel-checkbox=false");
    }

    public void Dispose()
    {
        System.Threading.Interlocked.Increment(ref chocoboRequestGeneration);
        chocoboExploration.Dispose();
        CancelChocoboSkillSession();
        CancelChocoboFoodSession();
        CancelChocoboPurchaseSession();
        EndCombatSettingsSession();
        ConfigManager.OnEffectiveProfileChanging -= EndCombatSettingsSession;
        ConfigManager.OnEffectiveProfileChanging -= CancelChocoboSkillSession;
        ConfigManager.OnEffectiveProfileChanging -= CancelChocoboFoodSession;
        ConfigManager.OnEffectiveProfileChanging -= CancelChocoboPurchaseSession;
        PhoenixDownRecoveryService.Dispose();
        ToastGui.ErrorToast -= BossModActionTweaksService.OnErrorToast;
        BossModActionTweaksService.ResetRecovery();
        FollowService.Dispose();
        MountService.ResetFateClingHold();

        Framework.Update -= OnFrameworkUpdate;
        DutyState.DutyCompleted -= OnDutyCompleted;
        ClientState.Login -= OnLoginEvent;

        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        MagiaMiniWindow.Dispose();
        uiFonts.Dispose();
        uiText.Dispose();
        shapedText?.Dispose();

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

    private void DrawUi()
    {
        ApplyAppearance();
        if (!MainWindow.IsOpen && !ConfigWindow.IsOpen && !MagiaMiniWindow.IsOpen && !AutoDutyWarningWindow.IsOpen)
            return;
        using var text = uiText.Enter();
        shapedText ??= new(TextureProvider);
        using var shaped = shapedText.Push();
        if (!uiFonts.Ready)
        {
            if (!fontIssueLogged && uiFonts.LoadException is { } error)
            {
                Log.Error(error, "[FrenRider] Required UI fonts failed to load.");
                fontIssueLogged = true;
            }
            DrawFontStatus(uiFonts.LoadException is null);
            return;
        }
        if (checkedHindiGeneration != uiFonts.Generation)
        {
            var generation = uiFonts.Generation;
            var hindiAvailable = true;
            foreach (var height in FrenRiderPresentation.FontSizes)
                hindiAvailable &= shapedText.Renderer.TryCheckGlyphs(["हिन्दी"], height * ImGuiHelpers.GlobalScale, out _);
            languageOptions.Replace(UiText.Languages.Select(l => new MaterialOption<string>(l.Code, l.Code,
                l.Code == "hi" && !hindiAvailable ? "Hindi (unavailable)" : l.Name, l.Code == "hi" && !hindiAvailable)).ToArray());
            checkedHindiGeneration = generation;
        }
        if (checkedFontGeneration != uiFonts.Generation)
        {
            try
            {
                var generation = uiFonts.Generation;
                foreach (var height in FrenRiderPresentation.FontSizes)
                    shapedText.Renderer.CheckGlyphs(uiText.RequiredText, height * ImGuiHelpers.GlobalScale);
                uiFonts.CheckGlyphs(uiText.RequiredText);
                checkedFontGeneration = generation;
            }
            catch (Exception ex)
            {
                if (!fontIssueLogged) { Log.Error(ex, "[FrenRider] Required UI glyph coverage failed."); fontIssueLogged = true; }
                DrawFontStatus(false);
                return;
            }
        }
        using var theme = MaterialTheme.Push(uiTheme, ImGuiHelpers.GlobalScale, MaterialStyleMode.ColorsOnly);
        using var geometry = new MaterialStyleScope();
        var compact = Configuration.UiCompact;
        var scale = ImGuiHelpers.GlobalScale;
        geometry.Style(ImGuiStyleVar.WindowPadding, new Vector2(compact ? 12 : 16) * scale);
        geometry.Style(ImGuiStyleVar.ItemSpacing, new Vector2(compact ? 8 : 12, compact ? 6 : 10) * scale);
        geometry.Style(ImGuiStyleVar.FramePadding, new Vector2(compact ? 8 : 12, compact ? 4 : 7) * scale);
        geometry.Style(ImGuiStyleVar.CellPadding, new Vector2(compact ? 8 : 12, compact ? 5 : 8) * scale);
        geometry.Style(ImGuiStyleVar.FrameRounding, 4 * scale);
        geometry.Style(ImGuiStyleVar.ChildRounding, 4 * scale);
        using var body = uiFonts.Push(UiFontRole.Body);
        using var chrome = MaterialWindowChrome.Push();
        WindowSystem.Draw();
    }

    private void DrawFontStatus(bool loading)
    {
        using var theme = MaterialTheme.Push(uiTheme, ImGuiHelpers.GlobalScale, MaterialStyleMode.ColorsOnly);
        using var chrome = MaterialWindowChrome.Push();
        ImGui.SetNextWindowSize(new Vector2(460 * ImGuiHelpers.GlobalScale, 0));
        fontStatusFold.PreDraw("Fren Rider##FontStatus", null, null, false, prepareFontStatusDecorations);
        var visible = ImGui.Begin("Fren Rider##FontStatus", ImGuiWindowFlags.AlwaysAutoResize);
        try
        {
            if (visible)
            {
                fontStatusDecorations.Paint();
                if (appliedLanguage == "hi")
                {
                    ImGui.TextWrapped(loading ? "Loading Hindi UI fonts..." : "Hindi UI fonts are unavailable. See the plugin log.");
                    if (!loading && ImGui.Button("Use English")) { Configuration.UiLanguage = "en"; Configuration.Save(); }
                }
                else MaterialText.TextWrapped(UiText.T(loading ? "Loading UI fonts..." : "UI fonts failed to load. See the plugin log."));
            }
        }
        finally
        {
            ImGui.End();
            fontStatusDecorations.Paint();
            fontStatusFold.PostDraw();
            ApplyWindowOpacity(fontStatusOpacity, "Fren Rider##FontStatus");
        }
    }

    private void ApplyAppearance()
    {
        var language = UiText.Languages.Any(l => l.Code == Configuration.UiLanguage) ? Configuration.UiLanguage : "en";
        if (language != appliedLanguage)
        {
            uiFonts?.Dispose();
            uiText?.Dispose();
            uiText = new(language, role => uiFonts!.Push(role));
            uiFonts = new(PluginInterface.UiBuilder.FontAtlas, uiText.GlyphRanges(), language);
            languageOptions = new(UiText.Languages.Select(l => new MaterialOption<string>(l.Code, l.Code, l.Name)).ToArray());
            appliedLanguage = language;
            checkedFontGeneration = -1;
            checkedHindiGeneration = -1;
            fontIssueLogged = false;
        }
        if (uiTheme is null || (Configuration.UiAccentRgb & 0xFFFFFF) != appliedAccent)
        {
            appliedAccent = Configuration.UiAccentRgb & 0xFFFFFF;
            uiTheme = FrenRiderPresentation.Theme(appliedAccent);
            var color = FrenRiderPresentation.Rgb(appliedAccent);
            accentDraft = new(color.X, color.Y, color.Z);
        }
    }

    internal void DrawAppearanceSelector()
    {
        DrawAccentSelector();
        ImGui.SameLine();
        DrawLanguageSelector();
    }

    internal void DrawAccentSelector()
    {
        using var controls = MaterialControls.Push(FrenRiderPresentation.Controls(Configuration.UiCompact ? 28 : 32, 18));
        if (!MaterialAppearanceSelector.DrawAccent("appearance", ref accentDraft,
            new(UiText.T("Color"), UiText.T("Language"), UiText.T("Teal"), UiText.T("Blue"), UiText.T("Pink"), UiText.T("Custom RGB")))) return;
        Configuration.UiAccentRgb = ((uint)Math.Clamp((int)MathF.Round(accentDraft.X * 255), 0, 255) << 16)
            | ((uint)Math.Clamp((int)MathF.Round(accentDraft.Y * 255), 0, 255) << 8) | (uint)Math.Clamp((int)MathF.Round(accentDraft.Z * 255), 0, 255);
        Configuration.Save();
    }

    internal void DrawLanguageSelector()
    {
        var language = appliedLanguage;
        using var controls = MaterialControls.Push(FrenRiderPresentation.Controls(Configuration.UiCompact ? 28 : 32, 18));
        if (!MaterialAppearanceSelector.DrawLanguage("appearance", ref language, languageOptions, languageWidth: 140)) return;
        Configuration.UiLanguage = language;
        Configuration.Save();
    }

    internal void DrawCompactSelector()
    {
        var compact = Configuration.UiCompact;
        if (ImGui.Checkbox("C", ref compact)) { Configuration.UiCompact = compact; Configuration.Save(); }
        if (ImGui.IsItemHovered()) MaterialText.SetTooltip(UiText.T("Compact mode"));
    }

    private void OnFrenRiderEnabledChanged(bool enabled)
    {
        Log.Information($"[FrenRider] FrenRider enabled state changed to: {enabled}");
        BossModConflictWarningService.Update(enabled);
        AdsIntegrationService.ResetHandoff();
        
        if (enabled)
        {
            AdsIntegrationService.Update(forceOwnershipRefresh: true);
            var allowCombatSetup = CombatService.PrepareForEnableCombatSetup();
            if (allowCombatSetup)
            {
                CaptureExternalAutomationSnapshot("FrenRider enable preset preparation");
                Log.Information("[FrenRider] Refreshing packaged BossMod presets on enable");
                AutorotIpcService.CreatePresets(force: true,
                    rotationProvider: CombatService.GetConfiguredRotationProvider(ConfigManager.GetActiveConfig()));
            }

           // Trigger AutoDuty check when FrenRider is enabled
            Log.Information("[FrenRider] Triggering AutoDuty detection check");
            
            // Force an immediate detection check first
            AutoDutyDetectionService.ForceCheck();
            Log.Information($"[FrenRider] AutoDuty detected after enable check: {AutoDutyDetectionService.IsAutoDutyDetected()}");
			//instead we stil just kill it outright.
			//commandManager?.ProcessCommand("/xldisableplugin AutoDuty");
			//commandManager?.ProcessCommand("/echo hi");

            if (allowCombatSetup)
            {
                CaptureExternalAutomationSnapshot("FrenRider enabled");

                Log.Information("[FrenRider] Applying one-time BossMod follow defaults on enable");
                CombatService.ApplyBossModFollowStartupDefaults();

                Log.Information("[FrenRider] Applying current BossMod preset selection");
                CombatService.ApplyPresetSelection("FrenRider enabled", installPresets: false);
            }
            else
            {
                Log.Information($"[FrenRider][DutyAuthority] Skipped enable-time combat setup: {CombatService.StateDetail}.");
            }

            CommandManager.ProcessCommand(
                ConfigManager.GetActiveConfig().ObstacleMapsOn
                    ? "/bmrai obstaclemaps on"
                    : "/bmrai obstaclemaps off");
        }
        else
        {
            BossModActionTweaksService.ResetRecovery();
            CoppeliaPowerlevelLeaseService.HandleManualFrenRiderDisable();
            AdsHyperFocusLeaseService.HandleManualFrenRiderDisable();
            FollowService.CancelFlyingStuckRecovery("disabled");
            FollowService.PreemptFarChase("disabled");
            MountService.PreemptFarChase("disabled");
            MountService.ResetFateClingHold();
            RespawnService.ResetForDisable();
            PhoenixDownRecoveryService.Reset();
            AutoDutyDetectionService.HandleFrenRiderDisabled();
            CombatService.ReleaseQuestingRsrAggroForDeparture("FrenRider disabled");
            CombatService.ReleaseDungeonRsrAggroForDeparture("FrenRider disabled");
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
        var config = ConfigManager.GetActiveConfig();
        if (config.Enabled)
            AutorotIpcService.PrepareOwnedBossModSettings(CombatService.GetConfiguredRotationProvider(config),
                GetCleanupAccountId(), GetCleanupCharacterKey(), config);
        ExternalAutomationCleanupService.CaptureIfMissing(GetCleanupAccountId(), GetCleanupCharacterKey(), reason);
    }

    private void EndCombatSettingsSession()
    {
        var config = ConfigManager.GetActiveConfig();
        CombatService.ReleaseQuestingRsrAggroForDeparture("combat settings session departure");
        CombatService.ReleaseDungeonRsrAggroForDeparture("combat settings session departure");
        if (ExternalAutomationCleanupService.TryGetSnapshot(GetCleanupAccountId(), GetCleanupCharacterKey(), out _))
            ExternalAutomationCleanupService.Cleanup(config, GetCleanupAccountId(), GetCleanupCharacterKey(), "combat settings session departure");
        else
            AutorotIpcService.ReleaseOwnedBossModSettings(config.CleanupMode == FrenRiderCleanupMode.TurnEverythingOff);
        CombatService.ClearExternalAutomationRuntimeState("combat settings session departure");
    }

    private void OnDutyCompleted(Dalamud.Game.DutyState.IDutyStateEventArgs args)
        => CombatService.ReleaseDungeonRsrAggroForDeparture("native duty completion");

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

    internal string ChocoboProbeStatus => chocoboExploration.IsSkillPreparation ? chocoboSkills.Status
        : chocoboExploration.IsActive ? "Companion discovery is running"
        : chocoboExploration.PendingReload ? "Waiting for character readiness" : "No Companion discovery is pending";

    internal string ChocoboSkillStatus => chocoboSkills.Status;
    internal bool IsChocoboSkillAllocationActive => chocoboSkills.IsActive;
    internal void OpenChocoboSettings() => ConfigWindow.OpenChocoboTesting();

    internal void AllocateChocoboSkills()
    {
        var generation = System.Threading.Volatile.Read(ref chocoboSkillRequestGeneration);
        _ = Framework.RunOnFrameworkThread(() =>
        {
            if (generation == System.Threading.Volatile.Read(ref chocoboSkillRequestGeneration))
                chocoboSkills.Start();
        });
    }

    internal void StopChocoboSkills()
    {
        System.Threading.Interlocked.Increment(ref chocoboSkillRequestGeneration);
        _ = Framework.RunOnFrameworkThread(() => chocoboSkills.Stop());
    }

    internal void CheckAutomaticChocoboSkills() => chocoboSkills.CheckAutomatic();

    internal string ChocoboFoodStatus => chocoboFood.Status;

    internal void FeedChocoboNow()
    {
        var generation = System.Threading.Volatile.Read(ref chocoboFoodRequestGeneration);
        _ = Framework.RunOnFrameworkThread(() =>
        {
            if (generation == System.Threading.Volatile.Read(ref chocoboFoodRequestGeneration))
                chocoboFood.Start();
        });
    }

    internal void StopChocoboFeeding()
    {
        System.Threading.Interlocked.Increment(ref chocoboFoodRequestGeneration);
        _ = Framework.RunOnFrameworkThread(() => chocoboFood.Stop());
    }

    internal void CheckAutomaticChocoboFood() => chocoboFood.CheckAutomatic();

    internal string ChocoboPurchaseStatus => chocoboPurchase.Status;

    internal void PurchaseChocoboGreensNow() => PurchaseChocoboStock(food: false);
    internal void PurchaseChocoboFoodNow() => PurchaseChocoboStock(food: true);

    private void PurchaseChocoboStock(bool food)
    {
        var generation = System.Threading.Volatile.Read(ref chocoboPurchaseRequestGeneration);
        _ = Framework.RunOnFrameworkThread(() =>
        {
            if (generation != System.Threading.Volatile.Read(ref chocoboPurchaseRequestGeneration)
                || !ConfigManager.TryGetActiveConfig(out var active) || active == null) return;
            chocoboPurchase.Start(food ? active.ChocoboFoodItemId : (int)GameHelpers.GysahlGreensItemId,
                food ? active.ChocoboFoodStockTarget : active.ChocoboGreensStockTarget);
        });
    }

    internal void StopChocoboPurchasing()
    {
        System.Threading.Interlocked.Increment(ref chocoboPurchaseRequestGeneration);
        _ = Framework.RunOnFrameworkThread(() => chocoboPurchase.Stop());
    }

    private void CancelChocoboPurchaseSession()
    {
        System.Threading.Interlocked.Increment(ref chocoboPurchaseRequestGeneration);
        chocoboPurchase.Stop("Companion purchase cancelled: context, profile, or shop session changed.");
    }

    private bool CanPurchaseChocobo()
        => (chocoboPurchase.HasOwnedFlow ? CanContinueOwnedChocoboPurchase() : CanUseCompanionActions(allowOwnedShop: true))
            && !Condition[ConditionFlag.RidingPillion]
            && !chocoboExploration.IsActive && !chocoboExploration.PendingReload
            && !chocoboSkills.IsActive && !chocoboFood.IsActive;

    private bool CanContinueOwnedChocoboPurchase()
        => ClientState.IsLoggedIn && PlayerState.ContentId != 0
            && !Condition[ConditionFlag.InCombat] && !Condition[ConditionFlag.LoggingOut]
            && !Condition[ConditionFlag.BoundByDuty] && !Condition[ConditionFlag.BoundByDuty56]
            && (chocoboPurchase.AllowsOwnedTravel
                || !Condition[ConditionFlag.Mounted] && !Condition[ConditionFlag.Mounting71])
            && !Condition[ConditionFlag.OccupiedInCutSceneEvent]
            && (chocoboPurchase.AllowsOwnedTravel && IsAreaTransitionActive()
                || ObjectTable.LocalPlayer is { CurrentHp: > 0 } player
                    && (!player.IsCasting || chocoboPurchase.AllowsOwnedTravel))
            && !AdsIntegrationService.ShouldPauseDutySystems && !AutomationService.IsRepairFlowActive
            && !CoppeliaPowerlevelLeaseService.ShouldSuppressCompanionAutoSummon
            && !PhoenixDownRecoveryService.HoldActions && !PhoenixDownRecoveryService.HoldMovement;

    private void CancelChocoboFoodSession()
    {
        System.Threading.Interlocked.Increment(ref chocoboFoodRequestGeneration);
        chocoboFood.Stop("Companion feeding cancelled: context or profile changed.");
    }

    private void CancelChocoboSkillSession()
    {
        System.Threading.Interlocked.Increment(ref chocoboSkillRequestGeneration);
        chocoboSkills.Stop("Skill allocation cancelled: context or profile changed.");
    }

    private bool CanAllocateChocoboSkills()
        => CanUseCompanionActions()
            && (!chocoboExploration.IsActive || chocoboExploration.IsSkillPreparation)
            && !chocoboExploration.PendingReload
            && (!chocoboSkills.IsActive || chocoboExploration.IsSkillPreparation)
            && !chocoboFood.IsActive && !chocoboPurchase.HasOwnedFlow;

    private bool CanFeedChocobo()
        => CanUseCompanionActions() && !Condition[ConditionFlag.RidingPillion]
            && !chocoboExploration.IsActive && !chocoboExploration.PendingReload && !chocoboSkills.IsActive
            && !chocoboPurchase.HasOwnedFlow;

    private bool CanUseCompanionActions(bool allowOwnedShop = false)
        => ClientState.IsLoggedIn && ObjectTable.LocalPlayer is { IsCasting: false, CurrentHp: > 0 }
            && !Condition[ConditionFlag.InCombat] && !Condition[ConditionFlag.LoggingOut]
            && !Condition[ConditionFlag.BetweenAreas] && !Condition[ConditionFlag.BetweenAreas51]
            && !Condition[ConditionFlag.BoundByDuty] && !Condition[ConditionFlag.BoundByDuty56]
            && !Condition[ConditionFlag.Mounted] && !Condition[ConditionFlag.Mounting71]
            && !Condition[ConditionFlag.OccupiedInCutSceneEvent]
            && (allowOwnedShop || (!Condition[ConditionFlag.OccupiedInQuestEvent]
                && !Condition[ConditionFlag.Occupied33] && !Condition[ConditionFlag.Occupied39]))
            && !AdsIntegrationService.ShouldPauseDutySystems && !AutomationService.IsUtilityGateActive
            && !CoppeliaPowerlevelLeaseService.ShouldSuppressCompanionAutoSummon
            && !PhoenixDownRecoveryService.HoldActions && !PhoenixDownRecoveryService.HoldMovement;

    internal void SetChocoboProbeAfterReload(bool selected)
    {
        if (Configuration.ChocoboProbeAfterReload == selected) return;
        Configuration.ChocoboProbeAfterReload = selected;
        Configuration.Save();
        if (!selected) System.Threading.Interlocked.Increment(ref chocoboRequestGeneration);
        _ = Framework.RunOnFrameworkThread(() => chocoboExploration.SelectionChanged(selected));
    }

    internal void RunChocoboProbe()
    {
        var generation = System.Threading.Volatile.Read(ref chocoboRequestGeneration);
        _ = Framework.RunOnFrameworkThread(() =>
        {
            if (generation == System.Threading.Volatile.Read(ref chocoboRequestGeneration)
                && !chocoboSkills.IsActive && !chocoboFood.IsActive && !chocoboPurchase.HasOwnedFlow)
                chocoboExploration.Start();
        });
    }

    internal void StopChocoboProbe()
    {
        System.Threading.Interlocked.Increment(ref chocoboRequestGeneration);
        _ = Framework.RunOnFrameworkThread(() => chocoboExploration.Stop("explicit stop"));
    }

    private void OnAliasCommand(string command, string args)
    {
        var arg = args.Trim().ToLowerInvariant();
        if (arg == "testchocobo")
        {
            ConfigWindow.OpenChocoboTesting();
        }
        else if (arg == "testchocobo on" || arg == "testchocobo off")
        {
            SetChocoboProbeAfterReload(arg == "testchocobo on");
            ConfigWindow.OpenChocoboTesting();
        }
        else if (arg == "testchocobo run")
        {
            RunChocoboProbe();
        }
        else if (arg == "testchocobo stop")
        {
            StopChocoboProbe();
        }
        else if (arg == "on" || arg == "off")
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
                PhoenixDownRecoveryService.Reset();
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
            Measure("dad-questionable-settings", DadIPC.UpdateQuestionableDutySettings);
            Measure("zone", ZoneService.Update);
            Measure("ads-readiness", AdsIntegrationService.ObserveHandoffReadiness);
            Measure("casting-recovery", BossModActionTweaksService.UpdateRecovery);
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
                chocoboExploration.Stop("logout", restore: false);
                CancelChocoboSkillSession();
                CancelChocoboFoodSession();
                CancelChocoboPurchaseSession();
                EndCombatSettingsSession();
                PhoenixDownRecoveryService.Reset();
                MountService.ResetFateClingHold();
                wasLoggedIn = false;
                loginDetectionDelay = 0;
                ConfigManager.ClearActiveCharacter();
            }

            if (ClientState.IsLoggedIn && Condition[ConditionFlag.LoggingOut])
            {
                chocoboExploration.Stop("logging out", restore: false);
                CancelChocoboSkillSession();
                CancelChocoboFoodSession();
                CancelChocoboPurchaseSession();
                if (!loggingOutCleanupDone)
                {
                    EndCombatSettingsSession();
                    loggingOutCleanupDone = true;
                }
                return;
            }
            loggingOutCleanupDone = false;

            if (IsAreaTransitionActive())
            {
                chocoboExploration.Stop("area transition", restore: false);
                CancelChocoboSkillSession();
                CancelChocoboFoodSession();
                if (chocoboPurchase.AllowsOwnedTravel) chocoboPurchase.Tick();
                else CancelChocoboPurchaseSession();
                PhoenixDownRecoveryService.Reset();
                BeastCaptureService.Suspend();
                BossModActionTweaksService.ResetRecovery();
                FrenTeleportService.ResetForAreaTransition();
                FollowService.ResetForAreaTransition();
                MountService.PreemptFarChase("area transition");
                MountService.ResetFateClingHold();
                RespawnService.ResetForAreaTransition();
                return;
            }

            if (loginDetectionDelay > 0)
            {
                loginDetectionDelay--;
                if (loginDetectionDelay == 0)
                    Measure("login", OnLogin);
            }

            Measure("chocobo-discovery", () =>
            {
                if (chocoboExploration.PendingReload)
                {
                    try { chocoboExploration.TickReload(!chocoboPurchase.HasOwnedFlow && ConfigManager.TryGetLocalActiveConfig(out _)); }
                    catch (Exception ex)
                    {
                        chocoboExploration.Stop($"reload registration failed: {ex.GetType().Name}", restore: false);
                    }
                }
                chocoboExploration.Tick();
            });

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
            Measure("phoenix-down-recovery", PhoenixDownRecoveryService.Update);
            Measure("chocobo-skills", chocoboSkills.Tick);
            Measure("chocobo-food", chocoboFood.Tick);
            Measure("chocobo-purchase", chocoboPurchase.Tick);
            Measure("fate-cling-hold", MountService.RefreshFateClingHold);
            if (!PhoenixDownRecoveryService.HoldActions)
                Measure("combat", CombatService.Update);
            if (!PhoenixDownRecoveryService.HoldMovement)
            {
                Measure("beast-capture", BeastCaptureService.Update);
                Measure("casting-recovery", BossModActionTweaksService.UpdateRecovery);
            }

            if (chocoboPurchase.HoldsMovement)
                return; // Hold local teleport, dialogs and movement for the owned restock operation.
            Measure("fren-teleport", FrenTeleportService.Update);
            Measure("auto-yes", AutoYesService.Update);
            Measure("respawn", RespawnService.Update);
            if (PhoenixDownRecoveryService.HoldMovement)
                return;
            Measure("fate-sync", FateSyncService.Update);
            Measure("follow", FollowService.Update);
            Measure("mount", MountService.Update);
            Measure("automation", AutomationService.Update);
            if (chocoboPurchase.HoldsMovement) return;
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
            dtrEntry.Text = new SeString(new TextPayload(uiText.Language == "hi" ? "FR: Off" : uiText.Label("FR: Off")));
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
                var statusKey = config.Enabled ? "FR: On" : "FR: Off";
                var statusText = uiText.Language == "hi" ? statusKey : uiText.Label(statusKey);
                dtrEntry.Text = new SeString(new TextPayload(statusText));
                break;
        }

        using var text = uiText.Enter();
        var fren = Configuration.KrangleEnabled ? KrangleService.KrangleName(config.FrenName) : config.FrenName;
        // Game-rendered DTR text cannot use the ImGui shaping route.
        if (uiText.Language == "hi")
        {
            dtrEntry.Tooltip = new SeString(new TextPayload(config.Enabled
                ? $"Fren Rider active - Following {fren}. Coppelia: {CoppeliaPowerlevelLeaseService.StatusText}. Cleanup: {ExternalAutomationCleanupService.StatusText}"
                : $"Fren Rider disabled - Click to toggle. Coppelia: {CoppeliaPowerlevelLeaseService.StatusText}. Cleanup: {ExternalAutomationCleanupService.StatusText}"));
            return;
        }
        dtrEntry.Tooltip = new SeString(new TextPayload(config.Enabled
            ? UiText.F("Fren Rider active - Following {0}. Coppelia: {1}. Cleanup: {2}", fren,
                UiText.T(CoppeliaPowerlevelLeaseService.StatusText), UiText.T(ExternalAutomationCleanupService.StatusText))
            : UiText.F("Fren Rider disabled - Click to toggle. Coppelia: {0}. Cleanup: {1}",
                UiText.T(CoppeliaPowerlevelLeaseService.StatusText), UiText.T(ExternalAutomationCleanupService.StatusText))));
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
    public void ToggleMiniUi() => MagiaMiniWindow.Toggle();
    internal void ApplyWindowOpacity(MaterialWindowOpacity opacity, string name)
    {
        opacity.Apply(name, Math.Clamp(Configuration.UiWindowOpacityPercent, 10, 100) / 100f, Configuration.UiTransparencyEnabled,
            Configuration.UiAutoFade, Math.Clamp(Configuration.UiFadedOpacityPercent, 10, 100) / 100f,
            float.IsFinite(Configuration.UiUnfocusedDelaySeconds) ? Math.Max(0, Configuration.UiUnfocusedDelaySeconds) : 10);
    }

    internal void DrawWindowAppearance()
    {
        UiGui.TextUnformatted("Window appearance");
        DrawAppearanceSelector();
        DrawCompactSelector();
        var config = Configuration;
        var changed = false;
        var compactVisibleOnMainWindow = config.UiCompactVisibleOnMainWindow;
        if (UiGui.Checkbox("Compact visible on main window" + "###UiCompactVisibleOnMainWindowSettings", ref compactVisibleOnMainWindow))
        { config.UiCompactVisibleOnMainWindow = compactVisibleOnMainWindow; changed = true; }
        var transparencyVisibleOnMainWindow = config.UiTransparencyVisibleOnMainWindow;
        if (UiGui.Checkbox("Transparency visible on main window###UiTransparencyVisibleOnMainWindowSettings", ref transparencyVisibleOnMainWindow))
        { config.UiTransparencyVisibleOnMainWindow = transparencyVisibleOnMainWindow; changed = true; }
        var languageVisibleOnMainWindow = config.UiLanguageVisibleOnMainWindow;
        if (UiGui.Checkbox("Language visible on main window" + "###UiLanguageVisibleOnMainWindowSettings", ref languageVisibleOnMainWindow))
        { config.UiLanguageVisibleOnMainWindow = languageVisibleOnMainWindow; changed = true; }
        var transparencyEnabled = config.UiTransparencyEnabled;
        if (UiGui.Checkbox("Transparency" + "###UiTransparencyEnabledSettings", ref transparencyEnabled))
        { config.UiTransparencyEnabled = transparencyEnabled; changed = true; }
        var autoFade = config.UiAutoFade;
        if (UiGui.Checkbox("Auto-fade when unfocused" + "###UiAutoFadeSettings", ref autoFade))
        { config.UiAutoFade = autoFade; changed = true; }
        ImGui.BeginDisabled(!transparencyEnabled);
        try
        {
        var opacity = Math.Clamp(config.UiWindowOpacityPercent, 10, 100);
        ImGui.SetNextItemWidth(180 * MaterialTheme.Metrics.Scale);
        if (UiGui.AppearanceSliderInt("Opacity (%)" + "###UiWindowOpacityPercentSettings", ref opacity, 10, 100, "%d%%", ImGuiSliderFlags.AlwaysClamp))
        { config.UiWindowOpacityPercent = opacity; changed = true; }
        var fadedOpacity = Math.Clamp(config.UiFadedOpacityPercent, 10, 100);
        ImGui.SetNextItemWidth(180 * MaterialTheme.Metrics.Scale);
        if (UiGui.AppearanceSliderInt("Unfocused opacity (%)" + "###UiFadedOpacityPercentSettings", ref fadedOpacity, 10, 100, "%d%%", ImGuiSliderFlags.AlwaysClamp))
        { config.UiFadedOpacityPercent = fadedOpacity; changed = true; }
        ImGui.BeginDisabled(!autoFade);
        try
        {
        var delay = float.IsFinite(config.UiUnfocusedDelaySeconds) ? Math.Max(0, config.UiUnfocusedDelaySeconds) : 10;
        ImGui.SetNextItemWidth(180 * MaterialTheme.Metrics.Scale);
        if (UiGui.AppearanceInputFloat("Unfocused delay (seconds)" + "###UiUnfocusedDelaySecondsSettings", ref delay))
        { delay = float.IsFinite(delay) ? Math.Max(0, delay) : 10; config.UiUnfocusedDelaySeconds = delay; changed = true; }
        }
        finally { ImGui.EndDisabled(); }
        }
        finally { ImGui.EndDisabled(); }
        if (changed) config.Save();
    }

    internal void DrawTransparency()
    {
        var enabled = Configuration.UiTransparencyEnabled;
        if (UiGui.Checkbox("Transparency" + "###UiTransparencyHeader", ref enabled))
        { Configuration.UiTransparencyEnabled = enabled; Configuration.Save(); }
    }

}
