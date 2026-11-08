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
    int ContractVersion = CoppeliaPowerlevelContract.Version,
    bool CombatSuppressed = false);

internal sealed record CoppeliaPowerlevelEnvironment(
    bool FrenRiderEnabled,
    string ConfiguredFrenName,
    string VisibleFrenName,
    ulong VisibleFrenObjectId,
    bool CompanionActive,
    string CompanionName,
    ulong CompanionObjectId,
    bool HyperFocusLeaseActive = false);

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
    ulong CompanionObjectId,
    bool CombatSuppressed = false);

internal sealed class CoppeliaPowerlevelLeaseCoordinator
{
    private static readonly TimeSpan LeaseExpiry = TimeSpan.FromSeconds(5);
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Func<CoppeliaPowerlevelEnvironment> readEnvironment;
    private readonly Action<string> recoverAfterLease;
    private readonly Func<bool>? suppressCombat;

    private string ownerToken = string.Empty;
    private DateTimeOffset lastHeartbeatUtc = DateTimeOffset.MinValue;
    private int combatSuppressedGeneration;
    private bool expired;

    public CoppeliaPowerlevelLeaseCoordinator(
        Func<DateTimeOffset> utcNow,
        Func<CoppeliaPowerlevelEnvironment> readEnvironment,
        Action<string> recoverAfterLease,
        Func<bool>? suppressCombat = null)
    {
        this.utcNow = utcNow;
        this.readEnvironment = readEnvironment;
        this.recoverAfterLease = recoverAfterLease;
        this.suppressCombat = suppressCombat;
    }

    public bool IsLeaseActive => !string.IsNullOrWhiteSpace(ownerToken);
    public bool CombatSuppressed => IsLeaseActive && !expired && combatSuppressedGeneration == LeaseGeneration;
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
        if (expired)
            return Reject(LeaseReason);

        var environment = readEnvironment();
        if (!environment.FrenRiderEnabled)
            return Reject("FrenRider is disabled.");

        if (environment.HyperFocusLeaseActive)
            return Reject("ADS Hyper Focus lease is active.");

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

        return AcknowledgeSuppression();
    }

    public CoppeliaPowerlevelResponse Heartbeat(string sessionToken)
    {
        sessionToken = NormalizeToken(sessionToken);
        Update();

        if (!IsLeaseActive)
            return Reject("No active Coppelia Powerlevel lease.");

        if (!string.Equals(ownerToken, sessionToken, StringComparison.Ordinal))
            return Reject("Coppelia Powerlevel lease is owned by another session.");
        if (expired)
            return Reject(LeaseReason);

        lastHeartbeatUtc = utcNow();
        LeaseReason = "Coppelia Powerlevel heartbeat received.";
        return AcknowledgeSuppression();
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
        if (!IsLeaseActive || expired)
            return;

        if (utcNow() - lastHeartbeatUtc <= LeaseExpiry)
            return;

        // Keep the owner and combat fence until its acknowledged cleanup/release,
        // or an explicit manual FrenRider Off. Recovery cannot outrun BMR restore.
        expired = true;
        LeaseReason = "Coppelia Powerlevel lease expired; restore and release or manually disable FrenRider.";
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
            environment.CompanionObjectId,
            CombatSuppressed);
    }

    private CoppeliaPowerlevelResponse AcknowledgeSuppression()
    {
        if (!CombatSuppressed)
        {
            var generation = LeaseGeneration;
            try
            {
                var suppressed = suppressCombat?.Invoke() == true;
                Update();
                if (suppressed && IsLeaseActive && !expired && LeaseGeneration == generation)
                    combatSuppressedGeneration = generation;
            }
            catch { /* A failed provider call never acknowledges suppression. */ }
        }
        if (!CombatSuppressed && !expired)
            LeaseReason = "Coppelia Powerlevel lease active; combat suppression is not confirmed.";
        return new CoppeliaPowerlevelResponse(true, LeaseReason, CombatSuppressed: CombatSuppressed);
    }

    private void ClearLease(string reason)
    {
        ownerToken = string.Empty;
        lastHeartbeatUtc = DateTimeOffset.MinValue;
        expired = false;
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
    private int legacyCombatSuppressedGeneration;
    private bool recoveryCycleActive;

    public CoppeliaPowerlevelLeaseService(Plugin plugin)
    {
        this.plugin = plugin;
        coordinator = new CoppeliaPowerlevelLeaseCoordinator(
            () => DateTimeOffset.UtcNow,
            BuildEnvironment,
            RecoverAfterLease,
            () =>
            {
                plugin.BossModActionTweaksService.ResetRecovery();
                return plugin.CombatService.SuppressForCoppeliaLease();
            });

        RegisterIpc();
    }

    public bool IsLeaseActive => coordinator.IsLeaseActive;
    public bool CombatSuppressed => coordinator.CombatSuppressed;
    public string StatusText => coordinator.LeaseReason;
    public bool ShouldSuppressCompanionAutoSummon => coordinator.IsLeaseActive;
    public bool IsRecoveryCycleActive => recoveryCycleActive;

    public void Update()
        => coordinator.Update();

    internal bool TryClaimLegacyCombatSuppression()
    {
        if (!coordinator.IsLeaseActive || legacyCombatSuppressedGeneration == coordinator.LeaseGeneration)
            return false;
        legacyCombatSuppressedGeneration = coordinator.LeaseGeneration;
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
            plugin.IsFrenRiderEffectivelyEnabled,
            config.FrenName ?? string.Empty,
            fren is { IsFound: true, IsVisible: true } ? fren.Name : string.Empty,
            fren is { IsFound: true, IsVisible: true } ? FindObjectIdByName(fren.Name) : 0,
            companionActive,
            companionName,
            companionObjectId,
            plugin.AdsHyperFocusLeaseService?.IsLeaseActive == true);
    }

    private void RecoverAfterLease(string reason)
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        if (!plugin.IsFrenRiderEffectivelyEnabled)
            return;

        recoveryCycleActive = true;
        try
        {
            Plugin.Log.Information($"[FrenRider][CoppeliaPowerlevel] Recovering after {reason}: cycling FrenRider off/on once.");
            plugin.ConfigManager.DisableForRecovery();
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
