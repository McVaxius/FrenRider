using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FrenRider.Models;

namespace FrenRider.Services;

public sealed class AdsReflectionIpcService : IDisposable
{
    public const float ReducedOutdoorMaxLoadDistance = 35f;

    private static readonly TimeSpan ReassertDelay = TimeSpan.FromSeconds(30);

    private readonly Plugin plugin;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;

    private DateTime nextAttemptUtc = DateTime.MinValue;
    private int consecutiveFailures;
    private bool rangeApplied;
    private bool huntsApplied;
    private bool queenApplied;
    private bool lastDesiredRange;
    private bool lastDesiredHunts;
    private bool lastDesiredQueen;

    public AdsReflectionIpcService(Plugin plugin, IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.plugin = plugin;
        this.pluginInterface = pluginInterface;
        this.log = log;
        StatusText = "ADS reflection idle.";
    }

    public string StatusText { get; private set; }
    public bool IsAdsAvailable { get; private set; }
    public bool HasPendingActions { get; private set; }
    public DateTime? LastSuccessAtUtc { get; private set; }
    public DateTime? NextAttemptAtUtc => nextAttemptUtc == DateTime.MinValue || nextAttemptUtc == DateTime.MaxValue
        ? null
        : nextAttemptUtc;

    public void Dispose()
    {
    }

    public void QueueImmediateUpdate()
        => nextAttemptUtc = DateTime.MinValue;

    public void Update(bool force = false)
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        Apply(config, force);
    }

    private void Apply(CharacterConfig config, bool force)
    {
        var desiredRange = config.BmrReduceActivationRangeForOutdoorAreas;
        var desiredHunts = config.BmrDisableHuntModules;
        var desiredQueen = config.BmrDisableQueenLunatender;
        var desiredChanged = desiredRange != lastDesiredRange
                             || desiredHunts != lastDesiredHunts
                             || desiredQueen != lastDesiredQueen;
        lastDesiredRange = desiredRange;
        lastDesiredHunts = desiredHunts;
        lastDesiredQueen = desiredQueen;

        if (desiredChanged)
            force = true;

        HasPendingActions = desiredRange || desiredHunts || desiredQueen || rangeApplied || huntsApplied || queenApplied;
        if (!HasPendingActions)
        {
            consecutiveFailures = 0;
            nextAttemptUtc = DateTime.MaxValue;
            StatusText = "No ADS reflection hacks enabled.";
            return;
        }

        var now = DateTime.UtcNow;
        if (!force && now < nextAttemptUtc)
            return;

        IsAdsAvailable = IsAdsLoaded();
        if (!IsAdsAvailable)
        {
            ScheduleFailure(now, "ADS unavailable; reflection hacks pending.");
            return;
        }

        var failures = new List<string>();

        if (desiredRange)
        {
            if (TrySetMaxLoadDistance(ReducedOutdoorMaxLoadDistance, out var error))
                rangeApplied = true;
            else
                failures.Add($"range ({error})");
        }
        else if (rangeApplied)
        {
            if (TryResetMaxLoadDistance(out var error))
                rangeApplied = false;
            else
                failures.Add($"range reset ({error})");
        }

        if (desiredHunts)
        {
            if (TrySetHuntsDisabled(true, out var error))
                huntsApplied = true;
            else
                failures.Add($"hunts ({error})");
        }
        else if (huntsApplied)
        {
            if (TrySetHuntsDisabled(false, out var error))
                huntsApplied = false;
            else
                failures.Add($"hunts reset ({error})");
        }

        if (desiredQueen)
        {
            if (TrySetQueenLunatenderDisabled(true, out var error))
                queenApplied = true;
            else
                failures.Add($"queen ({error})");
        }
        else if (queenApplied)
        {
            if (TrySetQueenLunatenderDisabled(false, out var error))
                queenApplied = false;
            else
                failures.Add($"queen reset ({error})");
        }

        if (failures.Count > 0)
        {
            ScheduleFailure(now, $"ADS unavailable/pending: {string.Join(", ", failures)}.");
            return;
        }

        consecutiveFailures = 0;
        LastSuccessAtUtc = now;

        if (desiredRange || desiredHunts || desiredQueen)
        {
            nextAttemptUtc = now + ReassertDelay;
            StatusText = $"ADS reflection applied; reasserting every {ReassertDelay.TotalSeconds:0}s.";
        }
        else
        {
            nextAttemptUtc = DateTime.MaxValue;
            StatusText = "ADS reflection hacks reset.";
        }
    }

    private void ScheduleFailure(DateTime now, string status)
    {
        consecutiveFailures++;
        var delaySeconds = consecutiveFailures switch
        {
            1 => 5,
            2 => 10,
            3 => 20,
            _ => 30,
        };

        nextAttemptUtc = now + TimeSpan.FromSeconds(delaySeconds);
        StatusText = status;
    }

    private bool TrySetMaxLoadDistance(float value, out string error)
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<float, bool>("ADS.Reflection.BMR.SetMaxLoadDistance");
            if (subscriber.InvokeFunc(value))
            {
                error = string.Empty;
                return true;
            }

            error = "ADS returned false";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            log.Debug($"[ADS.Reflection] SetMaxLoadDistance failed: {ex.Message}");
            return false;
        }
    }

    private bool TryResetMaxLoadDistance(out string error)
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<bool>("ADS.Reflection.BMR.ResetMaxLoadDistance");
            if (subscriber.InvokeFunc())
            {
                error = string.Empty;
                return true;
            }

            error = "ADS returned false";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            log.Debug($"[ADS.Reflection] ResetMaxLoadDistance failed: {ex.Message}");
            return false;
        }
    }

    private bool TrySetHuntsDisabled(bool disabled, out string error)
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<bool, bool>("ADS.Reflection.BMR.SetHuntsDisabled");
            if (subscriber.InvokeFunc(disabled))
            {
                error = string.Empty;
                return true;
            }

            error = "ADS returned false";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            log.Debug($"[ADS.Reflection] SetHuntsDisabled({disabled}) failed: {ex.Message}");
            return false;
        }
    }

    private bool TrySetQueenLunatenderDisabled(bool disabled, out string error)
    {
        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<bool, bool>("ADS.Reflection.BMR.SetQueenLunatenderDisabled");
            if (subscriber.InvokeFunc(disabled))
            {
                error = string.Empty;
                return true;
            }

            error = "ADS returned false";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            log.Debug($"[ADS.Reflection] SetQueenLunatenderDisabled({disabled}) failed: {ex.Message}");
            return false;
        }
    }

    private bool IsAdsLoaded()
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
            log.Debug($"[ADS.Reflection] Failed to inspect ADS availability: {ex.Message}");
            return false;
        }
    }
}
