using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FrenRider.Services;

internal sealed class VNavStateService
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private bool runningFailureLogged;
    private bool pathfindFailureLogged;

    public VNavStateService(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
    }

    public bool TryGetState(out bool pathRunning, out bool pathfindInProgress)
    {
        var runningKnown = TryReadAny(
            "path running",
            out pathRunning,
            ref runningFailureLogged,
            "vnavmesh.Path.IsRunning",
            "NavmeshManager.IsRunning");

        var pathfindKnown = TryReadAny(
            "pathfind in-progress",
            out pathfindInProgress,
            ref pathfindFailureLogged,
            "vnavmesh.Nav.PathfindInProgress",
            "vnavmesh.SimpleMove.PathfindInProgress");

        return runningKnown && pathfindKnown;
    }

    private bool TryReadAny(string stateName, out bool value, ref bool failureLogged, params string[] ipcNames)
    {
        value = false;
        var anySucceeded = false;
        Exception? lastException = null;

        foreach (var ipcName in ipcNames)
        {
            try
            {
                var current = pluginInterface.GetIpcSubscriber<bool>(ipcName).InvokeFunc();
                anySucceeded = true;
                value |= current;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        if (anySucceeded)
            return true;

        if (!failureLogged)
        {
            failureLogged = true;
            var detail = lastException == null
                ? "unknown error"
                : $"{lastException.GetType().Name}: {lastException.Message}";
            log.Debug($"[FR][VNavState] Could not read vnavmesh {stateName} state via IPC: {detail}");
        }

        return false;
    }
}
