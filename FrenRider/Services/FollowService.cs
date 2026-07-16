using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using ECommons.Automation;
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

internal enum FlyingStuckRecoveryPhase
{
    None,
    Ascending,
    Automoving,
}

public class FollowService
{
    private readonly Plugin plugin;
    private readonly FrenTracker tracker;
    private readonly ZoneService zoneService;
    private readonly VNavStateService vnavState;

    private Vector3 lastNavTarget;
    private bool isNavigating;
    private int lastMovementClingType;
    private Vector3 socialOffset;
    private long lastOffsetChangeMs;
    private long lastFlyingAdjustMs;
    private bool bossModFollowActive;
    private string bossModFollowTarget = string.Empty;
    private uint bossModFollowTerritoryId;
    private int bossModFollowCombatMode = -1;
    private bool bossModFollowFrenFlying;
    private bool bossModFollowSelfFlying;
    private bool lastNavigationWasFlying;
    private bool lastNavigationWasFrenFollow;
    private long lastNavCommandMs;
    private long lastFlyingIdleReissueMs;
    private Vector3 flyingIdleSamplePosition;
    private long flyingIdleSampleTimeMs;
    private bool flyingTakeoffPending;
    private long flyingTakeoffGroundNavHoldUntilMs;
    private bool chaseSettingsInitialized;
    private bool lastChaseEnabled;
    private float lastChaseDistance;
    private int lastChaseDelaySeconds;
    private long farChaseDelayEligibleSinceMs = FrenRiderMountPolicy.FarChaseDelayNotPendingMs;

    // Stuck detection: record position every 5s, check per-axis movement < 2y
    private Vector3 stuckCheckPosition;
    private long stuckCheckTimeMs;
    private Vector3 stuckFollowJumpBaselinePosition;
    private long stuckFollowJumpBaselineTimeMs;
    private long lastStuckFollowJumpMs;
    private const long StuckCheckIntervalMs = 5000;
    private const float StuckPerAxisThreshold = 2f;
    private const long StuckFollowJumpWindowMs = 10000;
    private const long StuckFollowJumpThrottleMs = 15000;
    private const float StuckFollowJumpMovementThreshold = 0.75f;
    private const long FlyingTakeoffJumpThrottleMs = 1000;
    private const long FlyingTakeoffGroundNavHoldMs = 1200;
    private const long FlyingIdleNavCommandGraceMs = 250;
    private const long FlyingIdleReissueThrottleMs = 500;
    private const long FlyingIdleMovementSampleMs = 150;
    private const float FlyingIdleMovementThreshold = 0.25f;

    private Vector3 flyingStuckBaselinePosition;
    private long flyingStuckBaselineTimeMs;
    private FlyingStuckRecoveryPhase flyingStuckRecoveryPhase;
    private long flyingStuckRecoveryPhaseStartMs;
    private bool flyingStuckAscendHeld;
    private bool flyingStuckAutomoveOn;
    private bool flyingStuckLoggedEcommonsUnavailable;

    private const long FlyingStuckWindowMs = 10000;
    private const long FlyingStuckAscendMs = 10000;
    private const long FlyingStuckAutomoveMs = 10000;
    private const float FlyingStuckPerAxisThreshold = 0.5f;

    private static readonly string[] ClingTypeNames = { "NavMesh", "Visland", "BossMod Follow", "Vanilla Follow" };

    public FollowState State { get; private set; } = FollowState.Idle;
    public string StateDetail { get; private set; } = "";
    public bool IsFarChaseRequested { get; private set; }
    public float FarChaseXzDistance { get; private set; }

    public FollowService(Plugin plugin, FrenTracker tracker, ZoneService zoneService)
    {
        this.plugin = plugin;
        this.tracker = tracker;
        this.zoneService = zoneService;
        vnavState = new VNavStateService(Plugin.PluginInterface, Plugin.Log);
    }

    public void Dispose()
    {
        CancelFlyingStuckRecovery("plugin dispose");
    }

    public void ResetForAreaTransition()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        PreemptFarChase("area transition");
        StopAllFollowing(config, "area transition");
        State = FollowState.Idle;
        StateDetail = "Area transition";
    }

    public void PreemptFarChase(string reason)
    {
        ResetFarChaseDelay();

        if (!IsFarChaseRequested)
            return;

        var config = plugin.ConfigManager.GetActiveConfig();
        StopAllFollowing(config, $"far chase preempted: {reason}");
        SetFarChaseRequested(false, reason);
    }

    public void CancelFlyingStuckRecovery(string reason)
    {
        if (flyingStuckRecoveryPhase == FlyingStuckRecoveryPhase.None
            && !flyingStuckAscendHeld
            && !flyingStuckAutomoveOn)
        {
            ResetFlyingStuckTracking();
            return;
        }

        ReleaseAscendKey($"{reason} cleanup");
        SendAutomoveOff($"{reason} cleanup", force: true);

        Plugin.Log.Information($"[FR][FlyingStuck] Recovery canceled: {reason}");
        flyingStuckRecoveryPhase = FlyingStuckRecoveryPhase.None;
        flyingStuckRecoveryPhaseStartMs = 0;
        ResetFlyingStuckTracking();
    }

    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var chaseDistance = GetChaseDistance(config);
        var chaseDelaySeconds = GetChaseDelaySeconds(config);
        if (ChaseSettingsChanged(config.MountUpToChaseFren, chaseDistance, chaseDelaySeconds))
        {
            PreemptFarChase("settings changed");
            StopAllFollowing(config, "far chase settings changed");
            State = FollowState.Idle;
            StateDetail = "Far chase settings changed";
            return;
        }

        // Zone transition: stop navigation and reset
        if (zoneService.ZoneChanged)
        {
            SetFarChaseRequested(false, "zone transition");
            StopAllFollowing(config, "zone transition");
            State = FollowState.Idle;
            StateDetail = "Zone transition";
            lastNavTarget = default;
            socialOffset = default;
            stuckCheckPosition = default;
            stuckCheckTimeMs = 0;
            ResetFlyingStuckTracking();
            ResetStuckFollowJumpTracking();
            ResetFlyingIdleSampler();
            LogFateFollowDecisionIfChanged(config, tracker.Fren, "zone transition");
            // Force immediate fren scan on next frame (skip throttle)
            tracker.ForceNextScan();
            return;
        }

        if (!config.Enabled)
        {
            SetFarChaseRequested(false, "disabled");
            StopAllFollowing(config, "disabled");
            State = FollowState.Idle;
            StateDetail = "Disabled";
            LogFateFollowDecisionIfChanged(config, tracker.Fren, "disabled");
            return;
        }

        if (plugin.AdsIntegrationService.ShouldPauseDutySystems
            || plugin.AdsIntegrationService.IsHandoffPending)
        {
            SetFarChaseRequested(false, plugin.AdsIntegrationService.IsHandoffPending
                ? "ADS handoff pending"
                : "ADS active");
            StopAllFollowing(config, plugin.AdsIntegrationService.IsHandoffPending
                ? "ADS handoff pending"
                : "ADS active");
            State = FollowState.Idle;
            StateDetail = plugin.AdsIntegrationService.IsHandoffPending
                ? "ADS handoff pending"
                : "ADS active";
            LogFateFollowDecisionIfChanged(config, tracker.Fren, StateDetail);
            return;
        }

        if (plugin.AutomationService.IsUtilityGateActive)
        {
            SetFarChaseRequested(false, "ADS utility active");
            StopAllFollowing(config, "ADS utility active");
            State = FollowState.Idle;
            StateDetail = "ADS utility active";
            LogFateFollowDecisionIfChanged(config, tracker.Fren, "ADS utility active");
            return;
        }

        if (Plugin.Condition[ConditionFlag.Unconscious])
        {
            SetFarChaseRequested(false, "unconscious");
            StopAllFollowing(config, "unconscious");
            State = FollowState.Idle;
            StateDetail = "Unconscious";
            LogFateFollowDecisionIfChanged(config, tracker.Fren, "unconscious");
            return;
        }

        if (HasTeleportOrDialogActivity())
        {
            SetFarChaseRequested(false, "teleport or dialog active");
            StopAllFollowing(config, "teleport or dialog active");
            State = FollowState.Idle;
            StateDetail = "Teleport or dialog active";
            LogFateFollowDecisionIfChanged(config, tracker.Fren, "teleport or dialog active");
            return;
        }

        var fren = tracker.Fren;
        if (fren == null || !fren.IsFound)
        {
            SetFarChaseRequested(false, "no fren found");
            StopAllFollowing(config, "no fren found");
            State = FollowState.Idle;
            StateDetail = "No fren found";
            LogFateFollowDecisionIfChanged(config, fren, "no fren found");
            return;
        }

        if (!fren.IsVisible)
        {
            SetFarChaseRequested(false, "fren not visible");
            StopAllFollowing(config, "fren not visible");
            State = FollowState.Idle;
            StateDetail = "Fren not visible";
            LogFateFollowDecisionIfChanged(config, fren, "fren not visible");
            return;
        }

        // Combat check
        if (Plugin.Condition[ConditionFlag.InCombat])
        {
            PreemptFarChase("combat");
            // FollowInCombat: 0=No, 1=Yes, 2=Auto
            if (config.FollowInCombat == 0)
            {
                StopAllFollowing(config, "combat pause");
                State = FollowState.InCombat;
                StateDetail = "Paused (in combat)";
                LogFateFollowDecisionIfChanged(config, fren, "combat pause");
                return;
            }
            // 1=Yes or 2=Auto: continue following
        }

        var distance = fren.Distance;
        var maxDist = GetMaxDistance(config);
        var clingDist = GetEffectiveClingDistance(config);
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        FarChaseXzDistance = localPlayer == null
            ? 0f
            : GetXzDistance(localPlayer.Position, fren.Position);
        var now = Environment.TickCount64;

        UpdateFarChaseRequest(config, fren, FarChaseXzDistance, distance, maxDist, clingDist, chaseDelaySeconds, now);

        var resolvedClingType = GetMovementClingType(config);
        if (resolvedClingType != 2 && bossModFollowActive)
        {
            StopBossModFollow();
        }

        var selfRidingPillion = Plugin.Condition[ConditionFlag.RidingPillion];
        var selfMounted = Plugin.Condition[ConditionFlag.Mounted]
            && (!IsFarChaseRequested || !selfRidingPillion);
        var selfMounting = Plugin.Condition[ConditionFlag.Mounting71];
        var selfFlying = IsSelfFlightNavActive();
        var frenFlying = fren.IsFlying;
        if (!selfMounted || (!frenFlying && !IsFarChaseRequested))
            ResetFlyingTakeoffState();

        // Too far — stop
        if (distance > maxDist)
        {
            SetFarChaseRequested(false, "beyond max follow distance");
            CancelFlyingStuckRecovery("too far");
            ResetStuckFollowJumpTracking();
            ResetFlyingTakeoffState();
            if (isNavigating) StopNavigation(config, $"too far ({distance:F1}y > {maxDist:F0}y max)");
            State = FollowState.TooFar;
            StateDetail = $"Too far ({distance:F1}y > {maxDist:F0}y max)";
            LogFateFollowDecisionIfChanged(config, fren, StateDetail);
            return;
        }

        // In range — stop
        if (distance <= clingDist)
        {
            SetFarChaseRequested(false, "in range");
            CancelFlyingStuckRecovery("in range");
            ResetStuckFollowJumpTracking();
            ResetFlyingTakeoffState();
            if (isNavigating) StopNavigation(config, $"in range ({distance:F1}y <= {clingDist:F1}y)");
            State = FollowState.InRange;
            StateDetail = $"In range ({distance:F1}y)";
            LogFateFollowDecisionIfChanged(config, fren, StateDetail);
            return;
        }

        if (IsFarChaseRequested && !selfMounted)
        {
            CancelFlyingStuckRecovery("far chase waiting for mount");
            ResetStuckFollowJumpTracking();
            ResetFlyingTakeoffState();
            if (isNavigating || bossModFollowActive)
                StopAllFollowing(config, "far chase waiting for own mount");
            State = FollowState.Following;
            StateDetail = selfMounting
                ? $"Far chase: mounting ({FarChaseXzDistance:F1}y XZ)"
                : $"Far chase: requesting mount ({FarChaseXzDistance:F1}y XZ)";
            LogFateFollowDecisionIfChanged(config, fren, "none");
            return;
        }

        if (flyingStuckRecoveryPhase != FlyingStuckRecoveryPhase.None)
        {
            if (!IsFlyingStuckRecoveryAllowed(config, selfMounted, selfFlying))
            {
                CancelFlyingStuckRecovery("flight follow no longer eligible");
            }
            else if (UpdateFlyingStuckRecovery(now))
            {
                State = FollowState.Following;
                LogFateFollowDecisionIfChanged(config, fren, "none");
                return;
            }
        }

        TryStartFlyingTakeoff(config, selfMounted, selfFlying, frenFlying || IsFarChaseRequested, now);

        // Formation mode: override target with formation position
        var formationTarget = plugin.FormationService.GetFormationTarget();
        if (formationTarget.HasValue)
        {
            ResetFlyingTakeoffState();
            CancelFlyingStuckRecovery("formation follow active");
            ResetStuckFollowJumpTracking();
            if (bossModFollowActive)
                StopBossModFollow();

            var formationLocalPlayer = Plugin.ObjectTable.LocalPlayer;
            if (formationLocalPlayer != null)
            {
                var formDist = Vector3.Distance(formationLocalPlayer.Position, formationTarget.Value);
                if (formDist <= 1.5f)
                {
                    if (isNavigating) StopNavigation(config, $"formation in range ({formDist:F1}y)");
                    State = FollowState.InRange;
                    StateDetail = $"Formation slot {plugin.FormationService.AssignedSlot} ({formDist:F1}y)";
                    LogFateFollowDecisionIfChanged(config, fren, StateDetail);
                    return;
                }

                State = FollowState.Following;
                StateDetail = $"Formation slot {plugin.FormationService.AssignedSlot} ({formDist:F1}y)";
                LogFateFollowDecisionIfChanged(config, fren, "none");
                NavigateToPosition(config, formationTarget.Value);
                return;
            }
        }

        // Follow
        if (resolvedClingType == 2)
            ResetStuckFollowJumpTracking();
        else
            UpdateStuckFollowJump(config, fren, distance, clingDist, maxDist, selfFlying, now);

        State = FollowState.Following;
        StateDetail = IsFarChaseRequested
            ? $"Far chase ({FarChaseXzDistance:F1}y XZ, max {maxDist:F0}y)"
            : $"Following ({distance:F1}y, cling {clingDist:F1}y)";
        LogFateFollowDecisionIfChanged(config, fren, "none");
        NavigateToFren(config, fren);
    }

    public float GetEffectiveClingDistance(CharacterConfig config)
    {
        var cling = config.Cling;

        if (zoneService.CurrentZone == ZoneType.DeepDungeon)
            cling += config.DDDistance;

        // FDistance is reserved for future autosync FATE work; FATE join/leave must not
        // change follow stop/start decisions in this pass.

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

    private int GetResolvedClingType(CharacterConfig config)
    {
        return zoneService.CurrentZone == ZoneType.Duty
            ? config.ClingTypeDuty
            : config.ClingType;
    }

    private int GetMovementClingType(CharacterConfig config)
        => IsFarChaseRequested ? 0 : GetResolvedClingType(config);

    private bool ChaseSettingsChanged(bool enabled, float distance, int delaySeconds)
    {
        if (!chaseSettingsInitialized)
        {
            chaseSettingsInitialized = true;
            lastChaseEnabled = enabled;
            lastChaseDistance = distance;
            lastChaseDelaySeconds = delaySeconds;
            return false;
        }

        if (lastChaseEnabled == enabled
            && lastChaseDistance.Equals(distance)
            && lastChaseDelaySeconds == delaySeconds)
        {
            return false;
        }

        lastChaseEnabled = enabled;
        lastChaseDistance = distance;
        lastChaseDelaySeconds = delaySeconds;
        return true;
    }

    private void UpdateFarChaseRequest(
        CharacterConfig config,
        FrenTracker.FrenState fren,
        float xzDistance,
        float distance,
        float maxDistance,
        float clingDistance,
        int delaySeconds,
        long now)
    {
        var eligible = config.Enabled
            && config.MountUpToChaseFren
            && zoneService.CurrentZone == ZoneType.Overworld
            && !Plugin.Condition[ConditionFlag.BoundByDuty]
            && !Plugin.Condition[ConditionFlag.BoundByDuty56]
            && !IsLoadingOrBetweenAreas()
            && !Plugin.Condition[ConditionFlag.InCombat]
            && !Plugin.Condition[ConditionFlag.Unconscious]
            && !config.Formation
            && fren.IsFound
            && fren.IsVisible
            && distance <= maxDistance;
        var requested = FrenRiderMountPolicy.ShouldRequestFarChase(
            IsFarChaseRequested,
            eligible,
            xzDistance,
            GetChaseDistance(config),
            clingDistance,
            delaySeconds,
            now,
            ref farChaseDelayEligibleSinceMs);

        if (requested == IsFarChaseRequested)
            return;

        StopAllFollowing(config, requested ? "starting far chase" : "ending far chase");
        SetFarChaseRequested(
            requested,
            requested
                ? "horizontal chase threshold exceeded"
                : eligible
                    ? "effective XZ cling range reached"
                    : "eligibility lost");
    }

    private void SetFarChaseRequested(bool requested, string reason)
    {
        if (!requested)
            ResetFarChaseDelay();

        if (IsFarChaseRequested == requested)
            return;

        IsFarChaseRequested = requested;
        Plugin.Log.Information(
            $"[FR][FarChase] {(requested ? "Started" : "Stopped")}: {reason}; xz={FarChaseXzDistance:F1}y");
    }

    private bool HasTeleportOrDialogActivity()
    {
        var teleportState = plugin.FrenTeleportService.State;
        return teleportState is FrenTeleportState.Waiting
                or FrenTeleportState.ReadingParty
                or FrenTeleportState.TeleportIssued
                or FrenTeleportState.Cooldown
            || GameHelpers.IsAddonVisible("SelectYesno")
            || GameHelpers.IsAddonVisible("_NotificationTelepo");
    }

    private static float GetChaseDistance(CharacterConfig config)
        => float.IsFinite(config.MountUpToChaseFrenDistance)
            ? Math.Max(1f, config.MountUpToChaseFrenDistance)
            : 1f;

    private static int GetChaseDelaySeconds(CharacterConfig config)
        => Math.Clamp(config.MountUpToChaseFrenDelaySeconds, 0, 300);

    private void ResetFarChaseDelay()
        => farChaseDelayEligibleSinceMs = FrenRiderMountPolicy.FarChaseDelayNotPendingMs;

    public static float GetXzDistance(Vector3 first, Vector3 second)
    {
        var dx = first.X - second.X;
        var dz = first.Z - second.Z;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    private void UpdateStuckFollowJump(
        CharacterConfig config,
        FrenTracker.FrenState fren,
        float distance,
        float clingDist,
        float maxDist,
        bool selfFlying,
        long now)
    {
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null
            || !ShouldTrackStuckFollowJump(config, fren, distance, clingDist, maxDist, selfFlying))
        {
            ResetStuckFollowJumpTracking();
            return;
        }

        var position = localPlayer.Position;
        if (stuckFollowJumpBaselineTimeMs == 0)
        {
            ResetStuckFollowJumpTracking(position, now);
            return;
        }

        var moved = Vector3.Distance(position, stuckFollowJumpBaselinePosition);
        if (moved >= StuckFollowJumpMovementThreshold)
        {
            ResetStuckFollowJumpTracking(position, now);
            return;
        }

        if (now - stuckFollowJumpBaselineTimeMs < StuckFollowJumpWindowMs)
            return;

        if (lastStuckFollowJumpMs != 0 && now - lastStuckFollowJumpMs < StuckFollowJumpThrottleMs)
            return;

        SendCommand("/gaction jump");
        lastStuckFollowJumpMs = now;
        Plugin.Log.Information(
            $"[FR][Pathing] Stuck follow jump sent after local movement stayed under {StuckFollowJumpMovementThreshold:F2}y for {StuckFollowJumpWindowMs / 1000}s; distance={distance:F1}y cling={clingDist:F1}y max={maxDist:F1}y local={FormatVector(position)} fren={FormatVector(fren.Position)}.");
        ResetStuckFollowJumpTracking(position, now);
    }

    private bool ShouldTrackStuckFollowJump(
        CharacterConfig config,
        FrenTracker.FrenState fren,
        float distance,
        float clingDist,
        float maxDist,
        bool selfFlying)
    {
        return config.Enabled
            && fren.IsFound
            && fren.IsVisible
            && distance > clingDist
            && distance <= maxDist
            && !selfFlying
            && !IsLoadingOrBetweenAreas()
            && !plugin.AdsIntegrationService.ShouldPauseDutySystems
            && !plugin.AutomationService.IsUtilityGateActive
            && GetMovementClingType(config) != 2;
    }

    private void ResetStuckFollowJumpTracking()
    {
        stuckFollowJumpBaselinePosition = default;
        stuckFollowJumpBaselineTimeMs = 0;
    }

    private void ResetStuckFollowJumpTracking(Vector3 position, long now)
    {
        stuckFollowJumpBaselinePosition = position;
        stuckFollowJumpBaselineTimeMs = now;
    }

    private void NavigateToFren(CharacterConfig config, FrenTracker.FrenState fren)
    {
        if (GetMovementClingType(config) == 2)
        {
            ResetFlyingTakeoffState();
            CancelFlyingStuckRecovery("BossMod follow active");
            ResetStuckFollowJumpTracking();
            EnsureBossModFollow(config, fren);
            return;
        }

        var target = fren.Position;

        // Apply social distancing offset
        if (ShouldApplySocialDistancing(config))
            target = ApplySocialDistancing(config, target);

        NavigateToPosition(config, target, isFrenFollowTarget: true);
    }

    private void NavigateToPosition(CharacterConfig config, Vector3 target, bool isFrenFollowTarget = false)
    {
        var selfFlying = IsSelfFlightNavActive();
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        var now = Environment.TickCount64;

        if (TryIssueImmediateFlytoAfterTakeoff(config, target, selfFlying, isFrenFollowTarget, localPlayer?.Position ?? default, now))
            return;

        if (ShouldHoldGroundRepathForTakeoff(isFrenFollowTarget, selfFlying, now))
            return;

        // First navigation after idle/zone change: always issue command
        if (!isNavigating)
        {
            IssueNavCommand(config, target, selfFlying, "initial navigation", isFrenFollowTarget);
            stuckCheckPosition = localPlayer?.Position ?? default;
            stuckCheckTimeMs = now;
            ResetFlyingStuckTrackingIfIneligible(config, localPlayer?.Position ?? default, selfFlying, isFrenFollowTarget, now);
            return;
        }

        // Already navigating - only re-pathfind if:
        // 1. Reached end of current path segment (close to lastNavTarget)
        // 2. Stuck (XYZ absolute sum delta < 10 over 5 seconds)

        // Check if we reached the end of the current path segment
        if (localPlayer != null)
        {
            if (TryReissueIdleFlyingFrenFollow(config, localPlayer.Position, target, selfFlying, isFrenFollowTarget, now))
                return;

            var distToNavTarget = Vector3.Distance(localPlayer.Position, lastNavTarget);
            var arrivedThreshold = selfFlying ? 5.0f : 2.0f;
            if (distToNavTarget < arrivedThreshold)
            {
                // Arrived at nav target - re-pathfind to updated fren position
                IssueNavCommand(
                    config,
                    target,
                    selfFlying,
                    $"repath after reaching last target (dist={distToNavTarget:F1}y threshold={arrivedThreshold:F1}y)",
                    isFrenFollowTarget);
                stuckCheckPosition = localPlayer.Position;
                stuckCheckTimeMs = now;
                ResetFlyingStuckTrackingIfIneligible(config, localPlayer.Position, selfFlying, isFrenFollowTarget, now);
                return;
            }

            if (UpdateFlyingStuckDetection(config, localPlayer.Position, target, selfFlying, isFrenFollowTarget, now))
                return;

            // Stuck detection: every 5 seconds check if we've barely moved on any axis
            if (now - stuckCheckTimeMs >= StuckCheckIntervalMs)
            {
                var pos = localPlayer.Position;
                var dx = Math.Abs(pos.X - stuckCheckPosition.X);
                var dy = Math.Abs(pos.Y - stuckCheckPosition.Y);
                var dz = Math.Abs(pos.Z - stuckCheckPosition.Z);

                if (dx < StuckPerAxisThreshold && dy < StuckPerAxisThreshold && dz < StuckPerAxisThreshold)
                {
                    // Stuck - stop current navigation and re-pathfind
                    var stuckReason =
                        $"stuck repath (dX={dx:F1} dY={dy:F1} dZ={dz:F1}, all <{StuckPerAxisThreshold}y in {StuckCheckIntervalMs / 1000}s)";
                    Plugin.Log.Information(
                        $"[FR][Pathing] {stuckReason}; local={FormatVector(pos)}; lastTarget={FormatVector(lastNavTarget)}; nextTarget={FormatVector(target)}");
                    StopNavigation(config, stuckReason);
                    IssueNavCommand(config, target, selfFlying, stuckReason, isFrenFollowTarget);
                }

                // Reset stuck check regardless
                stuckCheckPosition = pos;
                stuckCheckTimeMs = now;
            }
        }
    }

    private void TryStartFlyingTakeoff(
        CharacterConfig config,
        bool selfMounted,
        bool selfFlying,
        bool frenFlying,
        long now)
    {
        if (!selfMounted || !frenFlying || selfFlying)
            return;

        if (now - lastFlyingAdjustMs <= FlyingTakeoffJumpThrottleMs)
            return;

        SendCommand("/gaction jump");
        lastFlyingAdjustMs = now;
        flyingTakeoffPending = true;
        flyingTakeoffGroundNavHoldUntilMs = now + FlyingTakeoffGroundNavHoldMs;

        var stoppedGroundVnav = false;
        if (isNavigating && lastMovementClingType == 0 && !lastNavigationWasFlying)
        {
            StopNavigation(config, "flying takeoff");
            stoppedGroundVnav = true;
        }

        Plugin.Log.Information(
            $"[FR][Pathing] Flying follow takeoff: sent /gaction jump; stoppedGroundVnav={stoppedGroundVnav}; holdGroundRepathMs={FlyingTakeoffGroundNavHoldMs}");
    }

    private bool TryIssueImmediateFlytoAfterTakeoff(
        CharacterConfig config,
        Vector3 target,
        bool selfFlying,
        bool isFrenFollowTarget,
        Vector3 localPosition,
        long now)
    {
        if (!isFrenFollowTarget || !selfFlying)
            return false;

        if (!flyingTakeoffPending && (!isNavigating || lastNavigationWasFlying))
            return false;

        flyingTakeoffPending = false;
        flyingTakeoffGroundNavHoldUntilMs = 0;
        IssueNavCommand(config, target, true, "fren follow takeoff complete", isFrenFollowTarget);
        stuckCheckPosition = localPosition;
        stuckCheckTimeMs = now;
        ResetFlyingStuckTracking(localPosition, now);
        Plugin.Log.Information(
            $"[FR][Pathing] Flying follow takeoff complete; issued immediate flyto. local={FormatVector(localPosition)}; target={FormatVector(target)}");
        return true;
    }

    private bool ShouldHoldGroundRepathForTakeoff(bool isFrenFollowTarget, bool selfFlying, long now)
    {
        if (!isFrenFollowTarget || selfFlying || !flyingTakeoffPending)
            return false;

        if (now >= flyingTakeoffGroundNavHoldUntilMs)
            return false;

        ResetFlyingIdleSampler();
        return true;
    }

    private void ResetFlyingTakeoffState()
    {
        flyingTakeoffPending = false;
        flyingTakeoffGroundNavHoldUntilMs = 0;
    }

    private static bool IsSelfFlightNavActive()
    {
        return Plugin.Condition[ConditionFlag.InFlight]
            || Plugin.Condition[ConditionFlag.Diving];
    }

    private bool TryReissueIdleFlyingFrenFollow(
        CharacterConfig config,
        Vector3 localPosition,
        Vector3 target,
        bool selfFlying,
        bool isFrenFollowTarget,
        long now)
    {
        if (!ShouldCheckFlyingIdleReissue(config, selfFlying, isFrenFollowTarget, now))
        {
            ResetFlyingIdleSampler();
            return false;
        }

        if (!vnavState.TryGetState(out var pathRunning, out var pathfindInProgress))
            return false;

        if (pathRunning || pathfindInProgress)
        {
            ResetFlyingIdleSampler();
            return false;
        }

        if (!IsFlyingIdlePositionStable(localPosition, now))
            return false;

        lastFlyingIdleReissueMs = now;
        Plugin.Log.Information(
            $"[FR][Pathing] Flying vnav idle while following fren; reissuing flyto. local={FormatVector(localPosition)}; target={FormatVector(target)}");
        IssueNavCommand(config, target, selfFlying, "flying vnav idle reissue", isFrenFollowTarget);
        stuckCheckPosition = localPosition;
        stuckCheckTimeMs = now;
        ResetFlyingStuckTracking(localPosition, now);
        return true;
    }

    private bool ShouldCheckFlyingIdleReissue(
        CharacterConfig config,
        bool selfFlying,
        bool isFrenFollowTarget,
        long now)
    {
        return config.Enabled
            && isFrenFollowTarget
            && isNavigating
            && lastNavigationWasFrenFollow
            && lastNavigationWasFlying
            && lastMovementClingType == 0
            && selfFlying
            && (Plugin.Condition[ConditionFlag.Mounted] || Plugin.Condition[ConditionFlag.Diving])
            && !IsLoadingOrBetweenAreas()
            && !plugin.AdsIntegrationService.ShouldPauseDutySystems
            && !plugin.AutomationService.IsUtilityGateActive
            && GetMovementClingType(config) != 2
            && zoneService.CurrentZone != ZoneType.Foray
            && now - lastNavCommandMs >= FlyingIdleNavCommandGraceMs
            && now - lastFlyingIdleReissueMs >= FlyingIdleReissueThrottleMs;
    }

    private bool IsFlyingIdlePositionStable(Vector3 localPosition, long now)
    {
        if (flyingIdleSampleTimeMs == 0)
        {
            flyingIdleSamplePosition = localPosition;
            flyingIdleSampleTimeMs = now;
            return false;
        }

        if (now - flyingIdleSampleTimeMs < FlyingIdleMovementSampleMs)
            return false;

        var moved = Vector3.Distance(localPosition, flyingIdleSamplePosition);
        flyingIdleSamplePosition = localPosition;
        flyingIdleSampleTimeMs = now;
        return moved < FlyingIdleMovementThreshold;
    }

    private void ResetFlyingIdleSampler()
    {
        flyingIdleSamplePosition = default;
        flyingIdleSampleTimeMs = 0;
    }

    private void IssueNavCommand(CharacterConfig config, Vector3 target, bool selfFlying, string reason, bool isFrenFollowTarget)
    {
        var previousTarget = lastNavTarget;
        lastNavTarget = target;
        isNavigating = true;
        lastNavigationWasFrenFollow = isFrenFollowTarget;
        lastNavCommandMs = Environment.TickCount64;
        ResetFlyingIdleSampler();

        // Foray zones: never fly, always use moveto (no flying in forays)
        if (zoneService.CurrentZone == ZoneType.Foray)
        {
            lastMovementClingType = 0;
            lastNavigationWasFlying = false;
            var coords = FormatVector(target);
            var cmd = $"/vnav moveto {coords}";
            LogNavCommand(reason, lastMovementClingType, cmd, target, previousTarget, selfFlying);
            SendCommand(cmd);
            return;
        }

        if (selfFlying)
        {
            lastMovementClingType = 0;
            lastNavigationWasFlying = true;
            var coords = FormatVector(target);
            var cmd = $"/vnav flyto {coords}";
            LogNavCommand(reason, lastMovementClingType, cmd, target, previousTarget, selfFlying);
            SendCommand(cmd);
            return;
        }

        var clingType = GetMovementClingType(config);
        lastMovementClingType = clingType;
        lastNavigationWasFlying = false;

        SendNavigationCommand(clingType, target, reason, previousTarget, selfFlying);
    }

    private bool UpdateFlyingStuckDetection(
        CharacterConfig config,
        Vector3 position,
        Vector3 target,
        bool selfFlying,
        bool isFrenFollowTarget,
        long now)
    {
        if (!ShouldTrackFlyingStuck(config, selfFlying, isFrenFollowTarget))
        {
            ResetFlyingStuckTracking();
            return false;
        }

        if (flyingStuckBaselineTimeMs == 0)
        {
            ResetFlyingStuckTracking(position, now);
            return false;
        }

        var dx = Math.Abs(position.X - flyingStuckBaselinePosition.X);
        var dy = Math.Abs(position.Y - flyingStuckBaselinePosition.Y);
        var dz = Math.Abs(position.Z - flyingStuckBaselinePosition.Z);

        if (dx >= FlyingStuckPerAxisThreshold
            || dy >= FlyingStuckPerAxisThreshold
            || dz >= FlyingStuckPerAxisThreshold)
        {
            ResetFlyingStuckTracking(position, now);
            return false;
        }

        if (now - flyingStuckBaselineTimeMs < FlyingStuckWindowMs)
            return false;

        var reason =
            $"flying stuck escape (dX={dx:F1} dY={dy:F1} dZ={dz:F1}, all <{FlyingStuckPerAxisThreshold}y in {FlyingStuckWindowMs / 1000}s)";
        Plugin.Log.Warning(
            $"[FR][FlyingStuck] {reason}; local={FormatVector(position)}; lastTarget={FormatVector(lastNavTarget)}; nextTarget={FormatVector(target)}");

        StopNavigation(config, reason);
        StartFlyingStuckRecovery(now);
        ResetFlyingStuckTracking(position, now);
        return true;
    }

    private bool UpdateFlyingStuckRecovery(long now)
    {
        var elapsed = now - flyingStuckRecoveryPhaseStartMs;
        switch (flyingStuckRecoveryPhase)
        {
            case FlyingStuckRecoveryPhase.Ascending:
                if (elapsed < FlyingStuckAscendMs)
                {
                    StateDetail = $"Flying stuck escape: ascend ({Math.Max(0, (FlyingStuckAscendMs - elapsed) / 1000)}s)";
                    return true;
                }

                ReleaseAscendKey("ascend phase complete");
                SendAutomoveOn("forward phase start");
                flyingStuckRecoveryPhase = FlyingStuckRecoveryPhase.Automoving;
                flyingStuckRecoveryPhaseStartMs = now;
                StateDetail = $"Flying stuck escape: automove ({FlyingStuckAutomoveMs / 1000}s)";
                return true;

            case FlyingStuckRecoveryPhase.Automoving:
                if (elapsed < FlyingStuckAutomoveMs)
                {
                    StateDetail = $"Flying stuck escape: automove ({Math.Max(0, (FlyingStuckAutomoveMs - elapsed) / 1000)}s)";
                    return true;
                }

                SendAutomoveOff("forward phase complete", force: true);
                flyingStuckRecoveryPhase = FlyingStuckRecoveryPhase.None;
                flyingStuckRecoveryPhaseStartMs = 0;
                ResetFlyingStuckTracking(Plugin.ObjectTable.LocalPlayer?.Position ?? default, now);
                Plugin.Log.Information("[FR][FlyingStuck] Recovery complete; normal pathfinding will resume.");
                return false;

            default:
                return false;
        }
    }

    private void StartFlyingStuckRecovery(long now)
    {
        if (!plugin.ECommonsAvailable)
        {
            LogEcommonsUnavailableOnce();
            return;
        }

        flyingStuckRecoveryPhase = FlyingStuckRecoveryPhase.Ascending;
        flyingStuckRecoveryPhaseStartMs = now;
        SendAutomoveOff("recovery start", force: true);
        if (!HoldAscendKey("recovery start"))
        {
            flyingStuckRecoveryPhase = FlyingStuckRecoveryPhase.None;
            flyingStuckRecoveryPhaseStartMs = 0;
            ResetFlyingStuckTracking(Plugin.ObjectTable.LocalPlayer?.Position ?? default, now);
        }
    }

    private bool ShouldTrackFlyingStuck(CharacterConfig config, bool selfFlying, bool isFrenFollowTarget)
    {
        if (!plugin.ECommonsAvailable)
        {
            LogEcommonsUnavailableOnce();
            return false;
        }

        return config.Enabled
            && isFrenFollowTarget
            && isNavigating
            && lastNavigationWasFrenFollow
            && lastNavigationWasFlying
            && lastMovementClingType == 0
            && selfFlying
            && (Plugin.Condition[ConditionFlag.Mounted] || Plugin.Condition[ConditionFlag.Diving])
            && !IsLoadingOrBetweenAreas()
            && !plugin.AdsIntegrationService.ShouldPauseDutySystems
            && !plugin.AutomationService.IsUtilityGateActive
            && GetMovementClingType(config) != 2
            && zoneService.CurrentZone != ZoneType.Foray;
    }

    private bool IsFlyingStuckRecoveryAllowed(CharacterConfig config, bool selfMounted, bool selfFlying)
    {
        return plugin.ECommonsAvailable
            && config.Enabled
            && (selfMounted || Plugin.Condition[ConditionFlag.Diving])
            && selfFlying
            && !IsLoadingOrBetweenAreas()
            && !plugin.AdsIntegrationService.ShouldPauseDutySystems
            && !plugin.AutomationService.IsUtilityGateActive;
    }

    private static bool IsLoadingOrBetweenAreas()
    {
        return Plugin.Condition[ConditionFlag.BetweenAreas]
            || Plugin.Condition[ConditionFlag.BetweenAreas51];
    }

    private void ResetFlyingStuckTrackingIfIneligible(
        CharacterConfig config,
        Vector3 position,
        bool selfFlying,
        bool isFrenFollowTarget,
        long now)
    {
        if (ShouldTrackFlyingStuck(config, selfFlying, isFrenFollowTarget))
            ResetFlyingStuckTracking(position, now);
        else
            ResetFlyingStuckTracking();
    }

    private void ResetFlyingStuckTracking()
    {
        flyingStuckBaselinePosition = default;
        flyingStuckBaselineTimeMs = 0;
    }

    private void ResetFlyingStuckTracking(Vector3 position, long now)
    {
        flyingStuckBaselinePosition = position;
        flyingStuckBaselineTimeMs = now;
    }

    private bool HoldAscendKey(string reason)
    {
        try
        {
            var sent = WindowsKeypress.SendKeyHold(VirtualKey.SPACE, Array.Empty<VirtualKey>());
            flyingStuckAscendHeld = sent;
            if (sent)
                Plugin.Log.Information($"[FR][FlyingStuck] Holding ascend: {reason}");
            else
                Plugin.Log.Warning($"[FR][FlyingStuck] Failed to hold ascend: {reason}");
            return sent;
        }
        catch (Exception ex)
        {
            flyingStuckAscendHeld = false;
            Plugin.Log.Warning(ex, $"[FR][FlyingStuck] Failed to hold ascend: {reason}");
            return false;
        }
    }

    private void ReleaseAscendKey(string reason)
    {
        if (!flyingStuckAscendHeld)
            return;

        try
        {
            WindowsKeypress.SendKeyRelease(VirtualKey.SPACE, Array.Empty<VirtualKey>());
            Plugin.Log.Information($"[FR][FlyingStuck] Released ascend: {reason}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, $"[FR][FlyingStuck] Failed to release ascend: {reason}");
        }
        finally
        {
            flyingStuckAscendHeld = false;
        }
    }

    private void SendAutomoveOn(string reason)
    {
        if (flyingStuckAutomoveOn)
            return;

        SendCommand("/automove on");
        flyingStuckAutomoveOn = true;
        Plugin.Log.Information($"[FR][FlyingStuck] Automove on: {reason}");
    }

    private void SendAutomoveOff(string reason, bool force)
    {
        if (!force && !flyingStuckAutomoveOn)
            return;

        SendCommand("/automove off");
        flyingStuckAutomoveOn = false;
        Plugin.Log.Information($"[FR][FlyingStuck] Automove off: {reason}");
    }

    private void LogEcommonsUnavailableOnce()
    {
        if (flyingStuckLoggedEcommonsUnavailable)
            return;

        flyingStuckLoggedEcommonsUnavailable = true;
        Plugin.Log.Warning("[FR][FlyingStuck] ECommons is unavailable; flying stuck escape disabled.");
    }

    private void LogFateFollowDecisionIfChanged(CharacterConfig config, FrenTracker.FrenState? fren, string stopReason)
    {
        if (!zoneService.FateChanged)
            return;

        var distance = fren?.Distance;
        var cling = GetEffectiveClingDistance(config);
        var maxDistance = GetMaxDistance(config);
        var followMode = DescribeMovementMode(GetMovementClingType(config));
        var inCombat = Plugin.Condition[ConditionFlag.InCombat];
        var selfMounted = Plugin.Condition[ConditionFlag.Mounted];
        var selfFlying = IsSelfFlightNavActive();
        var fateText = zoneService.InFate
            ? $"entered:{zoneService.CurrentFateId}"
            : $"left:{zoneService.PreviousFateId}";

        Plugin.Log.Information(
            $"[FR][FATE] FollowDecision fate={fateText}; territory={zoneService.TerritoryId}; distance={FormatNullableDistance(distance)}; cling={cling:F1}; max={maxDistance:F1}; combat={inCombat}; selfMounted={selfMounted}; selfFlying={selfFlying}; frenMounted={fren?.IsMounted}; frenFlying={fren?.IsFlying}; mode={followMode}; state={State}; stopReason={stopReason}");
    }

    private static string FormatNullableDistance(float? distance)
    {
        return distance.HasValue
            ? $"{distance.Value:F1}"
            : "unknown";
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

    private void SendNavigationCommand(int clingType, Vector3 target, string reason, Vector3 previousTarget, bool selfFlying)
    {
        var typeName = DescribeMovementMode(clingType);

        var coords = FormatVector(target);
        var cmd = typeName switch
        {
            "NavMesh" => $"/vnav moveto {coords}",
            "Visland" => $"/visland moveto {coords}",
            "Vanilla Follow" => "/follow",
            _ => null,
        };

        if (cmd != null)
        {
            LogNavCommand(reason, clingType, cmd, target, previousTarget, selfFlying);
            SendCommand(cmd);
        }
    }

    private void EnsureBossModFollow(CharacterConfig config, FrenTracker.FrenState fren)
    {
        if (plugin.CombatService.IsQuestionableSoloAuthorityActive)
            return;

        var targetName = fren.Name.Trim();
        if (string.IsNullOrWhiteSpace(targetName))
            return;

        var signatureMatches = bossModFollowActive
            && string.Equals(bossModFollowTarget, targetName, StringComparison.Ordinal)
            && bossModFollowTerritoryId == zoneService.TerritoryId
            && bossModFollowCombatMode == config.FollowInCombat
            && bossModFollowFrenFlying == fren.IsFlying
            && bossModFollowSelfFlying == IsSelfFlightNavActive();

        if (signatureMatches)
            return;

        if (bossModFollowActive)
            StopBossModFollow();

        if (isNavigating)
            StopNavigation(config, "switching to BossMod follow");

        plugin.CaptureExternalAutomationSnapshot("BossMod follow start");
        SendCommand($"/bmrai follow {targetName}");
        SendCommand("/bmrai followoutofcombat on");

        if (config.FollowInCombat == 0)
        {
            SendCommand("/bmrai followcombat off");
            SendCommand("/bmrai followmodule off");
        }
        else
        {
            SendCommand("/bmrai followcombat on");
            SendCommand("/bmrai followmodule on");
        }

        bossModFollowActive = true;
        bossModFollowTarget = targetName;
        bossModFollowTerritoryId = zoneService.TerritoryId;
        bossModFollowCombatMode = config.FollowInCombat;
        bossModFollowFrenFlying = fren.IsFlying;
        bossModFollowSelfFlying = IsSelfFlightNavActive();
        Plugin.Log.Information($"[FR] Activated BossMod follow for '{targetName}' in territory {zoneService.TerritoryId}");
    }

    private void StopBossModFollow()
    {
        if (!bossModFollowActive)
            return;

        if (plugin.CombatService.IsQuestionableSoloAuthorityActive)
        {
            ClearBossModFollowState();
            Plugin.Log.Information("[FR] Relinquished local BossMod follow state under QuestionableSolo authority");
            return;
        }

        plugin.CaptureExternalAutomationSnapshot("BossMod follow stop");
        SendCommand("/bmrai followoutofcombat off");
        SendCommand("/bmrai followcombat off");
        SendCommand("/bmrai followmodule off");
        ClearBossModFollowState();
        Plugin.Log.Information("[FR] Stopped BossMod follow");
    }

    private void ClearBossModFollowState()
    {
        bossModFollowActive = false;
        bossModFollowTarget = string.Empty;
        bossModFollowTerritoryId = 0;
        bossModFollowCombatMode = -1;
        bossModFollowFrenFlying = false;
        bossModFollowSelfFlying = false;
    }

    private void StopAllFollowing(CharacterConfig config, string reason)
    {
        CancelFlyingStuckRecovery(reason);
        ResetFlyingTakeoffState();
        ResetStuckFollowJumpTracking();
        StopBossModFollow();
        if (isNavigating)
            StopNavigation(config, reason);
    }

    private void StopNavigation(CharacterConfig config, string reason)
    {
        if (!isNavigating) return;

        var cmd = lastMovementClingType switch
        {
            0 => "/vnavmesh stop",
            1 => "/visland stop",
            3 => "/follow",
            _ => "/vnavmesh stop",
        };

        Plugin.Log.Information(
            $"[FR][Pathing] StopNavigation reason={reason}; mode={DescribeMovementMode(lastMovementClingType)}; territory={zoneService.TerritoryId}; cmd={cmd}; lastTarget={FormatVector(lastNavTarget)}");
        isNavigating = false;
        lastNavigationWasFlying = false;
        lastNavigationWasFrenFollow = false;
        ResetFlyingIdleSampler();
        SendCommand(cmd);
    }

    private void LogNavCommand(string reason, int clingType, string command, Vector3 target, Vector3 previousTarget, bool selfFlying)
    {
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        var localPosition = localPlayer?.Position ?? default;
        var localToTarget = localPlayer != null ? Vector3.Distance(localPosition, target) : -1f;
        var targetDelta = Vector3.Distance(previousTarget, target);

        Plugin.Log.Information(
            $"[FR][Pathing] IssueNav reason={reason}; mode={DescribeMovementMode(clingType)}; territory={zoneService.TerritoryId}; selfFlying={selfFlying}; cmd={command}; local={FormatVector(localPosition)}; target={FormatVector(target)}; previousTarget={FormatVector(previousTarget)}; localToTarget={localToTarget:F1}; targetDelta={targetDelta:F1}");
    }

    private static string DescribeMovementMode(int clingType)
    {
        return clingType >= 0 && clingType < ClingTypeNames.Length
            ? ClingTypeNames[clingType]
            : $"Unknown({clingType})";
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
