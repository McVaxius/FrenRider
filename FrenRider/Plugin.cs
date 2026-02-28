using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
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
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/frenrider";

    public Configuration Configuration { get; init; }
    public ConfigManager ConfigManager { get; init; }
    public FrenTracker FrenTracker { get; init; }
    public ZoneService ZoneService { get; init; }
    public FollowService FollowService { get; init; }
    public MountService MountService { get; init; }
    public CombatService CombatService { get; init; }
    public AutomationService AutomationService { get; init; }
    public FormationService FormationService { get; init; }
    public string[] MountNames { get; private set; } = Array.Empty<string>();

    public readonly WindowSystem WindowSystem = new("FrenRider");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    private IDtrBarEntry? dtrEntry;
    private bool wasLoggedIn;
    private int loginDetectionDelay;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        ConfigManager = new ConfigManager(PluginInterface, Log);

        if (!string.IsNullOrEmpty(Configuration.LastAccountId))
            ConfigManager.CurrentAccountId = Configuration.LastAccountId;

        FrenTracker = new FrenTracker(this);
        ZoneService = new ZoneService();
        FollowService = new FollowService(this, FrenTracker, ZoneService);
        MountService = new MountService(this, FrenTracker, ZoneService);
        CombatService = new CombatService(this, FrenTracker, ZoneService);
        AutomationService = new AutomationService(this, FrenTracker, ZoneService);
        FormationService = new FormationService(this, FrenTracker);

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Fren Rider main window."
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

        Log.Information("===Fren Rider loaded!===");
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        ClientState.Login -= OnLoginEvent;

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        dtrEntry?.Remove();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        MainWindow.Toggle();
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
                var contentId = PlayerState.ContentId;
                Log.Information($"OnLogin: Character={charName}@{worldName}, ContentId={contentId:X16}");
                ConfigManager.EnsureAccountSelected(contentId, charName);
                ConfigManager.EnsureCharacterExists(charName, worldName);
                Configuration.LastAccountId = ConfigManager.CurrentAccountId;
                Configuration.Save();
                Log.Information($"Character detected: {charName}@{worldName} -> Account {ConfigManager.CurrentAccountId}");
            }
            else
            {
                Log.Warning($"OnLogin: Missing data - charName={charName}, worldName={worldName}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error during login detection: {ex.Message}");
        }
    }

    private void OnFrameworkUpdate(IFramework fw)
    {
        // Delayed login detection (LocalPlayer may not be ready immediately)
        if (ClientState.IsLoggedIn && !wasLoggedIn)
        {
            wasLoggedIn = true;
            loginDetectionDelay = 3; // Wait a few frames for LocalPlayer to be ready
        }
        else if (!ClientState.IsLoggedIn && wasLoggedIn)
        {
            wasLoggedIn = false;
            loginDetectionDelay = 0;
        }

        if (loginDetectionDelay > 0)
        {
            loginDetectionDelay--;
            if (loginDetectionDelay == 0)
                OnLogin();
        }

        // Update DTR bar
        UpdateDtrBar();

        // Update fren tracking
        FrenTracker.Update();

        // Update zone detection, following, and mount system
        ZoneService.Update();
        FollowService.Update();
        MountService.Update();
        CombatService.Update();
        AutomationService.Update();
        FormationService.Update();
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
                cfg.Enabled = !cfg.Enabled;
                ConfigManager.SaveCurrentAccount();
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
        var statusText = config.Enabled ? "FR: On" : "FR: Off";
        dtrEntry.Text = new SeString(new TextPayload(statusText));
        dtrEntry.Tooltip = new SeString(new TextPayload(
            config.Enabled
                ? $"Fren Rider active - Following {config.FrenName}"
                : "Fren Rider disabled - Click to toggle"));
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

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
