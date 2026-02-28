using System;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using FrenRider.Models;

namespace FrenRider.Services;

public enum FollowState
{
    Idle,       // Not following (disabled, no fren, etc.)
    Following,  // Actively navigating to fren
    InRange,    // Within cling distance, stopped
    TooFar,     // Beyond max distance, stopped
    InCombat,   // In combat, follow paused based on config
}

public class FollowService
{
    private readonly Plugin plugin;
    private readonly FrenTracker tracker;
    private readonly ZoneService zoneService;

    private Vector3 lastNavTarget;
    private bool isNavigating;
    private Vector3 socialOffset;
    private long lastOffsetChangeMs;

    private static readonly string[] ClingTypeNames = { "NavMesh", "Visland", "BossMod Follow", "Vanilla Follow" };

    public FollowState State { get; private set; } = FollowState.Idle;
    public string StateDetail { get; private set; } = "";

    public FollowService(Plugin plugin, FrenTracker tracker, ZoneService zoneService)
    {
        this.plugin = plugin;
        this.tracker = tracker;
        this.zoneService = zoneService;
    }

    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();

        // Zone transition: stop navigation and reset
        if (zoneService.ZoneChanged)
        {
            if (isNavigating) StopNavigation(config);
            State = FollowState.Idle;
            StateDetail = "Zone transition";
            lastNavTarget = default;
            socialOffset = default;
            return;
        }

        if (!config.Enabled)
        {
            if (isNavigating) StopNavigation(config);
            State = FollowState.Idle;
            StateDetail = "Disabled";
            return;
        }

        var fren = tracker.Fren;
        if (fren == null || !fren.IsFound)
        {
            if (isNavigating) StopNavigation(config);
            State = FollowState.Idle;
            StateDetail = "No fren found";
            return;
        }

        if (!fren.IsVisible)
        {
            if (isNavigating) StopNavigation(config);
            State = FollowState.Idle;
            StateDetail = "Fren not visible";
            return;
        }

        // Combat check
        if (Plugin.Condition[ConditionFlag.InCombat])
        {
            // FollowInCombat: 0=No, 1=Yes, 2=Auto
            if (config.FollowInCombat == 0)
            {
                if (isNavigating) StopNavigation(config);
                State = FollowState.InCombat;
                StateDetail = "Paused (in combat)";
                return;
            }
            // 1=Yes or 2=Auto: continue following
        }

        var distance = fren.Distance;
        var maxDist = GetMaxDistance(config);
        var clingDist = GetEffectiveClingDistance(config);

        // Too far — stop
        if (distance > maxDist)
        {
            if (isNavigating) StopNavigation(config);
            State = FollowState.TooFar;
            StateDetail = $"Too far ({distance:F1}y > {maxDist:F0}y max)";
            return;
        }

        // In range — stop
        if (distance <= clingDist)
        {
            if (isNavigating) StopNavigation(config);
            State = FollowState.InRange;
            StateDetail = $"In range ({distance:F1}y)";
            return;
        }

        // Formation mode: override target with formation position
        var formationTarget = plugin.FormationService.GetFormationTarget();
        if (formationTarget.HasValue)
        {
            var localPlayer = Plugin.ObjectTable.LocalPlayer;
            if (localPlayer != null)
            {
                var formDist = Vector3.Distance(localPlayer.Position, formationTarget.Value);
                if (formDist <= 1.5f)
                {
                    if (isNavigating) StopNavigation(config);
                    State = FollowState.InRange;
                    StateDetail = $"Formation slot {plugin.FormationService.AssignedSlot} ({formDist:F1}y)";
                    return;
                }

                State = FollowState.Following;
                StateDetail = $"Formation slot {plugin.FormationService.AssignedSlot} ({formDist:F1}y)";
                NavigateToPosition(config, formationTarget.Value);
                return;
            }
        }

        // Follow
        State = FollowState.Following;
        StateDetail = $"Following ({distance:F1}y, cling {clingDist:F1}y)";
        NavigateToFren(config, fren);
    }

    private float GetEffectiveClingDistance(CharacterConfig config)
    {
        var cling = config.Cling;

        if (zoneService.CurrentZone == ZoneType.DeepDungeon)
            cling += config.DDDistance;

        // FATE extra distance
        if (zoneService.InFate && config.FDistance > 0)
            cling += config.FDistance;

        // Add social distancing offset so we stop farther away
        if (ShouldApplySocialDistancing(config))
            cling = Math.Max(cling, config.SocialDistancing);

        return cling;
    }

    private float GetMaxDistance(CharacterConfig config)
    {
        return zoneService.CurrentZone == ZoneType.Foray
            ? config.MaxBistanceForay
            : config.MaxBistance;
    }

    private void NavigateToFren(CharacterConfig config, FrenTracker.FrenState fren)
    {
        var target = fren.Position;

        // Apply social distancing offset
        if (ShouldApplySocialDistancing(config))
            target = ApplySocialDistancing(config, target);

        NavigateToPosition(config, target);
    }

    private void NavigateToPosition(CharacterConfig config, Vector3 target)
    {
        // Only re-issue nav command if target moved significantly
        if (Vector3.Distance(target, lastNavTarget) < 1.0f && isNavigating)
            return;

        lastNavTarget = target;
        isNavigating = true;

        var clingType = zoneService.CurrentZone == ZoneType.Duty
            ? config.ClingTypeDuty
            : config.ClingType;

        SendNavigationCommand(clingType, target);
    }

    private bool ShouldApplySocialDistancing(CharacterConfig config)
    {
        if (config.SocialDistancing <= 0) return false;
        if (zoneService.IsIndoors && config.SocialDistancingIndoors == 0) return false;
        return true;
    }

    private Vector3 ApplySocialDistancing(CharacterConfig config, Vector3 target)
    {
        // Regenerate offset periodically (not every tick) for natural movement
        var now = Environment.TickCount64;
        if (now - lastOffsetChangeMs > 5000 || socialOffset == Vector3.Zero)
        {
            lastOffsetChangeMs = now;
            var rng = new Random();
            socialOffset = new Vector3(
                (float)(rng.NextDouble() * 2 - 1) * config.SocialDistanceXWiggle,
                0,
                (float)(rng.NextDouble() * 2 - 1) * config.SocialDistanceZWiggle
            );
        }

        return new Vector3(target.X + socialOffset.X, target.Y, target.Z + socialOffset.Z);
    }

    private void SendNavigationCommand(int clingType, Vector3 target)
    {
        var typeName = clingType >= 0 && clingType < ClingTypeNames.Length
            ? ClingTypeNames[clingType]
            : "NavMesh";

        var cmd = typeName switch
        {
            "NavMesh" => $"/vnav moveto {target.X:F2} {target.Y:F2} {target.Z:F2}",
            "Visland" => $"/visland moveto {target.X:F2} {target.Y:F2} {target.Z:F2}",
            "BossMod Follow" => "/bmr follow",
            "Vanilla Follow" => "/follow",
            _ => null,
        };

        if (cmd != null)
            SendCommand(cmd);
    }

    private void StopNavigation(CharacterConfig config)
    {
        if (!isNavigating) return;
        isNavigating = false;

        var clingType = zoneService.CurrentZone == ZoneType.Duty
            ? config.ClingTypeDuty
            : config.ClingType;

        var typeName = clingType >= 0 && clingType < ClingTypeNames.Length
            ? ClingTypeNames[clingType]
            : "NavMesh";

        var cmd = typeName switch
        {
            "NavMesh" => "/vnav stop",
            "Visland" => "/visland stop",
            _ => null, // BossMod/Vanilla have no explicit stop
        };

        if (cmd != null)
            SendCommand(cmd);
    }

    /// <summary>
    /// Send a slash command via Dalamud's command manager.
    /// Works for commands registered by any Dalamud plugin (VNavmesh, Visland, BossMod, etc.).
    /// </summary>
    private static void SendCommand(string command)
    {
        try
        {
            if (!Plugin.CommandManager.ProcessCommand(command))
                Plugin.Log.Warning($"Command not handled: {command}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Command failed [{command}]: {ex.Message}");
        }
    }
}
