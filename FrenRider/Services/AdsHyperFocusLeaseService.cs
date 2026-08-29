using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;
using FrenRider.Models;

namespace FrenRider.Services;

internal static class AdsHyperFocusContract
{
    public const int Version = 1;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed record AdsHyperFocusRequest(string SessionToken);

internal sealed record AdsHyperFocusResponse(
    bool Ok,
    string Reason,
    int ContractVersion = AdsHyperFocusContract.Version);

internal sealed record AdsHyperFocusEnvironment(
    bool FrenRiderEnabled,
    bool InDuty,
    AdsDutyCategory? DutyCategory,
    bool AdsHandoffPending,
    bool AdsControllingDuty,
    bool CoppeliaLeaseActive);

internal sealed record AdsHyperFocusStatus(
    int ContractVersion,
    bool LeaseActive,
    string LeaseReason,
    bool FrenRiderEnabled,
    bool InDuty,
    AdsDutyCategory? DutyCategory,
    bool AdsHandoffPending,
    bool AdsControllingDuty,
    bool CoppeliaLeaseActive);

internal sealed class AdsHyperFocusLeaseCoordinator
{
    private static readonly TimeSpan LeaseExpiry = TimeSpan.FromSeconds(5);
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Func<AdsHyperFocusEnvironment> readEnvironment;
    private readonly Action<string> releaseCombat;

    private string ownerToken = string.Empty;
    private DateTimeOffset lastHeartbeatUtc = DateTimeOffset.MinValue;

    public AdsHyperFocusLeaseCoordinator(
        Func<DateTimeOffset> utcNow,
        Func<AdsHyperFocusEnvironment> readEnvironment,
        Action<string> releaseCombat)
    {
        this.utcNow = utcNow;
        this.readEnvironment = readEnvironment;
        this.releaseCombat = releaseCombat;
    }

    public bool IsLeaseActive => !string.IsNullOrWhiteSpace(ownerToken);
    public string LeaseReason { get; private set; } = "No active ADS Hyper Focus lease.";
    public int LeaseGeneration { get; private set; }

    public AdsHyperFocusResponse Acquire(string sessionToken)
    {
        sessionToken = NormalizeToken(sessionToken);
        if (string.IsNullOrWhiteSpace(sessionToken))
            return Reject("Missing ADS Hyper Focus session token.");

        Update();
        if (IsLeaseActive && !string.Equals(ownerToken, sessionToken, StringComparison.Ordinal))
            return Reject("ADS Hyper Focus lease is already owned by another session.");

        var environment = readEnvironment();
        if (!environment.FrenRiderEnabled)
            return Reject("FrenRider is disabled.");
        if (!environment.InDuty)
            return Reject("FrenRider is not in a duty.");
        if (environment.DutyCategory != AdsDutyCategory.Solo)
            return Reject("ADS Hyper Focus requires a validated solo duty.");
        if (!environment.AdsHandoffPending && !environment.AdsControllingDuty)
            return Reject("ADS does not own or pending-own this duty.");
        if (environment.CoppeliaLeaseActive)
            return Reject("Coppelia Powerlevel lease is active.");

        var wasActive = IsLeaseActive;
        ownerToken = sessionToken;
        lastHeartbeatUtc = utcNow();
        LeaseReason = "ADS Hyper Focus lease active.";
        if (!wasActive)
            LeaseGeneration++;
        return new AdsHyperFocusResponse(true, LeaseReason);
    }

    public AdsHyperFocusResponse Heartbeat(string sessionToken)
    {
        sessionToken = NormalizeToken(sessionToken);
        Update();
        if (!IsLeaseActive)
            return Reject("No active ADS Hyper Focus lease.");
        if (!string.Equals(ownerToken, sessionToken, StringComparison.Ordinal))
            return Reject("ADS Hyper Focus lease is owned by another session.");

        lastHeartbeatUtc = utcNow();
        LeaseReason = "ADS Hyper Focus heartbeat received.";
        return new AdsHyperFocusResponse(true, LeaseReason);
    }

    public AdsHyperFocusResponse Release(string sessionToken)
    {
        sessionToken = NormalizeToken(sessionToken);
        if (!IsLeaseActive)
            return new AdsHyperFocusResponse(true, "No active ADS Hyper Focus lease.");
        if (!string.Equals(ownerToken, sessionToken, StringComparison.Ordinal))
            return Reject("ADS Hyper Focus lease is owned by another session.");

        ClearLease("ADS Hyper Focus lease released.");
        releaseCombat("normal release");
        return new AdsHyperFocusResponse(true, LeaseReason);
    }

    public void Update()
    {
        if (!IsLeaseActive || utcNow() - lastHeartbeatUtc <= LeaseExpiry)
            return;

        ClearLease("ADS Hyper Focus lease expired.");
        releaseCombat("lease expiry");
    }

    public void ManualFrenRiderDisable()
    {
        if (IsLeaseActive)
            ClearLease("ADS Hyper Focus lease revoked because FrenRider was manually disabled.");
    }

    public AdsHyperFocusStatus BuildStatus()
    {
        Update();
        var environment = readEnvironment();
        return new AdsHyperFocusStatus(
            AdsHyperFocusContract.Version,
            IsLeaseActive,
            LeaseReason,
            environment.FrenRiderEnabled,
            environment.InDuty,
            environment.DutyCategory,
            environment.AdsHandoffPending,
            environment.AdsControllingDuty,
            environment.CoppeliaLeaseActive);
    }

    private void ClearLease(string reason)
    {
        ownerToken = string.Empty;
        lastHeartbeatUtc = DateTimeOffset.MinValue;
        LeaseReason = reason;
    }

    private static AdsHyperFocusResponse Reject(string reason) => new(false, reason);
    private static string NormalizeToken(string? sessionToken) => string.IsNullOrWhiteSpace(sessionToken) ? string.Empty : sessionToken.Trim();
}

public sealed class AdsHyperFocusLeaseService : IDisposable
{
    private const string AcquireEndpoint = "FrenRider.ADS.HyperFocus.Acquire";
    private const string HeartbeatEndpoint = "FrenRider.ADS.HyperFocus.Heartbeat";
    private const string ReleaseEndpoint = "FrenRider.ADS.HyperFocus.Release";
    private const string StatusEndpoint = "FrenRider.ADS.HyperFocus.Status";

    private readonly Plugin plugin;
    private readonly AdsHyperFocusLeaseCoordinator coordinator;
    private ICallGateProvider<string, string>? acquireProvider;
    private ICallGateProvider<string, string>? heartbeatProvider;
    private ICallGateProvider<string, string>? releaseProvider;
    private ICallGateProvider<string>? statusProvider;
    private int combatActivatedGeneration;

    public AdsHyperFocusLeaseService(Plugin plugin)
    {
        this.plugin = plugin;
        coordinator = new AdsHyperFocusLeaseCoordinator(
            () => DateTimeOffset.UtcNow,
            BuildEnvironment,
            reason => plugin.CombatService.RestoreConfiguredCombatAfterAdsHyperFocusLease(reason));
        RegisterIpc();
    }

    public bool IsLeaseActive => coordinator.IsLeaseActive;
    public string StatusText => coordinator.LeaseReason;

    public void Update() => coordinator.Update();

    public bool TryClaimCombatActivation()
    {
        if (!coordinator.IsLeaseActive || combatActivatedGeneration == coordinator.LeaseGeneration)
            return false;

        combatActivatedGeneration = coordinator.LeaseGeneration;
        return true;
    }

    public void HandleManualFrenRiderDisable()
    {
        if (!coordinator.IsLeaseActive)
            return;

        coordinator.ManualFrenRiderDisable();
        plugin.CombatService.RestoreConfiguredCombatAfterAdsHyperFocusLease("manual disable");
    }

    public void Dispose()
    {
        acquireProvider?.UnregisterFunc();
        heartbeatProvider?.UnregisterFunc();
        releaseProvider?.UnregisterFunc();
        statusProvider?.UnregisterFunc();
    }

    private void RegisterIpc()
    {
        acquireProvider = Plugin.PluginInterface.GetIpcProvider<string, string>(AcquireEndpoint);
        heartbeatProvider = Plugin.PluginInterface.GetIpcProvider<string, string>(HeartbeatEndpoint);
        releaseProvider = Plugin.PluginInterface.GetIpcProvider<string, string>(ReleaseEndpoint);
        statusProvider = Plugin.PluginInterface.GetIpcProvider<string>(StatusEndpoint);

        acquireProvider.RegisterFunc(requestJson =>
        {
            var generation = coordinator.LeaseGeneration;
            var response = coordinator.Acquire(ReadToken(requestJson));
            if (response.Ok && coordinator.LeaseGeneration != generation)
                plugin.CombatService.ActivateAdsHyperFocusLease();
            return Serialize(response);
        });
        heartbeatProvider.RegisterFunc(requestJson => Serialize(coordinator.Heartbeat(ReadToken(requestJson))));
        releaseProvider.RegisterFunc(requestJson => Serialize(coordinator.Release(ReadToken(requestJson))));
        statusProvider.RegisterFunc(() => Serialize(coordinator.BuildStatus()));
    }

    private AdsHyperFocusEnvironment BuildEnvironment()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var inDuty = Plugin.Condition[ConditionFlag.BoundByDuty]
            || Plugin.Condition[ConditionFlag.BoundByDuty56]
            || Plugin.Condition[ConditionFlag.BoundByDuty95];
        return new AdsHyperFocusEnvironment(
            config.Enabled,
            inDuty,
            plugin.AdsIntegrationService.GetCurrentDutyCategory(),
            plugin.AdsIntegrationService.IsHandoffPending,
            plugin.AdsIntegrationService.IsControllingDuty,
            plugin.CoppeliaPowerlevelLeaseService.IsLeaseActive);
    }

    private static string ReadToken(string requestJson)
    {
        try
        {
            return JsonSerializer.Deserialize<AdsHyperFocusRequest>(requestJson, AdsHyperFocusContract.JsonOptions)?.SessionToken ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, AdsHyperFocusContract.JsonOptions);
}
