using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FrenRider.Services;

namespace FrenRider.IPC;

/// <summary>
/// Minimal IPC surface used by Questionable Companion's hunt-log combat mode.
/// </summary>
public sealed class CombatOnlyIPC : IDisposable
{
    public const string IsReadyEndpoint = "FrenRider.CombatOnly.IsReady";
    public const string ClearFrenNameEndpoint = "FrenRider.CombatOnly.ClearFrenName";

    private readonly ConfigManager configManager;
    private readonly IPluginLog log;
    private readonly ICallGateProvider<bool> isReadyProvider;
    private readonly ICallGateProvider<bool> clearFrenNameProvider;

    public CombatOnlyIPC(IDalamudPluginInterface pluginInterface, ConfigManager configManager, IPluginLog log)
    {
        this.configManager = configManager;
        this.log = log;

        isReadyProvider = pluginInterface.GetIpcProvider<bool>(IsReadyEndpoint);
        clearFrenNameProvider = pluginInterface.GetIpcProvider<bool>(ClearFrenNameEndpoint);

        isReadyProvider.RegisterFunc(() => true);
        clearFrenNameProvider.RegisterFunc(ClearActiveFrenName);
    }

    public void Dispose()
    {
        clearFrenNameProvider.UnregisterFunc();
        isReadyProvider.UnregisterFunc();
    }

    private bool ClearActiveFrenName()
    {
        try
        {
            var cleared = configManager.ClearActiveFrenName();
            if (cleared)
                log.Information("[FrenRider][CombatOnly] Cleared and saved FrenName for the active character configuration.");
            else
                log.Warning("[FrenRider][CombatOnly] Could not clear FrenName because no active account configuration could be saved.");

            return cleared;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[FrenRider][CombatOnly] Failed to clear the active FrenName.");
            return false;
        }
    }
}
