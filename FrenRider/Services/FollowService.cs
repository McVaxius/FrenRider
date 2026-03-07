using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
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
    private long lastFlyingAdjustMs;

    // Stuck detection: record position every 5s, compare absolute XYZ sum delta
    private Vector3 stuckCheckPosition;
    private long stuckCheckTimeMs;
    private const long StuckCheckIntervalMs = 5000;
    private const float StuckDeltaThreshold = 10f;

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
            stuckCheckPosition = default;
            stuckCheckTimeMs = 0;
            // Force immediate fren scan on next frame (skip throttle)
            tracker.ForceNextScan();
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

        // Flying follow: if fren is flying and we're mounted but NOT already flying, send jump
        // This matches SND: "if Svc.Condition[77] then flying_adjust = flying_adjust + 1"
        var selfMounted = Plugin.Condition[ConditionFlag.Mounted];
        var selfFlying = Plugin.Condition[ConditionFlag.InFlight];
        var frenFlying = fren.IsFlying;
        var now = Environment.TickCount64;
        
        if (selfMounted && frenFlying && !selfFlying && now - lastFlyingAdjustMs > 1000)
        {
            // Send jump command to initiate flight (only if not already flying)
            SendCommand("/gaction jump");
            lastFlyingAdjustMs = now;
            Plugin.Log.Information("Flying follow: sent jump command to initiate flight");
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
        var selfFlying = Plugin.Condition[ConditionFlag.InFlight];
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        var now = Environment.TickCount64;

        // First navigation after idle/zone change: always issue command
        if (!isNavigating)
        {
            IssueNavCommand(config, target, selfFlying);
            stuckCheckPosition = localPlayer?.Position ?? default;
            stuckCheckTimeMs = now;
            return;
        }

        // Already navigating - only re-pathfind if:
        // 1. Reached end of current path segment (close to lastNavTarget)
        // 2. Stuck (XYZ absolute sum delta < 10 over 5 seconds)

        // Check if we reached the end of the current path segment
        if (localPlayer != null)
        {
            var distToNavTarget = Vector3.Distance(localPlayer.Position, lastNavTarget);
            var arrivedThreshold = selfFlying ? 5.0f : 2.0f;
            if (distToNavTarget < arrivedThreshold)
            {
                // Arrived at nav target - re-pathfind to updated fren position
                IssueNavCommand(config, target, selfFlying);
                stuckCheckPosition = localPlayer.Position;
                stuckCheckTimeMs = now;
                return;
            }

            // Stuck detection: every 5 seconds check if we've barely moved
            if (now - stuckCheckTimeMs >= StuckCheckIntervalMs)
            {
                var pos = localPlayer.Position;
                var delta = Math.Abs(pos.X - stuckCheckPosition.X)
                          + Math.Abs(pos.Y - stuckCheckPosition.Y)
                          + Math.Abs(pos.Z - stuckCheckPosition.Z);

                if (delta < StuckDeltaThreshold)
                {
                    // Stuck - re-pathfind
                    Plugin.Log.Information($"[FR] Stuck detected (delta={delta:F1} < {StuckDeltaThreshold}), re-pathfinding");
                    IssueNavCommand(config, target, selfFlying);
                }

                // Reset stuck check regardless
                stuckCheckPosition = pos;
                stuckCheckTimeMs = now;
            }
        }
    }

    private void IssueNavCommand(CharacterConfig config, Vector3 target, bool selfFlying)
    {
        lastNavTarget = target;
        isNavigating = true;

        if (selfFlying)
        {
            var coords = FormatVector(target);
            var cmd = $"/vnav flyto {coords}";
            SendCommand(cmd);
            return;
        }

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

        var coords = FormatVector(target);
        var cmd = typeName switch
        {
            "NavMesh" => $"/vnav moveto {coords}",
            "Visland" => $"/visland moveto {coords}",
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

        var cmd = clingType switch
        {
            0 => "/vnavmesh stop",
            1 => "/visland stop",
            2 => "/bmrai follow off",
            3 => "/follow",
            _ => "/vnavmesh stop",
        };

        SendCommand(cmd);
    }

    /// <summary>
    /// Send a slash command to the game.
    /// Uses UIModule.ProcessChatBoxEntry to send commands directly to game (like typing in chat).
    /// </summary>
    private static unsafe void SendCommand(string command)
    {
        try
        {
            // Try plugin command first (for nav commands)
            if (Plugin.CommandManager.ProcessCommand(command))
                return;
            
            // Fall back to game command (for /hold, /release, etc.)
            var uiModule = UIModule.Instance();
            if (uiModule == null)
            {
                Plugin.Log.Error("UIModule is null, cannot send command");
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(command);
            var utf8String = Utf8String.FromSequence(bytes);
            uiModule->ProcessChatBoxEntry(utf8String, nint.Zero);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Command failed [{command}]: {ex.Message}");
        }
    }

    private static string FormatVector(Vector3 value)
    {
        return string.Format(CultureInfo.InvariantCulture, "{0:F2} {1:F2} {2:F2}", value.X, value.Y, value.Z);
    }
}
