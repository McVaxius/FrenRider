using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FrenRider.Services;

public sealed class BossModConflictWarningService
{
    internal const long WarningIntervalMs = 5_000;
    internal const string WarningMessage =
        "BossModReborn (BMR) and BossMod (VBM) are both loaded. Disable one to avoid combat conflicts.";

    private readonly Func<bool> hasConflict;
    private readonly Action<string> showWarning;
    private readonly Func<long> getNowMs;
    private readonly Action<Exception>? logFailure;

    private bool conflictActive;
    private long nextWarningMs;

    public BossModConflictWarningService(
        IDalamudPluginInterface pluginInterface,
        IToastGui toastGui,
        IPluginLog log)
        : this(
            () => HasLoadedConflict(pluginInterface),
            message =>
            {
                toastGui.ShowError($"Fren Rider: {message}");
                log.Warning($"[FrenRider] {message}");
            },
            () => Environment.TickCount64,
            ex => log.Debug($"[FrenRider] BossMod conflict warning check failed: {ex.Message}"))
    {
    }

    internal BossModConflictWarningService(
        Func<bool> hasConflict,
        Action<string> showWarning,
        Func<long> getNowMs,
        Action<Exception>? logFailure = null)
    {
        this.hasConflict = hasConflict;
        this.showWarning = showWarning;
        this.getNowMs = getNowMs;
        this.logFailure = logFailure;
    }

    internal static BossModConflictWarningService CreateForTesting(
        Func<bool> hasConflict,
        Action<string> showWarning,
        Func<long> getNowMs)
        => new(hasConflict, showWarning, getNowMs);

    public void Update(bool frenRiderEnabled)
    {
        if (!frenRiderEnabled)
        {
            Reset();
            return;
        }

        bool conflict;
        try
        {
            conflict = hasConflict();
        }
        catch (Exception ex)
        {
            Reset();
            logFailure?.Invoke(ex);
            return;
        }

        if (!conflict)
        {
            Reset();
            return;
        }

        var now = getNowMs();
        if (conflictActive && now < nextWarningMs)
            return;

        conflictActive = true;
        nextWarningMs = now + WarningIntervalMs;
        try
        {
            showWarning(WarningMessage);
        }
        catch (Exception ex)
        {
            logFailure?.Invoke(ex);
        }
    }

    private static bool HasLoadedConflict(IDalamudPluginInterface pluginInterface)
    {
        var bmrLoaded = false;
        var vbmLoaded = false;
        foreach (var plugin in pluginInterface.InstalledPlugins)
        {
            if (!plugin.IsLoaded)
                continue;

            bmrLoaded |= string.Equals(plugin.InternalName, "BossModReborn", StringComparison.OrdinalIgnoreCase);
            vbmLoaded |= string.Equals(plugin.InternalName, "BossMod", StringComparison.OrdinalIgnoreCase);
            if (bmrLoaded && vbmLoaded)
                return true;
        }

        return false;
    }

    private void Reset()
    {
        conflictActive = false;
        nextWarningMs = 0;
    }
}
