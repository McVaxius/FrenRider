using System;
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
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/frenrider";

    public Configuration Configuration { get; init; }
    public ConfigManager ConfigManager { get; init; }

    public readonly WindowSystem WindowSystem = new("FrenRider");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    private IDtrBarEntry? dtrEntry;
    private bool wasLoggedIn;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        ConfigManager = new ConfigManager(PluginInterface, Log);

        if (!string.IsNullOrEmpty(Configuration.LastAccountId))
            ConfigManager.CurrentAccountId = Configuration.LastAccountId;

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

        // DTR bar
        SetupDtrBar();

        // Login detection
        ClientState.Login += OnLogin;
        Framework.Update += OnFrameworkUpdate;

        // If already logged in at plugin load
        if (ClientState.IsLoggedIn)
            OnLogin();

        Log.Information("===Fren Rider loaded!===");
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        ClientState.Login -= OnLogin;

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

    private void OnLogin()
    {
        try
        {
            var charName = ObjectTable.LocalPlayer?.Name.ToString() ?? "";
            var worldName = ObjectTable.LocalPlayer?.HomeWorld.Value.Name.ToString() ?? "";
            if (!string.IsNullOrEmpty(charName) && !string.IsNullOrEmpty(worldName))
            {
                ConfigManager.EnsureCharacterExists(charName, worldName);
                Configuration.LastAccountId = ConfigManager.CurrentAccountId;
                Configuration.Save();
                Log.Information($"Character detected: {charName}@{worldName} -> Account {ConfigManager.CurrentAccountId}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error during login detection: {ex.Message}");
        }
    }

    private void OnFrameworkUpdate(IFramework fw)
    {
        // Delayed login detection (LocalPlayer may not be ready on Login event)
        if (ClientState.IsLoggedIn && !wasLoggedIn)
        {
            wasLoggedIn = true;
            OnLogin();
        }
        else if (!ClientState.IsLoggedIn && wasLoggedIn)
        {
            wasLoggedIn = false;
        }

        // Update DTR bar
        UpdateDtrBar();
    }

    public void SetupDtrBar()
    {
        try
        {
            dtrEntry = DtrBar.Get("Fren Rider");
            dtrEntry.Shown = Configuration.DtrBarEnabled;
            dtrEntry.Text = new SeString(new TextPayload("FR: Off"));
            dtrEntry.OnClick = (_) => MainWindow.Toggle();
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

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
