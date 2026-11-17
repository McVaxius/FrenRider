using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FrenRider.Models;

namespace FrenRider.Services;

public sealed class AdsReflectionIpcService : IDisposable
{
    public const float ReducedOutdoorMaxLoadDistance = 35f;

    private readonly Plugin plugin;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private readonly AdsAvailabilityCache adsAvailabilityCache;

    private DateTime nextAttemptUtc = DateTime.MinValue;
    private int consecutiveFailures;
    private bool rangeApplied;
    private bool huntsApplied;
    private bool queenApplied;
    private bool lastDesiredRange;
    private bool lastDesiredHunts;
    private bool lastDesiredQueen;
    private bool lastAdsAvailable;
    private bool lastApplyFailed;

    public AdsReflectionIpcService(Plugin plugin, IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.plugin = plugin;
        this.pluginInterface = pluginInterface;
        this.log = log;
        adsAvailabilityCache = new AdsAvailabilityCache(pluginInterface, log, "[ADS.Reflection]");
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
        if (!plugin.IsFrenRiderEffectivelyEnabled)
        {
            HasPendingActions = false;
            StatusText = "Automatic ADS reflection paused.";
            return;
        }
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
            lastApplyFailed = false;
            nextAttemptUtc = DateTime.MaxValue;
            StatusText = "No ADS reflection hacks enabled.";
            return;
        }

        var now = DateTime.UtcNow;
        if (!force && now < nextAttemptUtc)
            return;

        IsAdsAvailable = adsAvailabilityCache.IsLoaded(force);
        var adsReloadDetected = IsAdsAvailable && !lastAdsAvailable;
        lastAdsAvailable = IsAdsAvailable;
        if (!IsAdsAvailable)
        {
            lastApplyFailed = true;
            ScheduleFailure(now, "ADS unavailable; reflection hacks pending.");
            return;
        }

        force = force || adsReloadDetected || lastApplyFailed;
        if (!force
            && rangeApplied == desiredRange
            && huntsApplied == desiredHunts
            && queenApplied == desiredQueen)
        {
            nextAttemptUtc = DateTime.MaxValue;
            StatusText = "ADS reflection applied; no reassert pending.";
            return;
        }

        var failures = new List<string>();

        if (!plugin.IsFrenRiderEffectivelyEnabled) return;
        if (desiredRange && (!rangeApplied || force))
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

        if (!plugin.IsFrenRiderEffectivelyEnabled) return;
        if (desiredHunts && (!huntsApplied || force))
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

        if (!plugin.IsFrenRiderEffectivelyEnabled) return;
        if (desiredQueen && (!queenApplied || force))
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
            lastApplyFailed = true;
            ScheduleFailure(now, $"ADS unavailable/pending: {string.Join(", ", failures)}.");
            return;
        }

        consecutiveFailures = 0;
        lastApplyFailed = false;
        LastSuccessAtUtc = now;

        if (desiredRange || desiredHunts || desiredQueen)
        {
            nextAttemptUtc = DateTime.MaxValue;
            StatusText = "ADS reflection applied; waiting for desired-state change or ADS reload.";
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

}
