using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FrenRider.Services;

namespace FrenRider.IPC;

/// <summary>
/// Narrow typed handoff used by DAD after a participant enters the requested duty.
/// </summary>
public sealed class DadIPC : IDisposable
{
    public const string ConfigureAndEnableEndpoint = "FrenRider.Dad.ConfigureAndEnable";

    private readonly DadIpcEndpoint endpoint;

    public DadIPC(
        IDalamudPluginInterface pluginInterface,
        ConfigManager configManager,
        FrenTracker frenTracker,
        IPluginLog log)
    {
        var provider = pluginInterface.GetIpcProvider<string, bool>(ConfigureAndEnableEndpoint);
        endpoint = new DadIpcEndpoint(
            register: provider.RegisterFunc,
            unregister: provider.UnregisterFunc,
            configureAndEnable: configManager.ConfigureAndEnableActiveCharacter,
            forceNextTrackerScan: frenTracker.ForceNextScan,
            logInformation: message => log.Information(message),
            logWarning: (exception, message) => log.Warning(exception, message));
    }

    public void Dispose()
        => endpoint.Dispose();
}

internal sealed class DadIpcEndpoint : IDisposable
{
    private readonly Func<string, bool> configureAndEnable;
    private readonly Action forceNextTrackerScan;
    private readonly Action unregister;
    private readonly Action<string>? logInformation;
    private readonly Action<Exception, string>? logWarning;
    private bool disposed;

    internal DadIpcEndpoint(
        Action<Func<string, bool>> register,
        Action unregister,
        Func<string, bool> configureAndEnable,
        Action forceNextTrackerScan,
        Action<string>? logInformation = null,
        Action<Exception, string>? logWarning = null)
    {
        this.unregister = unregister;
        this.configureAndEnable = configureAndEnable;
        this.forceNextTrackerScan = forceNextTrackerScan;
        this.logInformation = logInformation;
        this.logWarning = logWarning;
        register(ConfigureAndEnable);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        unregister();
    }

    private bool ConfigureAndEnable(string nameAtWorld)
    {
        try
        {
            if (!configureAndEnable(nameAtWorld))
            {
                return false;
            }

            forceNextTrackerScan();
            logInformation?.Invoke($"[FrenRider][DAD] Configured exact Fren target '{nameAtWorld}', enabled FrenRider, and requested an immediate tracker scan.");
            return true;
        }
        catch (Exception ex)
        {
            logWarning?.Invoke(ex, "[FrenRider][DAD] ConfigureAndEnable failed.");
            return false;
        }
    }
}
