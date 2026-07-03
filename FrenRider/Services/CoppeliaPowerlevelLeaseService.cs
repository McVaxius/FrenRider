using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Ipc;

namespace FrenRider.Services;

internal static class CoppeliaPowerlevelContract
{
    public const int Version = 1;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed record CoppeliaPowerlevelRequest(string SessionToken);

internal sealed record CoppeliaPowerlevelResponse(
    bool Ok,
    string Reason,
    int ContractVersion = CoppeliaPowerlevelContract.Version);

internal sealed record CoppeliaPowerlevelEnvironment(
    bool FrenRiderEnabled,
    string ConfiguredFrenName,
    string VisibleFrenName,
    ulong VisibleFrenObjectId,
    bool CompanionActive,
    string CompanionName,
    ulong CompanionObjectId);

internal sealed record CoppeliaPowerlevelStatus(
    int ContractVersion,
    bool LeaseActive,
    string LeaseReason,
    bool FrenRiderEnabled,
    string ConfiguredFrenName,
    string VisibleFrenName,
    ulong VisibleFrenObjectId,
    bool CompanionActive,
    string CompanionName,
    ulong CompanionObjectId);

internal sealed class CoppeliaPowerlevelLeaseCoordinator
{
    private static readonly TimeSpan LeaseExpiry = TimeSpan.FromSeconds(5);
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Func<CoppeliaPowerlevelEnvironment> readEnvironment;
    private readonly Action<string> recoverAfterLease;

    private string ownerToken = string.Empty;
    private DateTimeOffset lastHeartbeatUtc = DateTimeOffset.MinValue;

    public CoppeliaPowerlevelLeaseCoordinator(
        Func<DateTimeOffset> utcNow,
        Func<CoppeliaPowerlevelEnvironment> readEnvironment,
        Action<string> recoverAfterLease)
    {
        this.utcNow = utcNow;
        this.readEnvironment = readEnvironment;
        this.recoverAfterLease = recoverAfterLease;
    }

    public bool IsLeaseActive => !string.IsNullOrWhiteSpace(ownerToken);
    public string LeaseReason { get; private set; } = "No active Coppelia lease.";
    public int LeaseGeneration { get; private set; }

    public CoppeliaPowerlevelResponse Acquire(string sessionToken)
    {
        sessionToken = NormalizeToken(sessionToken);
        if (string.IsNullOrWhiteSpace(sessionToken))
            return Reject("Missing Coppelia session token.");

        Update();

        if (IsLeaseActive && !string.Equals(ownerToken, sessionToken, StringComparison.Ordinal))
            return Reject("Coppelia Powerlevel lease is already owned by another session.");

        var environment = readEnvironment();
        if (!environment.FrenRiderEnabled)
            return Reject("FrenRider is disabled.");

        if (string.IsNullOrWhiteSpace(environment.ConfiguredFrenName))
            return Reject("FrenRider has no configured Fren.");

        if (environment.CompanionActive)
            return Reject("A battle companion chocobo is active.");

        var wasActive = IsLeaseActive;
        ownerToken = sessionToken;
        lastHeartbeatUtc = utcNow();
        LeaseReason = "Coppelia Powerlevel lease active.";
        if (!wasActive)
            LeaseGeneration++;

        return new CoppeliaPowerlevelResponse(true, LeaseReason);
    }

    public CoppeliaPowerlevelResponse Heartbeat(string sessionToken)
    {
        sessionToken = NormalizeToken(sessionToken);
        Update();

        if (!IsLeaseActive)
            return Reject("No active Coppelia Powerlevel lease.");

        if (!string.Equals(ownerToken, sessionToken, StringComparison.Ordinal))
            return Reject("Coppelia Powerlevel lease is owned by another session.");

        lastHeartbeatUtc = utcNow();
        LeaseReason = "Coppelia Powerlevel heartbeat received.";
        return new CoppeliaPowerlevelResponse(true, LeaseReason);
    }

    public CoppeliaPowerlevelResponse Release(string sessionToken)
    {
        sessionToken = NormalizeToken(sessionToken);
        if (!IsLeaseActive)
            return new CoppeliaPowerlevelResponse(true, "No active Coppelia Powerlevel lease.");

        if (!string.Equals(ownerToken, sessionToken, StringComparison.Ordinal))
            return Reject("Coppelia Powerlevel lease is owned by another session.");

        ClearLease("Coppelia Powerlevel lease released.");
        recoverAfterLease("normal release");
        return new CoppeliaPowerlevelResponse(true, LeaseReason);
    }

    public void Update()
    {
        if (!IsLeaseActive)
            return;

        if (utcNow() - lastHeartbeatUtc <= LeaseExpiry)
            return;

        ClearLease("Coppelia Powerlevel lease expired.");
        recoverAfterLease("lease expiry");
    }

    public void ManualFrenRiderDisable()
    {
        if (!IsLeaseActive)
            return;

        ClearLease("Coppelia Powerlevel lease revoked because FrenRider was manually disabled.");
    }

    public CoppeliaPowerlevelStatus BuildStatus()
    {
        Update();
        var environment = readEnvironment();
        return new CoppeliaPowerlevelStatus(
            CoppeliaPowerlevelContract.Version,
            IsLeaseActive,
            LeaseReason,
            environment.FrenRiderEnabled,
            environment.ConfiguredFrenName,
            environment.VisibleFrenName,
            environment.VisibleFrenObjectId,
            environment.CompanionActive,
            environment.CompanionName,
            environment.CompanionObjectId);
    }

    private void ClearLease(string reason)
    {
        ownerToken = string.Empty;
        lastHeartbeatUtc = DateTimeOffset.MinValue;
        LeaseReason = reason;
    }

    private static CoppeliaPowerlevelResponse Reject(string reason)
        => new(false, reason);

    private static string NormalizeToken(string? sessionToken)
        => string.IsNullOrWhiteSpace(sessionToken) ? string.Empty : sessionToken.Trim();
}

public sealed class CoppeliaPowerlevelLeaseService : IDisposable
{
    private const string AcquireEndpoint = "FrenRider.Coppelia.Powerlevel.Acquire";
    private const string HeartbeatEndpoint = "FrenRider.Coppelia.Powerlevel.Heartbeat";
    private const string ReleaseEndpoint = "FrenRider.Coppelia.Powerlevel.Release";
    private const string StatusEndpoint = "FrenRider.Coppelia.Powerlevel.Status";

    private readonly Plugin plugin;
    private readonly CoppeliaPowerlevelLeaseCoordinator coordinator;
    private ICallGateProvider<string, string>? acquireProvider;
    private ICallGateProvider<string, string>? heartbeatProvider;
    private ICallGateProvider<string, string>? releaseProvider;
    private ICallGateProvider<string>? statusProvider;
    private int combatSuppressedGeneration;
    private bool recoveryCycleActive;

    public CoppeliaPowerlevelLeaseService(Plugin plugin)
    {
        this.plugin = plugin;
        coordinator = new CoppeliaPowerlevelLeaseCoordinator(
            () => DateTimeOffset.UtcNow,
            BuildEnvironment,
            RecoverAfterLease);

        RegisterIpc();
    }

    public bool IsLeaseActive => coordinator.IsLeaseActive;
    public string StatusText => coordinator.LeaseReason;
    public bool ShouldSuppressCompanionAutoSummon => coordinator.IsLeaseActive;
    public bool IsRecoveryCycleActive => recoveryCycleActive;

    public void Update()
        => coordinator.Update();

    public bool TryClaimCombatSuppression()
    {
        if (!coordinator.IsLeaseActive)
            return false;

        if (combatSuppressedGeneration == coordinator.LeaseGeneration)
            return false;

        combatSuppressedGeneration = coordinator.LeaseGeneration;
        return true;
    }

    public void HandleManualFrenRiderDisable()
    {
        if (recoveryCycleActive)
            return;

        coordinator.ManualFrenRiderDisable();
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

        acquireProvider.RegisterFunc(requestJson => Serialize(coordinator.Acquire(ReadToken(requestJson))));
        heartbeatProvider.RegisterFunc(requestJson => Serialize(coordinator.Heartbeat(ReadToken(requestJson))));
        releaseProvider.RegisterFunc(requestJson => Serialize(coordinator.Release(ReadToken(requestJson))));
        statusProvider.RegisterFunc(() => Serialize(coordinator.BuildStatus()));
    }

    private CoppeliaPowerlevelEnvironment BuildEnvironment()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var fren = plugin.FrenTracker.Fren;
        var (companionActive, companionName, companionObjectId) = FindCompanion();

        return new CoppeliaPowerlevelEnvironment(
            config.Enabled,
            config.FrenName ?? string.Empty,
            fren is { IsFound: true, IsVisible: true } ? fren.Name : string.Empty,
            fren is { IsFound: true, IsVisible: true } ? FindObjectIdByName(fren.Name) : 0,
            companionActive,
            companionName,
            companionObjectId);
    }

    private void RecoverAfterLease(string reason)
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        if (!config.Enabled)
            return;

        recoveryCycleActive = true;
        try
        {
            Plugin.Log.Information($"[FrenRider][CoppeliaPowerlevel] Recovering after {reason}: cycling FrenRider off/on once.");
            plugin.ConfigManager.SetFrenRiderEnabled(false);
            plugin.ConfigManager.SetFrenRiderEnabled(true);
        }
        finally
        {
            recoveryCycleActive = false;
        }
    }

    private static (bool Active, string Name, ulong ObjectId) FindCompanion()
    {
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is IBattleNpc { BattleNpcKind: BattleNpcSubKind.Buddy } buddy)
                return (true, buddy.Name.TextValue, buddy.GameObjectId);
        }

        return (false, string.Empty, 0);
    }

    private static ulong FindObjectIdByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return 0;

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj.ObjectKind != ObjectKind.Pc)
                continue;

            if (string.Equals(obj.Name.TextValue, name, StringComparison.OrdinalIgnoreCase))
                return obj.GameObjectId;
        }

        return 0;
    }

    private static string ReadToken(string requestJson)
    {
        try
        {
            return JsonSerializer.Deserialize<CoppeliaPowerlevelRequest>(
                       requestJson,
                       CoppeliaPowerlevelContract.JsonOptions)?.SessionToken ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, CoppeliaPowerlevelContract.JsonOptions);
}
