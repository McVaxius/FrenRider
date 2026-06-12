using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

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

    public BossModActionTweaksService(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
    }

    public bool HasResult { get; private set; }
    public bool HasFailures { get; private set; }
    public bool HasNotLoadedTargets { get; private set; }
    public string StatusText { get; private set; } = string.Empty;

    public void ApplyDontMoveWhileCasting(bool enabled)
    {
        var results = Targets.Select(target => Apply(target, enabled)).ToArray();

        HasResult = true;
        HasFailures = results.Any(result => result.Outcome == ApplyOutcome.Failed);
        HasNotLoadedTargets = results.Any(result => result.Outcome == ApplyOutcome.NotLoaded);
        StatusText = string.Join("; ", results.Select(result => $"{result.Target.Label}: {FormatOutcome(result.Outcome)}"));

        foreach (var result in results.Where(result => result.Outcome == ApplyOutcome.Failed))
            log.Warning($"[BossMod ActionTweaks] {result.Target.Label} failed: {result.Detail}");

        log.Information($"[BossMod ActionTweaks] PreventMovingWhileCasting={enabled}: {StatusText}");
    }

    private ApplyResult Apply(BossModTarget target, bool enabled)
    {
        try
        {
            var exposed = pluginInterface.InstalledPlugins.FirstOrDefault(plugin =>
                plugin.IsLoaded
                && string.Equals(plugin.InternalName, target.InternalName, StringComparison.OrdinalIgnoreCase));
            if (exposed == null)
                return new(target, ApplyOutcome.NotLoaded, string.Empty);

            var liveAssembly = FindLivePluginAssembly(exposed, out var discoveryFailure);
            if (liveAssembly == null)
                return new(target, ApplyOutcome.Failed, $"plugin is loaded, but live instance discovery failed: {discoveryFailure}");

            var serviceType = liveAssembly.GetType("BossMod.Service");
            if (serviceType == null)
                return new(target, ApplyOutcome.Failed, "BossMod.Service was not found in the live plugin assembly");

            var configRoot = GetStaticMember(serviceType, "Config");
            if (configRoot == null)
                return new(target, ApplyOutcome.Failed, "BossMod.Service.Config was not available");

            var configNode = FindConfigNode(configRoot);
            if (configNode == null)
                return new(target, ApplyOutcome.Failed, $"{CurrentConfigType} or {LegacyConfigType} was not found");

            var setting = configNode.GetType().GetField(SettingField, InstanceMembers);
            if (setting?.FieldType != typeof(bool))
                return new(target, ApplyOutcome.Failed, $"{configNode.GetType().FullName}.{SettingField} was not a bool field");

            if (setting.GetValue(configNode) is not bool current)
                return new(target, ApplyOutcome.Failed, $"{SettingField} could not be read");

            if (current == enabled)
                return new(target, ApplyOutcome.AlreadySet, string.Empty);

            setting.SetValue(configNode, enabled);
            if (setting.GetValue(configNode) is not bool readBack || readBack != enabled)
                return new(target, ApplyOutcome.Failed, $"{SettingField} read-back did not match");

            NotifyModified(configNode);
            return new(target, ApplyOutcome.Applied, string.Empty);
        }
        catch (Exception ex)
        {
            return new(target, ApplyOutcome.Failed, UnwrapMessage(ex));
        }
    }

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
            _ => "failed",
        };

    private static string UnwrapMessage(Exception ex)
    {
        while (ex is TargetInvocationException { InnerException: not null })
            ex = ex.InnerException;

        return ex.Message;
    }

    private sealed record BossModTarget(string InternalName, string Label);
    private sealed record ApplyResult(BossModTarget Target, ApplyOutcome Outcome, string Detail);

    private enum ApplyOutcome
    {
        Applied,
        AlreadySet,
        NotLoaded,
        Failed,
    }
}
