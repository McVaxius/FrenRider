using System;
using System.Linq;
using System.Reflection;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FrenRider.Models;

namespace FrenRider.Services;

public sealed class DaedalusTargetModeService
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ITargetManager targetManager;
    private readonly IToastGui toastGui;
    private readonly IPluginLog log;

    public DaedalusTargetModeService(
        IDalamudPluginInterface pluginInterface,
        ITargetManager targetManager,
        IToastGui toastGui,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.targetManager = targetManager;
        this.toastGui = toastGui;
        this.log = log;
    }

    public bool Apply(DaedalusTargetMode mode, bool notifyUser)
    {
        if (!Enum.IsDefined(mode))
            return Fail($"Daedalus engage mode value {(int)mode} is invalid.", notifyUser);

        ulong? focusTargetId = null;
        if (mode == DaedalusTargetMode.Focus)
        {
            var target = targetManager.Target;
            if (target is not IBattleNpc battleNpc ||
                !TryResolveFocusTargetId(
                    battleNpc.GameObjectId,
                    battleNpc.BattleNpcKind == BattleNpcSubKind.Combatant,
                    battleNpc.IsTargetable,
                    battleNpc.CurrentHp,
                    out var resolvedTargetId))
            {
                return Fail(
                    "Daedalus Focus needs a living enemy hard target; the saved mode was not applied.",
                    notifyUser);
            }

            focusTargetId = resolvedTargetId;
        }

        try
        {
            var exposed = pluginInterface.InstalledPlugins.FirstOrDefault(plugin =>
                plugin.IsLoaded &&
                string.Equals(plugin.InternalName, "Daedalus", StringComparison.OrdinalIgnoreCase));
            if (exposed == null)
                return Fail("Daedalus is not loaded.", notifyUser);

            var instance = BossModExternalAutomationSnapshotProvider.FindLivePluginInstance(
                exposed,
                out _,
                out var discoveryFailure);
            if (instance == null)
                return Fail($"Daedalus live instance discovery failed: {discoveryFailure}", notifyUser);

            if (!TryBroadcastTargetMode(instance, mode, focusTargetId, out var reflectionFailure))
                return Fail(reflectionFailure, notifyUser);

            log.Information($"[FrenRider] Applied Daedalus engage mode {mode}.");
            return true;
        }
        catch (Exception ex)
        {
            return Fail($"Daedalus engage-mode reflection failed: {UnwrapMessage(ex)}", notifyUser);
        }
    }

    internal static bool IsEffectiveRotation(int normalPlugin, int forayPlugin, bool isForay)
        => (isForay ? forayPlugin : normalPlugin) == 4;

    internal static bool TryResolveFocusTargetId(
        ulong objectId,
        bool isEnemy,
        bool isTargetable,
        uint currentHp,
        out ulong focusTargetId)
    {
        focusTargetId = 0;
        if (objectId == 0 || !isEnemy || !isTargetable || currentHp == 0)
            return false;

        focusTargetId = objectId;
        return true;
    }

    internal static bool TryBroadcastTargetMode(
        object pluginInstance,
        DaedalusTargetMode mode,
        ulong? focusTargetId,
        out string failure)
    {
        try
        {
            if (!Enum.IsDefined(mode))
            {
                failure = $"Daedalus engage mode value {(int)mode} is invalid.";
                return false;
            }

            var coordinationBus = GetInstanceMember(pluginInstance, "coordinationBus");
            if (coordinationBus == null)
            {
                failure = "Daedalus LAN coordination is disabled or coordinationBus is unavailable.";
                return false;
            }

            if (GetInstanceMember(coordinationBus, "FocusTargetId") is not ulong savedFocusTargetId)
            {
                failure = "Daedalus CoordinationBus.FocusTargetId is unavailable.";
                return false;
            }

            if (GetInstanceMember(coordinationBus, "OffTankSenderId") is not string offTankSenderId)
            {
                failure = "Daedalus CoordinationBus.OffTankSenderId is unavailable.";
                return false;
            }

            if (mode == DaedalusTargetMode.Focus && focusTargetId is not > 0)
            {
                failure = "Daedalus Focus needs a living enemy hard target.";
                return false;
            }

            var method = coordinationBus.GetType()
                .GetMethods(InstanceMembers)
                .FirstOrDefault(candidate =>
                {
                    if (!string.Equals(candidate.Name, "BroadcastTargetMode", StringComparison.Ordinal))
                        return false;

                    var parameters = candidate.GetParameters();
                    return parameters.Length == 3
                        && IsTargetModeEnum(parameters[0].ParameterType)
                        && parameters[1].ParameterType == typeof(ulong)
                        && parameters[2].ParameterType == typeof(string);
                });
            if (method == null)
            {
                failure = "Daedalus CoordinationBus.BroadcastTargetMode is unavailable.";
                return false;
            }

            var parameters = method.GetParameters();
            var daedalusMode = Enum.ToObject(parameters[0].ParameterType, (int)mode);
            var targetId = mode == DaedalusTargetMode.Focus
                ? focusTargetId!.Value
                : savedFocusTargetId;
            method.Invoke(coordinationBus, new object[] { daedalusMode, targetId, offTankSenderId });

            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = $"Daedalus engage-mode reflection failed: {UnwrapMessage(ex)}";
            return false;
        }
    }

    private bool Fail(string message, bool notifyUser)
    {
        log.Warning($"[FrenRider] {message}");
        if (notifyUser)
        {
            try
            {
                toastGui.ShowError($"Fren Rider: {message}");
            }
            catch (Exception ex)
            {
                log.Debug($"[FrenRider] Failed to show Daedalus error toast: {ex.Message}");
            }
        }
        return false;
    }

    private static bool IsTargetModeEnum(Type type)
        => type.IsEnum
            && string.Equals(Enum.GetName(type, 0), "None", StringComparison.Ordinal)
            && string.Equals(Enum.GetName(type, 1), "Focus", StringComparison.Ordinal)
            && string.Equals(Enum.GetName(type, 2), "Split", StringComparison.Ordinal)
            && string.Equals(Enum.GetName(type, 3), "KillAdds", StringComparison.Ordinal);

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

    private static string UnwrapMessage(Exception ex)
    {
        while (ex is TargetInvocationException { InnerException: not null })
            ex = ex.InnerException;

        return ex.Message;
    }
}
