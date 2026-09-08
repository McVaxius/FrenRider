using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace FrenRider.Services;

public sealed class BossModActionTweaksService
{
    private const BindingFlags InstanceMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags StaticMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const string CurrentConfigType = "BossMod.ActionTweaksConfig";
    private const string LegacyConfigType = "BossMod.ActionManagerConfig";
    private const string SettingField = "PreventMovingWhileCasting";

    private static readonly BossModTarget[] Targets =
    {
        new("BossModReborn", "BMR"),
        new("BossMod", "VBM"),
    };

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private readonly AutorotIpcService autorotIpcService;
    private readonly Plugin plugin;
    private readonly CastingFailureRecovery recovery;
    private readonly HashSet<string> movementErrors = new(StringComparer.Ordinal);

    public BossModActionTweaksService(Plugin plugin)
    {
        this.plugin = plugin;
        pluginInterface = Plugin.PluginInterface;
        log = Plugin.Log;
        autorotIpcService = plugin.AutorotIpcService;
        recovery = new CastingFailureRecovery(ReadRecoverySettings, message => log.Warning(message));
        var sheet = Plugin.DataManager.GetExcelSheet<LogMessage>();
        foreach (var rowId in new uint[] { 562, 565, 566 })
        {
            var text = sheet.GetRowOrDefault(rowId)?.Text.ExtractText();
            if (!string.IsNullOrWhiteSpace(text))
                movementErrors.Add(text.Trim());
        }
    }

    public bool HasResult { get; private set; }
    public bool HasFailures { get; private set; }
    public bool HasNotLoadedTargets { get; private set; }
    public string StatusText { get; private set; } = string.Empty;

    public void ApplyDontMoveWhileCasting(bool enabled)
    {
        // The explicit checkbox choice supersedes the temporary recovery values.
        recovery.Reset(restore: false);
        var results = Targets
            .Select(target => Apply(target, enabled))
            .Append(ApplyRsr(enabled))
            .ToArray();

        HasResult = true;
        HasFailures = results.Any(result => result.Outcome == ApplyOutcome.Failed);
        HasNotLoadedTargets = results.Any(result =>
            result.Outcome is ApplyOutcome.NotLoaded or ApplyOutcome.Unavailable);
        StatusText = string.Join("; ", results.Select(result => $"{result.Label}: {FormatOutcome(result.Outcome)}"));

        foreach (var result in results.Where(result => result.Outcome == ApplyOutcome.Failed))
            log.Warning($"[BossMod ActionTweaks] {result.Label} failed: {result.Detail}");

        log.Information($"[BossMod ActionTweaks] PreventMovingWhileCasting={enabled}: {StatusText}");
    }

    internal void OnErrorToast(ref SeString message, ref bool isHandled)
    {
        if (movementErrors.Contains(message.TextValue.Trim()))
            recovery.OnError(ReadRecoveryContext(refreshOwnership: true), Environment.TickCount64);
    }

    public void UpdateRecovery()
    {
        if (recovery.HasPendingWork)
            recovery.Update(ReadRecoveryContext(), Environment.TickCount64);
    }

    public void ResetRecovery() => recovery.Reset();

    private CastingRecoveryContext ReadRecoveryContext(bool refreshOwnership = false)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        var target = Plugin.TargetManager.Target as IBattleNpc;
        var inDuty = Plugin.Condition[ConditionFlag.BoundByDuty]
            || Plugin.Condition[ConditionFlag.BoundByDuty56]
            || Plugin.Condition[ConditionFlag.BoundByDuty95];
        var ads = plugin.AdsIntegrationService;
        var ownership = plugin.AdsDutyIpcService.Current;
        if (refreshOwnership)
        {
            var identity = AdsIntegrationService.ReadLiveDutyIdentity();
            ownership = plugin.AdsDutyIpcService.Refresh(inDuty, identity.TerritoryTypeId,
                identity.ContentFinderConditionId, force: true);
        }
        var questionable = plugin.QuestionableIpcService.Refresh(force: refreshOwnership);
        var playerReady = Plugin.ClientState.IsLoggedIn && player?.CurrentHp > 0
            && !Plugin.Condition[ConditionFlag.Unconscious]
            && !Plugin.Condition[ConditionFlag.BetweenAreas]
            && !Plugin.Condition[ConditionFlag.BetweenAreas51]
            && !Plugin.Condition[ConditionFlag.WatchingCutscene]
            && !Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent];
        var livingHostile = target is { IsTargetable: true, CurrentHp: > 0 }
            && target.StatusFlags.HasFlag(StatusFlags.Hostile);
        return new CastingRecoveryContext(
            Plugin.PlayerState.ContentId,
            Plugin.ClientState.TerritoryType,
            livingHostile ? target!.GameObjectId : 0,
            player?.Position ?? default,
            playerReady && plugin.ConfigManager.GetActiveConfig().Enabled,
            Plugin.Condition[ConditionFlag.InCombat],
            inDuty,
            ads.IsControllingDuty || ownership.IsOwned,
            ads.IsHandoffPending,
            ownership.StatusReadable,
            plugin.AdsUtilityIpcService.Refresh(force: refreshOwnership).UtilityRunning
                || plugin.AutomationService.IsUtilityGateActive,
            plugin.AdsHyperFocusLeaseService.IsLeaseActive,
            questionable.IsRunning || plugin.CombatService.IsQuestionableSoloAuthorityActive
                || (!questionable.StatusReadable && plugin.QuestionableIpcService.WasRunningWithin(QuestionableIpcService.RecentRunningHold)),
            plugin.CoppeliaPowerlevelLeaseService.IsLeaseActive);
    }

    private IEnumerable<CastingMovementSetting> ReadRecoverySettings()
    {
        foreach (var target in Targets)
        {
            var setting = ReadRecoverySetting(target.InternalName, target.Label, rsr: false);
            if (setting != null)
                yield return setting;
        }
        var rsr = ReadRecoverySetting("RotationSolver", "RSR", rsr: true);
        if (rsr != null)
            yield return rsr;
    }

    private CastingMovementSetting? ReadRecoverySetting(string internalName, string label, bool rsr)
    {
        try
        {
            var exposed = pluginInterface.InstalledPlugins.FirstOrDefault(p => p.IsLoaded
                && string.Equals(p.InternalName, internalName, StringComparison.OrdinalIgnoreCase));
            if (exposed == null)
                return null;
            var instance = BossModExternalAutomationSnapshotProvider.FindLivePluginInstance(exposed, out var assembly, out _);
            if (instance == null || assembly == null)
                return null;

            if (rsr)
            {
                // Resolve Basic from this live plugin's load context; never load another copy.
                var basic = AssemblyLoadContext.GetLoadContext(assembly)?.Assemblies
                    .FirstOrDefault(a => a.GetName().Name == "RotationSolver.Basic");
                var service = basic?.GetType("RotationSolver.Basic.Service");
                var config = service == null ? null : GetStaticMember(service, "Config");
                var poslock = config == null ? null : GetInstanceMember(config, "PoslockCasting");
                var value = poslock?.GetType().GetProperty("Value", InstanceMembers);
                if (value?.PropertyType != typeof(bool) || !value.CanRead || !value.CanWrite)
                    return null;
                return new(label, () => value.GetValue(poslock) as bool?, enabled => value.SetValue(poslock, enabled));
            }

            var bossModService = assembly.GetType("BossMod.Service");
            var root = bossModService == null ? null : GetStaticMember(bossModService, "Config");
            var node = root == null ? null : FindConfigNode(root);
            var field = node?.GetType().GetField(SettingField, InstanceMembers);
            if (field?.FieldType != typeof(bool) || field.IsInitOnly)
                return null;
            // Temporary writes intentionally do not notify the config persistence event.
            return new(label, () => field.GetValue(node) as bool?, enabled => field.SetValue(node, enabled));
        }
        catch (Exception ex)
        {
            log.Debug($"[Casting recovery] {label} setting unreadable: {UnwrapMessage(ex)}");
            return null;
        }
    }

    private ApplyResult Apply(BossModTarget target, bool enabled)
    {
        try
        {
            var exposed = pluginInterface.InstalledPlugins.FirstOrDefault(plugin =>
                plugin.IsLoaded
                && string.Equals(plugin.InternalName, target.InternalName, StringComparison.OrdinalIgnoreCase));
            if (exposed == null)
                return new(target.Label, ApplyOutcome.NotLoaded, string.Empty);

            var liveAssembly = FindLivePluginAssembly(exposed, out var discoveryFailure);
            if (liveAssembly == null)
                return new(target.Label, ApplyOutcome.Failed, $"plugin is loaded, but live instance discovery failed: {discoveryFailure}");

            var serviceType = liveAssembly.GetType("BossMod.Service");
            if (serviceType == null)
                return new(target.Label, ApplyOutcome.Failed, "BossMod.Service was not found in the live plugin assembly");

            var configRoot = GetStaticMember(serviceType, "Config");
            if (configRoot == null)
                return new(target.Label, ApplyOutcome.Failed, "BossMod.Service.Config was not available");

            var configNode = FindConfigNode(configRoot);
            if (configNode == null)
                return new(target.Label, ApplyOutcome.Failed, $"{CurrentConfigType} or {LegacyConfigType} was not found");

            var setting = configNode.GetType().GetField(SettingField, InstanceMembers);
            if (setting?.FieldType != typeof(bool))
                return new(target.Label, ApplyOutcome.Failed, $"{configNode.GetType().FullName}.{SettingField} was not a bool field");

            if (setting.GetValue(configNode) is not bool current)
                return new(target.Label, ApplyOutcome.Failed, $"{SettingField} could not be read");

            if (current == enabled)
                return new(target.Label, ApplyOutcome.AlreadySet, string.Empty);

            setting.SetValue(configNode, enabled);
            if (setting.GetValue(configNode) is not bool readBack || readBack != enabled)
                return new(target.Label, ApplyOutcome.Failed, $"{SettingField} read-back did not match");

            NotifyModified(configNode);
            return new(target.Label, ApplyOutcome.Applied, string.Empty);
        }
        catch (Exception ex)
        {
            return new(target.Label, ApplyOutcome.Failed, UnwrapMessage(ex));
        }
    }

    private ApplyResult ApplyRsr(bool enabled)
        => autorotIpcService.TrySetRsrPoslockCasting(enabled)
            ? new("RSR", ApplyOutcome.Applied, string.Empty)
            : new("RSR", ApplyOutcome.Unavailable, string.Empty);

    // Modern BossMod entrypoints may use IAsyncDalamudPlugin or HostedPlugin, so resolve through Dalamud's live wrapper.
    private static Assembly? FindLivePluginAssembly(IExposedPlugin exposed, out string failure)
    {
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

        if (GetInstanceMember(localPlugin, "Assembly") is not Assembly assembly)
        {
            failure = "Dalamud LocalPlugin had no live assembly";
            return null;
        }

        if (!ReferenceEquals(instance.GetType().Assembly, assembly))
        {
            failure = "Dalamud LocalPlugin instance and assembly did not match";
            return null;
        }

        if (assembly.GetType("BossMod.Service") == null)
        {
            failure = "BossMod.Service was not found in the live plugin assembly";
            return null;
        }

        failure = string.Empty;
        return assembly;
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
    {
        for (var type = root.GetType(); type != null; type = type.BaseType)
        {
            var property = type.GetProperty(name, InstanceMembers | BindingFlags.DeclaredOnly);
            if (property != null)
                return property.GetValue(root);

            var field = type.GetField(name, InstanceMembers | BindingFlags.DeclaredOnly);
            if (field != null)
                return field.GetValue(root);
        }

        return null;
    }

    private static object? FindConfigNode(object configRoot)
    {
        if (GetInstanceMember(configRoot, "Nodes") is not IEnumerable nodes)
            return null;

        object? legacy = null;
        foreach (var node in nodes)
        {
            var typeName = node?.GetType().FullName;
            if (string.Equals(typeName, CurrentConfigType, StringComparison.Ordinal))
                return node;

            if (string.Equals(typeName, LegacyConfigType, StringComparison.Ordinal))
                legacy = node;
        }

        return legacy;
    }

    private static void NotifyModified(object configNode)
    {
        var modified = GetInstanceMember(configNode, "Modified");
        var fire = modified?.GetType().GetMethod("Fire", InstanceMembers, Type.EmptyTypes);
        if (fire != null)
        {
            fire.Invoke(modified, null);
            return;
        }

        var notifyModified = configNode.GetType().GetMethod("NotifyModified", InstanceMembers, Type.EmptyTypes);
        if (notifyModified != null)
        {
            notifyModified.Invoke(configNode, null);
            return;
        }

        throw new MissingMethodException("Neither Modified.Fire() nor NotifyModified() was available");
    }

    private static string FormatOutcome(ApplyOutcome outcome)
        => outcome switch
        {
            ApplyOutcome.Applied => "applied",
            ApplyOutcome.AlreadySet => "already set",
            ApplyOutcome.NotLoaded => "not loaded",
            ApplyOutcome.Unavailable => "unavailable",
            _ => "failed",
        };

    private static string UnwrapMessage(Exception ex)
    {
        while (ex is TargetInvocationException { InnerException: not null })
            ex = ex.InnerException;

        return ex.Message;
    }

    private sealed record BossModTarget(string InternalName, string Label);
    private sealed record ApplyResult(string Label, ApplyOutcome Outcome, string Detail);

    private enum ApplyOutcome
    {
        Applied,
        AlreadySet,
        NotLoaded,
        Unavailable,
        Failed,
    }
}

internal readonly record struct CastingRecoveryContext(
    ulong CharacterId,
    uint TerritoryId,
    ulong TargetId,
    Vector3 Position,
    bool EnabledAndReady,
    bool InCombat,
    bool InDuty = false,
    bool AdsOwned = false,
    bool HandoffPending = false,
    bool AdsOwnershipReadable = true,
    bool UtilityActive = false,
    bool HyperFocusActive = false,
    bool QuestionableControlled = false,
    bool CoppeliaControlled = false)
{
    public bool HasControl => EnabledAndReady && !AdsOwned && !HandoffPending
        && (!InDuty || AdsOwnershipReadable) && !UtilityActive && !HyperFocusActive
        && !QuestionableControlled && !CoppeliaControlled;
}

internal sealed record CastingMovementSetting(string Label, Func<bool?> Read, Action<bool> Write);

internal sealed class CastingFailureRecovery(
    Func<IEnumerable<CastingMovementSetting>> readSettings,
    Action<string> warn)
{
    private const long WindowMs = 5000;
    private readonly Queue<long> errors = new();
    private readonly List<CastingMovementSetting> changed = new();
    private CastingRecoveryContext tracked;
    private long? restoreAt;

    internal bool IsRecovering => restoreAt.HasValue;
    internal bool HasPendingWork => IsRecovering || errors.Count > 0;

    internal void Update(CastingRecoveryContext context, long now)
    {
        if (!context.HasControl || context.InCombat
            || context.CharacterId != tracked.CharacterId || context.TerritoryId != tracked.TerritoryId)
        {
            Reset();
            tracked = context;
            return;
        }

        if (restoreAt.HasValue)
        {
            if (now >= restoreAt.Value)
            {
                Reset();
                tracked = context;
            }
            return;
        }

        if (context.TargetId == 0 || context.TargetId != tracked.TargetId
            || Vector3.DistanceSquared(context.Position, tracked.Position) > 0.25f)
        {
            errors.Clear();
            tracked = context;
        }
        while (errors.TryPeek(out var first) && now - first > WindowMs)
            errors.Dequeue();
        if (errors.Count == 0)
            tracked = context;
    }

    internal void OnError(CastingRecoveryContext context, long now)
    {
        Update(context, now);
        if (IsRecovering || !context.HasControl || context.InCombat || context.TargetId == 0)
            return;

        errors.Enqueue(now);
        if (errors.Count < 3)
            return;

        errors.Clear();
        restoreAt = now + WindowMs;
        foreach (var setting in readSettings())
        {
            try
            {
                if (setting.Read() != true)
                    continue;
                // Keep restoration even if a setter throws after changing its value.
                changed.Add(setting);
                setting.Write(false);
                if (setting.Read() != false)
                    warn($"[Casting recovery] {setting.Label} did not confirm the temporary unlock.");
            }
            catch (Exception ex)
            {
                warn($"[Casting recovery] {setting.Label} unlock failed: {ex.Message}");
            }
        }
    }

    internal void Reset(bool restore = true)
    {
        if (restore)
        {
            foreach (var setting in changed)
            {
                try
                {
                    setting.Write(true);
                    if (setting.Read() != true)
                        warn($"[Casting recovery] {setting.Label} did not confirm restoration.");
                }
                catch (Exception ex)
                {
                    warn($"[Casting recovery] {setting.Label} restoration failed: {ex.Message}");
                }
            }
        }
        changed.Clear();
        restoreAt = null;
        errors.Clear();
        tracked = default;
    }
}
