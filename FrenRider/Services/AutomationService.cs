using System;
using Dalamud.Game.ClientState.Conditions;
using FrenRider.Models;

namespace FrenRider.Services;

public class AutomationService
{
    private readonly Plugin plugin;
    private readonly FrenTracker tracker;
    private readonly ZoneService zoneService;

    private int idleTickCounter;
    private long lastIdleActionMs;
    private long lastFoodCheckMs;
    private int idleListIndex;

    private static readonly string[] DefaultIdleList = new[]
    {
        "/tomescroll",
        "/doze",
        "/sit",
        "/think",
        "/lookout",
        "/stretch",
        "/box",
        "/pushups",
    };

    public string LastIdleAction { get; private set; } = "";
    public bool IsIdle { get; private set; }

    public AutomationService(Plugin plugin, FrenTracker tracker, ZoneService zoneService)
    {
        this.plugin = plugin;
        this.tracker = tracker;
        this.zoneService = zoneService;
    }

    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        if (!config.Enabled)
        {
            idleTickCounter = 0;
            IsIdle = false;
            return;
        }

        // Zone transition reset
        if (zoneService.ZoneChanged)
        {
            idleTickCounter = 0;
            IsIdle = false;
            LastIdleAction = "";
            return;
        }

        var inCombat = Plugin.Condition[ConditionFlag.InCombat];
        var mounted = Plugin.Condition[ConditionFlag.Mounted];
        var now = Environment.TickCount64;

        // Don't idle if in combat or mounted
        if (inCombat || mounted)
        {
            idleTickCounter = 0;
            IsIdle = false;
            return;
        }

        // Check if following is idle (in range of fren, not moving)
        var follow = plugin.FollowService;
        if (follow.State == FollowState.InRange)
        {
            idleTickCounter++;

            if (idleTickCounter >= config.IdleTicksBeforeAction)
            {
                IsIdle = true;

                // Throttle idle actions to every 30 seconds minimum
                if (now - lastIdleActionMs > 30000)
                {
                    lastIdleActionMs = now;
                    PerformIdleAction(config);
                }
            }
        }
        else
        {
            idleTickCounter = 0;
            IsIdle = false;
        }

        // Food consumption check (every 60 seconds)
        if (now - lastFoodCheckMs > 60000 && !inCombat)
        {
            lastFoodCheckMs = now;
            CheckFood(config);
        }
    }

    private void PerformIdleAction(CharacterConfig config)
    {
        string action;

        switch (config.IdleActionMode)
        {
            case 0: // Specific action
                action = config.IdleAction;
                break;
            case 1: // Action from list
                var list = config.IdleListMode == 1 && config.CustomIdleList.Length > 0
                    ? config.CustomIdleList
                    : DefaultIdleList;

                if (list.Length == 0) return;
                action = list[idleListIndex % list.Length];
                idleListIndex++;
                break;
            default:
                return;
        }

        if (string.IsNullOrWhiteSpace(action)) return;

        LastIdleAction = action;
        SendCommand(action);
        Plugin.Log.Information($"Idle action: {action}");
    }

    private void CheckFood(CharacterConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.FeedMeItem)) return;

        // Check if food buff is active (Well Fed status ID = 48)
        // This is a stub — actual implementation needs status checking via FFXIVClientStructs
        // Future: check player's status list for Well Fed buff
        // If missing, send /item "FoodName" command
    }

    /// <summary>
    /// Trigger repair based on config (0=No, 1=Self, 2=Inn NPC).
    /// </summary>
    public void TriggerRepair(CharacterConfig config)
    {
        switch (config.Repair)
        {
            case 1: // Self repair
                SendCommand("/generalaction \"Repair\"");
                Plugin.Log.Information("Triggered self repair");
                break;
            case 2: // NPC repair (would need to interact with mender NPC)
                Plugin.Log.Information("NPC repair not yet implemented");
                break;
        }
    }

    private static void SendCommand(string command)
    {
        try
        {
            if (!Plugin.CommandManager.ProcessCommand(command))
                Plugin.Log.Warning($"Automation command not handled: {command}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Automation command failed [{command}]: {ex.Message}");
        }
    }
}
