using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FrenRider.Models;

namespace FrenRider.Services;

public enum ExternalAutomationCleanupState
{
    Idle,
    Captured,
    Restored,
    ForceOff,
    Partial,
    Failed,
}

public interface IExternalAutomationCommandSender
{
    bool TrySendCommand(string command);
}

public interface IExternalAutomationSnapshotProvider
{
    ExternalAutomationSnapshot Capture(string accountId, string characterKey);
}

public sealed record RsrCleanupAttempt(bool Success, string ReportedAction, bool UsedFallback);

public interface IRsrCleanupController
{
    RsrCleanupAttempt TurnOff();
}

public sealed class AutorotRsrCleanupController : IRsrCleanupController
{
    public const string TypedActionLabel = "RSR IPC Off";
    public const string FallbackCommand = "/rotation cancel";
    private readonly Func<bool> tryTypedOff;
    private readonly IExternalAutomationCommandSender commandSender;

    public AutorotRsrCleanupController(
        AutorotIpcService autorotIpcService,
        IExternalAutomationCommandSender commandSender)
    {
        tryTypedOff = () => autorotIpcService.TrySetRsrMode(AutorotIpcService.RsrStateCommandType.Off);
        this.commandSender = commandSender;
    }

    public AutorotRsrCleanupController(
        Func<bool> tryTypedOff,
        IExternalAutomationCommandSender commandSender)
    {
        this.tryTypedOff = tryTypedOff;
        this.commandSender = commandSender;
    }

    public RsrCleanupAttempt TurnOff()
    {
        var typedSucceeded = false;
        try
        {
            typedSucceeded = tryTypedOff();
        }
        catch
        {
            // Typed IPC failure still permits the documented command fallback.
        }

        if (typedSucceeded)
            return new RsrCleanupAttempt(true, TypedActionLabel, UsedFallback: false);

        return new RsrCleanupAttempt(
            commandSender.TrySendCommand(FallbackCommand),
            FallbackCommand,
            UsedFallback: true);
    }
}

internal sealed class FallbackOnlyRsrCleanupController(
    IExternalAutomationCommandSender commandSender) : IRsrCleanupController
{
    public RsrCleanupAttempt TurnOff()
        => new(
            commandSender.TrySendCommand(AutorotRsrCleanupController.FallbackCommand),
            AutorotRsrCleanupController.FallbackCommand,
            UsedFallback: true);
}

public interface IDaedalusAutomationController
{
    bool TryGetEnabled(out bool enabled);
    bool TrySetEnabled(bool enabled);
}

public sealed class AutorotDaedalusAutomationController(
    AutorotIpcService autorotIpcService) : IDaedalusAutomationController
{
    public static string GetActionLabel(bool enabled)
        => $"Daedalus IPC SetEnabled({enabled.ToString().ToLowerInvariant()})";

    public bool TryGetEnabled(out bool enabled)
        => autorotIpcService.TryGetDaedalusEnabled(out enabled);

    public bool TrySetEnabled(bool enabled)
        => autorotIpcService.TrySetDaedalusEnabled(enabled);
}

public sealed record BossModAutomationSnapshot(
    bool IsAvailable,
    bool? AiActive,
    bool? ForbidMovement,
    bool? FollowOutOfCombat,
    bool? FollowDuringCombat,
    bool? FollowDuringActiveBossModule,
    bool? FollowTarget,
    int? FollowSlot,
    string Detail)
{
    public static BossModAutomationSnapshot Unavailable(string detail) => new(false, null, null, null, null, null, null, null, detail);
}

public sealed record CbtAutomationSnapshot(bool IsAvailable, bool? AutoFollowEnabled, string Detail)
{
    public static CbtAutomationSnapshot Unavailable(string detail) => new(false, null, detail);
}

public sealed record DaedalusAutomationSnapshot(bool IsAvailable, bool? Enabled, string Detail)
{
    public static DaedalusAutomationSnapshot Unavailable(string detail) => new(false, null, detail);
}

public sealed record ExternalAutomationSnapshot(
    string AccountId,
    string CharacterKey,
    BossModAutomationSnapshot Bmr,
    BossModAutomationSnapshot Vbm,
    CbtAutomationSnapshot Cbt,
    DateTimeOffset CapturedAtUtc,
    DaedalusAutomationSnapshot? Daedalus = null)
{
    public bool HasAnyAvailableState => Bmr.IsAvailable || Vbm.IsAvailable || Cbt.IsAvailable || Daedalus?.IsAvailable == true;
    public bool HasUnavailableState =>
        !Bmr.IsAvailable || !Vbm.IsAvailable || !Cbt.IsAvailable || Daedalus is { IsAvailable: false };
}

public sealed record ExternalAutomationCleanupResult(
    ExternalAutomationCleanupState State,
    string StatusText,
    IReadOnlyList<string> Commands);

public sealed class ExternalAutomationCleanupService
{
    private readonly IExternalAutomationCommandSender commandSender;
    private readonly IExternalAutomationSnapshotProvider snapshotProvider;
    private readonly Action<string>? infoLog;
    private readonly Action<string>? warningLog;
    private readonly IRsrCleanupController rsrCleanupController;
    private readonly IDaedalusAutomationController? daedalusAutomationController;
    private readonly Dictionary<string, ExternalAutomationSnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> wrathStartedKeys = new(StringComparer.OrdinalIgnoreCase);

    public ExternalAutomationCleanupService(
        IExternalAutomationCommandSender commandSender,
        IExternalAutomationSnapshotProvider snapshotProvider,
        Action<string>? infoLog = null,
        Action<string>? warningLog = null,
        IRsrCleanupController? rsrCleanupController = null,
        IDaedalusAutomationController? daedalusAutomationController = null)
    {
        this.commandSender = commandSender;
        this.snapshotProvider = snapshotProvider;
        this.infoLog = infoLog;
        this.warningLog = warningLog;
        this.rsrCleanupController = rsrCleanupController ?? new FallbackOnlyRsrCleanupController(commandSender);
        this.daedalusAutomationController = daedalusAutomationController;
    }

    public ExternalAutomationCleanupState State { get; private set; } = ExternalAutomationCleanupState.Idle;
    public string StatusText { get; private set; } = "Cleanup idle.";

    public bool TryGetSnapshot(string accountId, string characterKey, out ExternalAutomationSnapshot snapshot)
    {
        var found = snapshots.TryGetValue(BuildKey(accountId, characterKey), out var storedSnapshot);
        snapshot = storedSnapshot!;
        return found;
    }

    public void CaptureIfMissing(string accountId, string characterKey, string reason)
    {
        var key = BuildKey(accountId, characterKey);
        if (snapshots.ContainsKey(key))
            return;

        var snapshot = snapshotProvider.Capture(NormalizeAccountId(accountId), NormalizeCharacterKey(characterKey));
        if (daedalusAutomationController != null)
            snapshot = snapshot with { Daedalus = CaptureDaedalus() };
        snapshots[key] = snapshot;

        State = ExternalAutomationCleanupState.Captured;
        StatusText = $"Captured automation snapshot ({FormatSnapshotSummary(snapshot)}).";
        infoLog?.Invoke($"[ExternalCleanup] {StatusText} reason={reason}");
    }

    public void MarkWrathAutoStarted(string accountId, string characterKey, string reason)
    {
        var key = BuildKey(accountId, characterKey);
        wrathStartedKeys.Add(key);
        infoLog?.Invoke($"[ExternalCleanup] Tracked Wrath auto start for {key} ({reason}).");
    }

    public ExternalAutomationCleanupResult Cleanup(
        CharacterConfig config,
        string accountId,
        string characterKey,
        string reason)
    {
        return config.CleanupMode == FrenRiderCleanupMode.TurnEverythingOff
            ? TurnEverythingOff(accountId, characterKey, reason)
            : RestoreSnapshot(accountId, characterKey, reason);
    }

    private ExternalAutomationCleanupResult RestoreSnapshot(string accountId, string characterKey, string reason)
    {
        var key = BuildKey(accountId, characterKey);
        if (!snapshots.TryGetValue(key, out var snapshot))
        {
            State = ExternalAutomationCleanupState.Failed;
            StatusText = "Restore failed: no automation snapshot captured for this account/character.";
            warningLog?.Invoke($"[ExternalCleanup] {StatusText} reason={reason}");
            return new ExternalAutomationCleanupResult(State, StatusText, Array.Empty<string>());
        }

        var attempts = new List<CommandAttempt>();
        RestoreBossMod("BMR", "/bmrai", snapshot.Bmr, attempts);
        RestoreBossMod("VBM", "/vbmai", snapshot.Vbm, attempts);
        RestoreCbt(snapshot.Cbt, attempts);
        RestoreDaedalus(snapshot.Daedalus, attempts);

        var result = CompleteCleanup(
            key,
            attempts,
            successState: snapshot.HasUnavailableState ? ExternalAutomationCleanupState.Partial : ExternalAutomationCleanupState.Restored,
            successText: snapshot.HasUnavailableState
                ? $"Partial restore ({FormatSnapshotSummary(snapshot)})."
                : "Restored captured automation state.",
            reason);

        snapshots.Remove(key);
        wrathStartedKeys.Remove(key);
        return result;
    }

    private ExternalAutomationCleanupResult TurnEverythingOff(string accountId, string characterKey, string reason)
    {
        var key = BuildKey(accountId, characterKey);
        var attempts = new List<CommandAttempt>
        {
            Send("/bmrai off"),
            Send("/vbmai off"),
            Send("/cbt disable AutoFollow"),
        };

        RsrCleanupAttempt rsr;
        try
        {
            rsr = rsrCleanupController.TurnOff();
        }
        catch (Exception ex)
        {
            rsr = new RsrCleanupAttempt(false, AutorotRsrCleanupController.TypedActionLabel, UsedFallback: false);
            warningLog?.Invoke($"[ExternalCleanup] RSR cleanup controller failed: {ex.Message}");
        }
        attempts.Add(new CommandAttempt(rsr.ReportedAction, rsr.Success));

        if (daedalusAutomationController != null)
            attempts.Add(SetDaedalusEnabled(false));

        if (wrathStartedKeys.Contains(key))
            attempts.Add(Send("/wrath auto off"));

        var result = CompleteCleanup(
            key,
            attempts,
            successState: ExternalAutomationCleanupState.ForceOff,
            successText: "Forced FR-managed automation off.",
            reason);

        snapshots.Remove(key);
        wrathStartedKeys.Remove(key);
        return result;
    }

    private ExternalAutomationCleanupResult CompleteCleanup(
        string key,
        IReadOnlyList<CommandAttempt> attempts,
        ExternalAutomationCleanupState successState,
        string successText,
        string reason)
    {
        var commands = attempts.Select(attempt => attempt.Command).ToArray();
        var failed = attempts.Where(attempt => !attempt.Success).ToArray();

        if (attempts.Count == 0 || failed.Length == attempts.Count)
        {
            State = ExternalAutomationCleanupState.Failed;
            StatusText = attempts.Count == 0
                ? "Cleanup failed: no captured automation state was available to restore."
                : $"Cleanup failed: {failed.Length}/{attempts.Count} commands failed.";
            warningLog?.Invoke($"[ExternalCleanup] {StatusText} key={key}; reason={reason}");
            return new ExternalAutomationCleanupResult(State, StatusText, commands);
        }

        if (failed.Length > 0 || successState == ExternalAutomationCleanupState.Partial)
        {
            State = ExternalAutomationCleanupState.Partial;
            StatusText = failed.Length > 0
                ? $"Partial cleanup: {failed.Length}/{attempts.Count} commands failed."
                : successText;
            warningLog?.Invoke($"[ExternalCleanup] {StatusText} key={key}; reason={reason}");
            return new ExternalAutomationCleanupResult(State, StatusText, commands);
        }

        State = successState;
        StatusText = successText;
        infoLog?.Invoke($"[ExternalCleanup] {StatusText} key={key}; reason={reason}");
        return new ExternalAutomationCleanupResult(State, StatusText, commands);
    }

    private void RestoreBossMod(string label, string prefix, BossModAutomationSnapshot snapshot, List<CommandAttempt> attempts)
    {
        if (!snapshot.IsAvailable)
            return;

        if (snapshot.AiActive == false)
            attempts.Add(Send($"{prefix} off"));

        AddBoolCommand(attempts, $"{prefix} forbidmovement", snapshot.ForbidMovement);
        AddBoolCommand(attempts, $"{prefix} followoutofcombat", snapshot.FollowOutOfCombat);
        AddBoolCommand(attempts, $"{prefix} followcombat", snapshot.FollowDuringCombat);
        AddBoolCommand(attempts, $"{prefix} followmodule", snapshot.FollowDuringActiveBossModule);

        if (snapshot.FollowTarget == true && snapshot.FollowSlot is { } followSlot)
            attempts.Add(Send($"{prefix} follow Slot{followSlot}"));

        if (snapshot.AiActive == true)
            attempts.Add(Send($"{prefix} on"));

        infoLog?.Invoke($"[ExternalCleanup] Queued {label} restore commands.");
    }

    private void RestoreCbt(CbtAutomationSnapshot snapshot, List<CommandAttempt> attempts)
    {
        if (!snapshot.IsAvailable || snapshot.AutoFollowEnabled is not { } autoFollowEnabled)
            return;

        attempts.Add(Send(autoFollowEnabled ? "/cbt enable AutoFollow" : "/cbt disable AutoFollow"));
    }

    private DaedalusAutomationSnapshot CaptureDaedalus()
    {
        try
        {
            return daedalusAutomationController!.TryGetEnabled(out var enabled)
                ? new DaedalusAutomationSnapshot(true, enabled, string.Empty)
                : DaedalusAutomationSnapshot.Unavailable("typed IPC unavailable");
        }
        catch (Exception ex)
        {
            warningLog?.Invoke($"[ExternalCleanup] Daedalus snapshot failed: {ex.Message}");
            return DaedalusAutomationSnapshot.Unavailable(ex.Message);
        }
    }

    private void RestoreDaedalus(DaedalusAutomationSnapshot? snapshot, List<CommandAttempt> attempts)
    {
        if (snapshot is not { IsAvailable: true, Enabled: { } enabled })
            return;

        attempts.Add(SetDaedalusEnabled(enabled));
    }

    private CommandAttempt SetDaedalusEnabled(bool enabled)
    {
        var action = AutorotDaedalusAutomationController.GetActionLabel(enabled);
        if (daedalusAutomationController == null)
            return new CommandAttempt(action, false);

        try
        {
            return new CommandAttempt(action, daedalusAutomationController.TrySetEnabled(enabled));
        }
        catch (Exception ex)
        {
            warningLog?.Invoke($"[ExternalCleanup] {action} failed: {ex.Message}");
            return new CommandAttempt(action, false);
        }
    }

    private void AddBoolCommand(List<CommandAttempt> attempts, string commandPrefix, bool? value)
    {
        if (value is not { } enabled)
            return;

        attempts.Add(Send($"{commandPrefix} {(enabled ? "on" : "off")}"));
    }

    private CommandAttempt Send(string command)
        => new(command, commandSender.TrySendCommand(command));

    private static string FormatSnapshotSummary(ExternalAutomationSnapshot snapshot)
    {
        var targets = new List<string>
        {
            FormatTarget("BMR", snapshot.Bmr.IsAvailable, snapshot.Bmr.Detail),
            FormatTarget("VBM", snapshot.Vbm.IsAvailable, snapshot.Vbm.Detail),
            FormatTarget("CBT", snapshot.Cbt.IsAvailable, snapshot.Cbt.Detail),
        };
        if (snapshot.Daedalus != null)
            targets.Add(FormatTarget("Daedalus", snapshot.Daedalus.IsAvailable, snapshot.Daedalus.Detail));

        return string.Join("; ", targets);
    }

    private static string FormatTarget(string label, bool available, string detail)
        => available
            ? $"{label} captured"
            : $"{label} unavailable{(string.IsNullOrWhiteSpace(detail) ? string.Empty : $": {detail}")}";

    private static string BuildKey(string accountId, string characterKey)
        => $"{NormalizeAccountId(accountId)}|{NormalizeCharacterKey(characterKey)}";

    private static string NormalizeAccountId(string accountId)
        => string.IsNullOrWhiteSpace(accountId) ? "unknown-account" : accountId.Trim();

    private static string NormalizeCharacterKey(string characterKey)
        => string.IsNullOrWhiteSpace(characterKey) ? "default" : characterKey.Trim();

    private sealed record CommandAttempt(string Command, bool Success);
}

public sealed class DalamudExternalAutomationCommandSender : IExternalAutomationCommandSender
{
    public bool TrySendCommand(string command)
        => GameHelpers.SendChatCommand(command, "[ExternalCleanup]");
}

public sealed class BossModExternalAutomationSnapshotProvider : IExternalAutomationSnapshotProvider
{
    private const BindingFlags InstanceMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags StaticMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const string AiConfigType = "BossMod.AI.AIConfig";

    private static readonly BossModTarget[] Targets =
    {
        new("BossModReborn", "BMR"),
        new("BossMod", "VBM"),
    };

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;

    public BossModExternalAutomationSnapshotProvider(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
    }

    public ExternalAutomationSnapshot Capture(string accountId, string characterKey)
    {
        var bmr = CaptureBossMod(Targets[0]);
        var vbm = CaptureBossMod(Targets[1]);
        var cbt = CbtAutomationSnapshot.Unavailable("live AutoFollow read unavailable");
        return new ExternalAutomationSnapshot(accountId, characterKey, bmr, vbm, cbt, DateTimeOffset.UtcNow);
    }

    private BossModAutomationSnapshot CaptureBossMod(BossModTarget target)
    {
        try
        {
            var exposed = pluginInterface.InstalledPlugins.FirstOrDefault(plugin =>
                plugin.IsLoaded &&
                string.Equals(plugin.InternalName, target.InternalName, StringComparison.OrdinalIgnoreCase));
            if (exposed == null)
                return BossModAutomationSnapshot.Unavailable("not loaded");

            var liveAssembly = FindLivePluginAssembly(exposed, out var discoveryFailure);
            if (liveAssembly == null)
                return BossModAutomationSnapshot.Unavailable($"live instance discovery failed: {discoveryFailure}");

            var serviceType = liveAssembly.GetType("BossMod.Service");
            if (serviceType == null)
                return BossModAutomationSnapshot.Unavailable("BossMod.Service not found");

            var configRoot = GetStaticMember(serviceType, "Config");
            if (configRoot == null)
                return BossModAutomationSnapshot.Unavailable("BossMod.Service.Config unavailable");

            var configNode = FindConfigNode(configRoot);
            if (configNode == null)
                return BossModAutomationSnapshot.Unavailable($"{AiConfigType} not found");

            return new BossModAutomationSnapshot(
                true,
                TryGetAiActive(liveAssembly),
                TryGetBool(configNode, "ForbidMovement"),
                TryGetBool(configNode, "FollowOutOfCombat"),
                TryGetBool(configNode, "FollowDuringCombat"),
                TryGetBool(configNode, "FollowDuringActiveBossModule"),
                TryGetBool(configNode, "FollowTarget"),
                TryGetInt(configNode, "FollowSlot"),
                string.Empty);
        }
        catch (Exception ex)
        {
            var message = UnwrapMessage(ex);
            log.Warning($"[ExternalCleanup] Failed to capture {target.Label}: {message}");
            return BossModAutomationSnapshot.Unavailable(message);
        }
    }

    private static Assembly? FindLivePluginAssembly(IExposedPlugin exposed, out string failure)
    {
        _ = FindLivePluginInstance(exposed, out var assembly, out failure);
        return assembly;
    }

    internal static object? FindLivePluginInstance(
        IExposedPlugin exposed,
        out Assembly? assembly,
        out string failure)
    {
        assembly = null;
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var localPlugin = FindLocalPlugin(exposed, exposed.GetType().Assembly, depth: 4, visited);
        if (localPlugin == null)
        {
            failure = "Dalamud LocalPlugin wrapper was not reachable from IExposedPlugin";
            return null;
        }

        var instance = GetInstanceMember(localPlugin, "instance");
        if (instance == null)
        {
            failure = "Dalamud LocalPlugin had no live instance";
            return null;
        }

        if (GetInstanceMember(localPlugin, "Assembly") is not Assembly liveAssembly)
        {
            failure = "Dalamud LocalPlugin had no live assembly";
            return null;
        }

        if (!ReferenceEquals(instance.GetType().Assembly, liveAssembly))
        {
            failure = "Dalamud LocalPlugin instance and assembly did not match";
            return null;
        }

        assembly = liveAssembly;
        failure = string.Empty;
        return instance;
    }

    private static object? FindLocalPlugin(object root, Assembly dalamudAssembly, int depth, HashSet<object> visited)
    {
        if (IsLocalPlugin(root.GetType()))
            return root;

        if (depth <= 0 || !visited.Add(root))
            return null;

        foreach (var value in EnumerateMemberValues(root))
        {
            if (value == null || value.GetType().Assembly != dalamudAssembly)
                continue;

            var found = FindLocalPlugin(value, dalamudAssembly, depth - 1, visited);
            if (found != null)
                return found;
        }

        return null;
    }

    private static bool? TryGetAiActive(Assembly liveAssembly)
    {
        var aiManagerType = liveAssembly.GetType("BossMod.AI.AIManager");
        if (aiManagerType == null)
            return null;

        var instance = GetStaticMember(aiManagerType, "Instance");
        if (instance == null)
            return null;

        if (!TryGetInstanceMember(instance, "Beh", out var behavior))
            return null;

        return behavior != null;
    }

    private static bool IsLocalPlugin(Type type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (string.Equals(current.FullName, "Dalamud.Plugin.Internal.Types.LocalPlugin", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static IEnumerable<object?> EnumerateMemberValues(object root)
    {
        for (var type = root.GetType(); type != null; type = type.BaseType)
        {
            foreach (var field in type.GetFields(InstanceMembers | BindingFlags.DeclaredOnly))
            {
                object? value;
                try
                {
                    value = field.GetValue(root);
                }
                catch
                {
                    continue;
                }

                yield return value;
            }

            foreach (var property in type.GetProperties(InstanceMembers | BindingFlags.DeclaredOnly))
            {
                if (property.GetIndexParameters().Length != 0)
                    continue;

                object? value;
                try
                {
                    value = property.GetValue(root);
                }
                catch
                {
                    continue;
                }

                yield return value;
            }
        }
    }

    private static object? GetStaticMember(Type type, string name)
    {
        var property = type.GetProperty(name, StaticMembers);
        if (property != null)
            return property.GetValue(null);

        return type.GetField(name, StaticMembers)?.GetValue(null);
    }

    private static object? GetInstanceMember(object root, string name)
        => TryGetInstanceMember(root, name, out var value) ? value : null;

    private static bool TryGetInstanceMember(object root, string name, out object? value)
    {
        for (var type = root.GetType(); type != null; type = type.BaseType)
        {
            var property = type.GetProperty(name, InstanceMembers | BindingFlags.DeclaredOnly);
            if (property != null)
            {
                value = property.GetValue(root);
                return true;
            }

            var field = type.GetField(name, InstanceMembers | BindingFlags.DeclaredOnly);
            if (field != null)
            {
                value = field.GetValue(root);
                return true;
            }
        }

        value = null;
        return false;
    }

    private static object? FindConfigNode(object configRoot)
    {
        if (GetInstanceMember(configRoot, "Nodes") is not IEnumerable nodes)
            return null;

        foreach (var node in nodes)
        {
            var typeName = node?.GetType().FullName;
            if (string.Equals(typeName, AiConfigType, StringComparison.Ordinal))
                return node;
        }

        return null;
    }

    private static bool? TryGetBool(object root, string name)
        => GetInstanceMember(root, name) is bool value ? value : null;

    private static int? TryGetInt(object root, string name)
    {
        var value = GetInstanceMember(root, name);
        return value switch
        {
            int intValue => intValue,
            uint uintValue when uintValue <= int.MaxValue => (int)uintValue,
            _ => null,
        };
    }

    private static string UnwrapMessage(Exception ex)
    {
        while (ex is TargetInvocationException { InnerException: not null })
            ex = ex.InnerException;

        return ex.Message;
    }

    private sealed record BossModTarget(string InternalName, string Label);
}
