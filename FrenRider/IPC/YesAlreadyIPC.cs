using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FrenRider.IPC;

/// <summary>
/// Manages YesAlready plugin pause/unpause via ECommons shared data.
/// Pattern from PunishXIV/YesAlready BlockListHandler:
/// YesAlready checks a shared HashSet&lt;string&gt; named "YesAlready.StopRequests".
/// If any entries exist, YesAlready is "Locked" (paused).
/// We add "FrenRider" to pause, remove to unpause.
/// </summary>
public class YesAlreadyIPC : IDisposable
{
    private const string StopRequestsKey = "YesAlready.StopRequests";
    private const string LockName = "FrenRider";

    private readonly IPluginLog log;
    private bool isPaused;

    public bool IsPaused => isPaused;

    public YesAlreadyIPC(IPluginLog log)
    {
        this.log = log;
    }

    /// <summary>
    /// Pause YesAlready by adding our name to the shared stop requests set.
    /// </summary>
    public void Pause()
    {
        if (isPaused) return;

        try
        {
            var stopRequests = Plugin.PluginInterface.GetOrCreateData<HashSet<string>>(StopRequestsKey, () => []);
            stopRequests.Add(LockName);
            isPaused = true;
            log.Information("[YesAlready] Paused (added FrenRider to StopRequests)");
        }
        catch (Exception ex)
        {
            log.Warning($"[YesAlready] Failed to pause: {ex.Message}");
        }
    }

    /// <summary>
    /// Unpause YesAlready by removing our name from the shared stop requests set.
    /// </summary>
    public void Unpause()
    {
        if (!isPaused) return;

        try
        {
            var stopRequests = Plugin.PluginInterface.GetOrCreateData<HashSet<string>>(StopRequestsKey, () => []);
            stopRequests.Remove(LockName);
            isPaused = false;
            log.Information("[YesAlready] Unpaused (removed FrenRider from StopRequests)");
        }
        catch (Exception ex)
        {
            log.Warning($"[YesAlready] Failed to unpause: {ex.Message}");
        }
    }

    public void Dispose()
    {
        // Always unpause on dispose to avoid leaving YesAlready locked
        if (isPaused)
        {
            try
            {
                var stopRequests = Plugin.PluginInterface.GetOrCreateData<HashSet<string>>(StopRequestsKey, () => []);
                stopRequests.Remove(LockName);
                isPaused = false;
                log.Information("[YesAlready] Unpaused on dispose");
            }
            catch (Exception ex)
            {
                log.Warning($"[YesAlready] Failed to unpause on dispose: {ex.Message}");
            }
        }
    }
}
