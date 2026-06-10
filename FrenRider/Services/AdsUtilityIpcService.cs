using System;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FrenRider.Services;

public sealed class AdsUtilityStatusSnapshot
{
    public static AdsUtilityStatusSnapshot Empty { get; } = new();

    public bool IsAvailable { get; init; }
    public bool StatusReadable { get; init; }
    public bool UtilityRunning { get; init; }
    public bool UtilitySuppressesGenericYesNo { get; init; }
    public string UtilityTask { get; init; } = string.Empty;
    public string UtilityMode { get; init; } = string.Empty;
    public string UtilityStatus { get; init; } = string.Empty;
    public string UtilityLastSuccess { get; init; } = string.Empty;
    public string UtilityLastFailure { get; init; } = string.Empty;
    public DateTime? UtilityCompletedAtUtc { get; init; }
    public string DesynthMode { get; init; } = string.Empty;
    public string DesynthSource { get; init; } = string.Empty;
    public string DesynthPreset { get; init; } = string.Empty;
    public string DesynthLedgerStatus { get; init; } = string.Empty;
    public int DesynthEligible { get; init; }
    public int DesynthCompleted { get; init; }
    public string DesynthFailure { get; init; } = string.Empty;
    public DateTime CapturedAtUtc { get; init; }

    public bool IsRepairRunning
        => UtilityRunning
           && (UtilityMode is "self" or "npc-no-inn" or "npc-no-teleport-no-inn" or "npc"
               || UtilityTask.Contains("repair", StringComparison.OrdinalIgnoreCase));

    public bool SuppressesGenericYesNo
        => StatusReadable && UtilitySuppressesGenericYesNo;

    public bool IsDesynthRunning
        => UtilityRunning
           && (UtilityMode.Contains("desynth", StringComparison.OrdinalIgnoreCase)
               || UtilityTask.Contains("desynth", StringComparison.OrdinalIgnoreCase));
}

public sealed class AdsUtilityIpcService : IDisposable
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private readonly AdsAvailabilityCache adsAvailabilityCache;
    private DateTime lastRefreshUtc = DateTime.MinValue;

    public AdsUtilityIpcService(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
        adsAvailabilityCache = new AdsAvailabilityCache(pluginInterface, log, "[FrenRider][ADS Utility]");
    }

    public AdsUtilityStatusSnapshot Current { get; private set; } = AdsUtilityStatusSnapshot.Empty;
    public bool IsAdsAvailable { get; private set; }

    public void Dispose()
    {
    }

    public bool CheckAvailability()
    {
        IsAdsAvailable = adsAvailabilityCache.IsLoaded(force: true);
        return IsAdsAvailable;
    }

    public bool ShouldSuppressGenericYesNo()
        => Refresh().SuppressesGenericYesNo;

    public bool IsAnyUtilityRunning()
        => Refresh().UtilityRunning;

    public AdsUtilityStatusSnapshot Refresh(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && now - lastRefreshUtc < TimeSpan.FromMilliseconds(100))
            return Current;

        lastRefreshUtc = now;
        IsAdsAvailable = adsAvailabilityCache.IsLoaded(force);
        if (!IsAdsAvailable)
        {
            Current = AdsUtilityStatusSnapshot.Empty;
            return Current;
        }

        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<string>("ADS.GetStatusJson");
            var json = subscriber.InvokeFunc();
            if (string.IsNullOrWhiteSpace(json))
            {
                Current = new AdsUtilityStatusSnapshot
                {
                    IsAvailable = true,
                    StatusReadable = false,
                    CapturedAtUtc = now,
                };
                return Current;
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Current = new AdsUtilityStatusSnapshot
            {
                IsAvailable = true,
                StatusReadable = true,
                UtilityRunning = GetBool(root, "utilityRunning"),
                UtilitySuppressesGenericYesNo = GetBool(root, "utilitySuppressesGenericYesNo"),
                UtilityTask = GetString(root, "utilityTask"),
                UtilityMode = GetString(root, "utilityMode"),
                UtilityStatus = GetString(root, "utilityStatus"),
                UtilityLastSuccess = GetString(root, "utilityLastSuccess"),
                UtilityLastFailure = GetString(root, "utilityLastFailure"),
                UtilityCompletedAtUtc = GetDateTime(root, "utilityCompletedAtUtc"),
                DesynthMode = GetString(root, "desynthMode"),
                DesynthSource = GetString(root, "desynthSource"),
                DesynthPreset = GetString(root, "desynthPreset"),
                DesynthLedgerStatus = GetString(root, "desynthLedgerStatus"),
                DesynthEligible = GetInt(root, "desynthEligible"),
                DesynthCompleted = GetInt(root, "desynthCompleted"),
                DesynthFailure = GetString(root, "desynthFailure"),
                CapturedAtUtc = now,
            };
            return Current;
        }
        catch (Exception ex)
        {
            log.Debug($"[FrenRider][ADS Utility] Failed to read ADS status JSON: {ex.Message}");
            Current = new AdsUtilityStatusSnapshot
            {
                IsAvailable = true,
                StatusReadable = false,
                CapturedAtUtc = now,
            };
            return Current;
        }
    }

    public bool StartRepair(string mode, out string failure)
    {
        failure = string.Empty;
        IsAdsAvailable = adsAvailabilityCache.IsLoaded(force: true);
        if (!IsAdsAvailable)
        {
            failure = "ADS not loaded.";
            return false;
        }

        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<string, bool>("ADS.StartRepair");
            if (subscriber.InvokeFunc(mode))
                return true;

            failure = BuildFailureText(Refresh(force: true));
            return false;
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            log.Debug($"[FrenRider][Repair] Failed to start ADS repair via IPC: {ex.Message}");
            return false;
        }
    }

    public bool StartDesynth(string mode, out string failure)
    {
        failure = string.Empty;
        IsAdsAvailable = adsAvailabilityCache.IsLoaded(force: true);
        if (!IsAdsAvailable)
        {
            failure = "ADS not loaded.";
            return false;
        }

        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<string, bool>("ADS.StartDesynth");
            if (subscriber.InvokeFunc(mode))
                return true;

            failure = BuildFailureText(Refresh(force: true));
            return false;
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            log.Debug($"[FrenRider][ADS Utility] Failed to start ADS desynthesis via IPC: {ex.Message}");
            return false;
        }
    }

    public bool OpenDesynthConfig(out string failure)
    {
        failure = string.Empty;
        if (adsAvailabilityCache.IsLoaded(force: true))
        {
            try
            {
                if (pluginInterface.GetIpcSubscriber<bool>("ADS.OpenDesynthConfigUi").InvokeFunc())
                    return true;
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }
        }

        return GameHelpers.SendChatCommand("/ads desynth", "ADS Utility");
    }

    private static string BuildFailureText(AdsUtilityStatusSnapshot status)
    {
        if (!string.IsNullOrWhiteSpace(status.UtilityLastFailure))
            return status.UtilityLastFailure;

        if (!string.IsNullOrWhiteSpace(status.UtilityStatus))
            return status.UtilityStatus;

        return status.StatusReadable
            ? "ADS did not accept the utility request."
            : "ADS status was not readable.";
    }

    private static string GetString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static bool GetBool(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var property)
           && property.ValueKind is JsonValueKind.True or JsonValueKind.False
           && property.GetBoolean();

    private static int GetInt(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value) ? value : 0;

    private static DateTime? GetDateTime(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return null;

        return DateTime.TryParse(property.GetString(), out var value)
            ? value
            : null;
    }
}
