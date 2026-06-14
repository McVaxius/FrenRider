using System;
using System.Linq;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FrenRider.Services;

public enum AdsDutyOwnershipSource
{
    None,
    Typed,
    JsonFallback,
    StaleHold,
    Unloaded,
    Unreadable,
}

public sealed record AdsDutyOwnershipSnapshot(
    bool AdsLoaded,
    bool StatusReadable,
    bool IsOwned,
    bool InInstancedDuty,
    string OwnershipMode,
    AdsDutyOwnershipSource Source,
    DateTime CapturedAtUtc,
    string Detail)
{
    public static AdsDutyOwnershipSnapshot Empty { get; } = new(
        false,
        false,
        false,
        false,
        string.Empty,
        AdsDutyOwnershipSource.None,
        DateTime.MinValue,
        "Not polled.");
}

public readonly record struct AdsStartDutyRequestResult(bool EndpointAvailable, bool Accepted, string Detail);

public sealed class AdsDutyIpcService : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan TransientOwnedHold = TimeSpan.FromSeconds(5);

    private readonly Func<bool> isAdsLoaded;
    private readonly Func<bool> queryTypedOwnership;
    private readonly Func<string> queryStatusJson;
    private readonly Func<bool> startDutyFromInside;
    private readonly Func<DateTime> utcNow;
    private readonly Action<string> logTransition;

    private DateTime lastPollUtc = DateTime.MinValue;
    private DateTime lastKnownOwnedUtc = DateTime.MinValue;
    private string lastTransitionSignature = string.Empty;

    public AdsDutyIpcService(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        isAdsLoaded = () =>
        {
            try
            {
                return pluginInterface.InstalledPlugins.Any(installedPlugin =>
                    installedPlugin.IsLoaded
                    && (string.Equals(installedPlugin.InternalName, "ADS", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(installedPlugin.Name, "AI Duty Solver", StringComparison.OrdinalIgnoreCase)
                        || installedPlugin.Name.Contains("ADS", StringComparison.OrdinalIgnoreCase)));
            }
            catch
            {
                return false;
            }
        };
        queryTypedOwnership = () => pluginInterface.GetIpcSubscriber<bool>("ADS.IsDutyOwned").InvokeFunc();
        queryStatusJson = () => pluginInterface.GetIpcSubscriber<string>("ADS.GetStatusJson").InvokeFunc();
        startDutyFromInside = () => pluginInterface.GetIpcSubscriber<bool>("ADS.StartDutyFromInside").InvokeFunc();
        utcNow = () => DateTime.UtcNow;
        logTransition = message => log.Information(message);
    }

    public AdsDutyIpcService(
        Func<bool> isAdsLoaded,
        Func<bool> queryTypedOwnership,
        Func<string> queryStatusJson,
        Func<bool> startDutyFromInside,
        Func<DateTime> utcNow,
        Action<string>? logTransition = null)
    {
        this.isAdsLoaded = isAdsLoaded;
        this.queryTypedOwnership = queryTypedOwnership;
        this.queryStatusJson = queryStatusJson;
        this.startDutyFromInside = startDutyFromInside;
        this.utcNow = utcNow;
        this.logTransition = logTransition ?? (_ => { });
    }

    public AdsDutyOwnershipSnapshot Current { get; private set; } = AdsDutyOwnershipSnapshot.Empty;

    public void Dispose()
    {
    }

    public AdsDutyOwnershipSnapshot Refresh(bool force = false)
    {
        var now = utcNow();
        if (!force && now - lastPollUtc < PollInterval)
            return Current;

        lastPollUtc = now;
        if (!isAdsLoaded())
        {
            lastKnownOwnedUtc = DateTime.MinValue;
            return Apply(new AdsDutyOwnershipSnapshot(
                false,
                false,
                false,
                false,
                string.Empty,
                AdsDutyOwnershipSource.Unloaded,
                now,
                "ADS unloaded."));
        }

        try
        {
            var owned = queryTypedOwnership();
            return ApplySuccessful(
                now,
                owned,
                owned,
                owned ? "Owned" : "NotOwned",
                AdsDutyOwnershipSource.Typed,
                "ADS.IsDutyOwned");
        }
        catch (Exception typedException)
        {
            try
            {
                var json = queryStatusJson();
                if (!TryParseFallbackStatus(json, out var inInstancedDuty, out var ownershipMode, out var owned))
                    throw new InvalidOperationException("ADS.GetStatusJson omitted readable duty ownership fields.");

                return ApplySuccessful(
                    now,
                    owned,
                    inInstancedDuty,
                    ownershipMode,
                    AdsDutyOwnershipSource.JsonFallback,
                    "ADS.GetStatusJson fallback");
            }
            catch (Exception jsonException)
            {
                if (Current.IsOwned
                    && lastKnownOwnedUtc != DateTime.MinValue
                    && now - lastKnownOwnedUtc <= TransientOwnedHold)
                {
                    return Apply(Current with
                    {
                        StatusReadable = false,
                        Source = AdsDutyOwnershipSource.StaleHold,
                        CapturedAtUtc = now,
                        Detail = $"Transient IPC failure; preserving owned state. Typed: {typedException.Message}; JSON: {jsonException.Message}",
                    });
                }

                return Apply(new AdsDutyOwnershipSnapshot(
                    true,
                    false,
                    false,
                    false,
                    string.Empty,
                    AdsDutyOwnershipSource.Unreadable,
                    now,
                    $"Duty ownership unreadable. Typed: {typedException.Message}; JSON: {jsonException.Message}"));
            }
        }
    }

    public AdsStartDutyRequestResult RequestStartDutyFromInside()
    {
        try
        {
            var accepted = startDutyFromInside();
            return new AdsStartDutyRequestResult(
                true,
                accepted,
                accepted ? "ADS.StartDutyFromInside accepted." : "ADS.StartDutyFromInside rejected.");
        }
        catch (Exception ex)
        {
            return new AdsStartDutyRequestResult(false, false, ex.Message);
        }
    }

    public static bool TryParseFallbackStatus(
        string json,
        out bool inInstancedDuty,
        out string ownershipMode,
        out bool owned)
    {
        inInstancedDuty = false;
        ownershipMode = string.Empty;
        owned = false;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("inInstancedDuty", out var dutyProperty)
            || dutyProperty.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !root.TryGetProperty("ownershipMode", out var modeProperty)
            || modeProperty.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        inInstancedDuty = dutyProperty.GetBoolean();
        ownershipMode = modeProperty.GetString() ?? string.Empty;
        owned = inInstancedDuty && IsOwnedOrLeavingMode(ownershipMode);
        return true;
    }

    public static bool IsOwnedOrLeavingMode(string ownershipMode)
        => ownershipMode.Equals("OwnedStartOutside", StringComparison.OrdinalIgnoreCase)
           || ownershipMode.Equals("OwnedStartInside", StringComparison.OrdinalIgnoreCase)
           || ownershipMode.Equals("OwnedResumeInside", StringComparison.OrdinalIgnoreCase)
           || ownershipMode.Equals("Leaving", StringComparison.OrdinalIgnoreCase);

    private AdsDutyOwnershipSnapshot ApplySuccessful(
        DateTime now,
        bool owned,
        bool inInstancedDuty,
        string ownershipMode,
        AdsDutyOwnershipSource source,
        string detail)
    {
        lastKnownOwnedUtc = owned ? now : DateTime.MinValue;
        return Apply(new AdsDutyOwnershipSnapshot(
            true,
            true,
            owned,
            inInstancedDuty,
            ownershipMode,
            source,
            now,
            detail));
    }

    private AdsDutyOwnershipSnapshot Apply(AdsDutyOwnershipSnapshot snapshot)
    {
        Current = snapshot;
        var signature = $"{snapshot.AdsLoaded}|{snapshot.StatusReadable}|{snapshot.IsOwned}|{snapshot.InInstancedDuty}|{snapshot.OwnershipMode}|{snapshot.Source}";
        if (string.Equals(signature, lastTransitionSignature, StringComparison.Ordinal))
            return Current;

        lastTransitionSignature = signature;
        logTransition(
            $"[FrenRider][ADS Duty] Ownership transition: loaded={snapshot.AdsLoaded}, readable={snapshot.StatusReadable}, owned={snapshot.IsOwned}, inDuty={snapshot.InInstancedDuty}, mode={snapshot.OwnershipMode}, source={snapshot.Source}.");
        return Current;
    }
}
