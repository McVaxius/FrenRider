using System;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FrenRider.Services;

internal sealed class AdsAvailabilityCache
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(2);

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private readonly string logPrefix;

    private bool cachedLoaded;
    private DateTime cacheExpiresUtc = DateTime.MinValue;

    public AdsAvailabilityCache(IDalamudPluginInterface pluginInterface, IPluginLog log, string logPrefix)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
        this.logPrefix = logPrefix;
    }

    public bool IsLoaded(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && now < cacheExpiresUtc)
            return cachedLoaded;

        cachedLoaded = Scan();
        cacheExpiresUtc = now + CacheTtl;
        return cachedLoaded;
    }

    private bool Scan()
    {
        try
        {
            return pluginInterface.InstalledPlugins.Any(installedPlugin =>
                installedPlugin.IsLoaded
                && (string.Equals(installedPlugin.InternalName, "ADS", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(installedPlugin.Name, "AI Duty Solver", StringComparison.OrdinalIgnoreCase)
                    || installedPlugin.Name.Contains("ADS", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex)
        {
            log.Debug($"{logPrefix} Failed to inspect ADS availability: {ex.Message}");
            return false;
        }
    }
}
