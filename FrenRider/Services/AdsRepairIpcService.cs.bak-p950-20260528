using System;
using System.Linq;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FrenRider.Services;

public sealed class AdsRepairStatusSnapshot
{
    public static AdsRepairStatusSnapshot Empty { get; } = new();

    public bool IsAvailable { get; init; }
    public bool StatusReadable { get; init; }
    public bool UtilityRunning { get; init; }
    public string UtilityTask { get; init; } = string.Empty;
    public string UtilityMode { get; init; } = string.Empty;
    public string UtilityStatus { get; init; } = string.Empty;
    public string UtilityLastSuccess { get; init; } = string.Empty;
    public string UtilityLastFailure { get; init; } = string.Empty;
    public DateTime? UtilityCompletedAtUtc { get; init; }
    public DateTime CapturedAtUtc { get; init; }

    public bool IsRepairRunning
        => UtilityRunning
           && (UtilityMode is "self" or "npc-no-inn" or "npc"
               || UtilityTask.Contains("repair", StringComparison.OrdinalIgnoreCase));
}

public sealed class AdsRepairIpcService : IDisposable
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private DateTime lastRefreshUtc = DateTime.MinValue;

    public AdsRepairIpcService(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
    }

    public AdsRepairStatusSnapshot Current { get; private set; } = AdsRepairStatusSnapshot.Empty;
    public bool IsAdsAvailable { get; private set; }

    public void Dispose()
    {
    }

    public bool CheckAvailability()
    {
        IsAdsAvailable = IsAdsLoaded();
        return IsAdsAvailable;
    }

    public AdsRepairStatusSnapshot Refresh(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && now - lastRefreshUtc < TimeSpan.FromSeconds(1))
            return Current;

        lastRefreshUtc = now;
        IsAdsAvailable = IsAdsLoaded();
        if (!IsAdsAvailable)
        {
            Current = AdsRepairStatusSnapshot.Empty;
            return Current;
        }

        try
        {
            var subscriber = pluginInterface.GetIpcSubscriber<string>("ADS.GetStatusJson");
            var json = subscriber.InvokeFunc();
            if (string.IsNullOrWhiteSpace(json))
            {
                Current = new AdsRepairStatusSnapshot
                {
                    IsAvailable = true,
                    StatusReadable = false,
                    CapturedAtUtc = now,
                };
                return Current;
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Current = new AdsRepairStatusSnapshot
            {
                IsAvailable = true,
                StatusReadable = true,
                UtilityRunning = GetBool(root, "utilityRunning"),
                UtilityTask = GetString(root, "utilityTask"),
                UtilityMode = GetString(root, "utilityMode"),
                UtilityStatus = GetString(root, "utilityStatus"),
                UtilityLastSuccess = GetString(root, "utilityLastSuccess"),
                UtilityLastFailure = GetString(root, "utilityLastFailure"),
                UtilityCompletedAtUtc = GetDateTime(root, "utilityCompletedAtUtc"),
                CapturedAtUtc = now,
            };
            return Current;
        }
        catch (Exception ex)
        {
            log.Debug($"[FrenRider][Repair] Failed to read ADS status JSON: {ex.Message}");
            Current = new AdsRepairStatusSnapshot
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
        IsAdsAvailable = IsAdsLoaded();
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
            log.Warning($"[FrenRider][Repair] Failed to inspect installed plugins: {ex.Message}");
            return false;
        }
    }

    private static string BuildFailureText(AdsRepairStatusSnapshot status)
    {
        if (!string.IsNullOrWhiteSpace(status.UtilityLastFailure))
            return status.UtilityLastFailure;

        if (!string.IsNullOrWhiteSpace(status.UtilityStatus))
            return status.UtilityStatus;

        return status.StatusReadable
            ? "ADS did not accept the repair request."
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

    private static DateTime? GetDateTime(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return null;

        return DateTime.TryParse(property.GetString(), out var value)
            ? value
            : null;
    }
}
