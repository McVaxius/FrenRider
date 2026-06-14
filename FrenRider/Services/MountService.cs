using System;
using System.Linq;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FrenRider.Models;

namespace FrenRider.Services;

public enum MountState
{
    Idle,           // Not doing anything mount-related
    WaitingToMount, // Fren mounted, waiting for proximity before mounting
    Mounting,       // In the process of mounting
    Mounted,        // On own mount or riding pillion
    Dismounting,    // Fren dismounted, dismounting self
}

public class MountService
{
    private readonly Plugin plugin;
    private readonly FrenTracker tracker;
    private readonly ZoneService zoneService;

    private bool wasFrenMounted;
    private long mountCooldownMs;
    private bool farChaseMountPending;
    private bool farChaseMountOwned;
    private bool wasFarChaseRequested;
    private long farChaseMountPendingUntilMs;
    private string lastDesiredMountStateLogKey = "";

    public MountState State { get; private set; } = MountState.Idle;
    public string StateDetail { get; private set; } = "";
    public bool IsFarChaseMountOwned => farChaseMountOwned;

    public MountService(Plugin plugin, FrenTracker tracker, ZoneService zoneService)
    {
        this.plugin = plugin;
        this.tracker = tracker;
        this.zoneService = zoneService;
    }

    public void PreemptFarChase(string reason)
    {
        ClearFarChaseMountPending();
        wasFarChaseRequested = false;
        State = MountState.Idle;
        StateDetail = $"Far chase preempted: {reason}";
        lastDesiredMountStateLogKey = "";
    }

    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var now = Environment.TickCount64;
        var selfRidingPillion = Plugin.Condition[ConditionFlag.RidingPillion];
        var selfOnOwnMount = Plugin.Condition[ConditionFlag.Mounted] && !selfRidingPillion;
        UpdateFarChaseMountOwnership(selfOnOwnMount, now);
        UpdateFarChaseTransition(plugin.FollowService.IsFarChaseRequested);

        if (!config.Enabled)
        {
            PreemptFarChase("disabled");
            State = MountState.Idle;
            StateDetail = "Disabled";
            wasFrenMounted = false;
            return;
        }

        if (plugin.AdsIntegrationService.ShouldPauseDutySystems
            || plugin.AdsIntegrationService.IsHandoffPending)
        {
            PreemptFarChase("ADS authority active");
            lastDesiredMountStateLogKey = "";
            State = MountState.Idle;
            StateDetail = plugin.AdsIntegrationService.IsHandoffPending
                ? "ADS handoff pending"
                : "ADS active";
            return;
        }

        if (plugin.AutomationService.IsUtilityGateActive)
        {
            lastDesiredMountStateLogKey = "";
            State = MountState.Idle;
            StateDetail = "ADS utility active";
            return;
        }

        if (Plugin.Condition[ConditionFlag.Unconscious])
        {
            lastDesiredMountStateLogKey = "";
            State = MountState.Idle;
            StateDetail = "Unconscious";
            return;
        }

        var fren = tracker.Fren;
        if (fren == null || !fren.IsFound || !fren.IsVisible)
        {
            lastDesiredMountStateLogKey = "";
            State = MountState.Idle;
            StateDetail = "No fren";
            wasFrenMounted = false;
            return;
        }

        var selfOnMountOrPillion = Plugin.Condition[ConditionFlag.Mounted]
            || Plugin.Condition[ConditionFlag.RidingPillion];
        var selfMountBusy = selfOnMountOrPillion
            || Plugin.Condition[ConditionFlag.Mounting71];
        var selfFlying = Plugin.Condition[ConditionFlag.InFlight];
        var selfAirborne = selfFlying || Plugin.Condition[ConditionFlag.Diving];
        var inCombat = Plugin.Condition[ConditionFlag.InCombat];
        var desiredMountState = GetDesiredMountState(config, fren, out var correctionReason);
        LogDesiredMountStateIfChanged(desiredMountState, correctionReason);

        if (plugin.FollowService.IsFarChaseRequested)
        {
            if (selfRidingPillion)
            {
                if (now >= mountCooldownMs)
                {
                    State = MountState.Dismounting;
                    StateDetail = "Far chase: leaving pillion";
                    DismountSelf();
                }
                else
                {
                    State = MountState.WaitingToMount;
                    StateDetail = $"Far chase pillion cooldown ({(mountCooldownMs - now) / 1000.0:F1}s)";
                }
                return;
            }

            if (selfOnOwnMount)
            {
                State = MountState.Mounted;
                StateDetail = selfFlying
                    ? "Far chase: flying"
                    : "Far chase: mounted; taking off";
                return;
            }

            if (selfMountBusy || farChaseMountPending)
            {
                State = MountState.Mounting;
                StateDetail = "Far chase: mounting own mount";
                return;
            }

            if (!CanIssueFarChaseMountCommand(config, out var blockReason))
            {
                State = MountState.Idle;
                StateDetail = $"Far chase blocked: {blockReason}";
                return;
            }

            if (now >= mountCooldownMs)
            {
                farChaseMountPending = true;
                farChaseMountPendingUntilMs = now + 5000;
                MountOwnMount(config, "Far chase");
            }
            else
            {
                State = MountState.WaitingToMount;
                StateDetail = $"Far chase mount cooldown ({(mountCooldownMs - now) / 1000.0:F1}s)";
            }
            return;
        }

        if (selfOnOwnMount
            && desiredMountState is FrenMountPolicy.OnFoot or FrenMountPolicy.Pillion)
        {
            var correctionAllowed = CanSafelyCorrectOwnMount(config, fren, out var retainReason);
            var correctionAction = FrenRiderMountPolicy.GetCorrectionAction(
                selfOnOwnMount,
                desiredMountState,
                correctionAllowed,
                selfAirborne);
            if (correctionAction != FrenMountCorrectionAction.None)
            {
                if (now >= mountCooldownMs)
                {
                    if (correctionAction == FrenMountCorrectionAction.Land)
                    {
                        LandSelf();
                        StateDetail = desiredMountState == FrenMountPolicy.Pillion
                            ? "Near mounted fren; landing to ride pillion"
                            : "Near fren on foot; landing to dismount";
                    }
                    else
                    {
                        DismountSelf();
                        StateDetail = desiredMountState == FrenMountPolicy.Pillion
                            ? "Near mounted fren; dismounting to ride pillion"
                            : "Near fren on foot; dismounting";
                    }
                }
                else
                {
                    State = MountState.Dismounting;
                    StateDetail = $"Mount correction cooldown ({(mountCooldownMs - now) / 1000.0:F1}s)";
                }
            }
            else
            {
                State = MountState.Mounted;
                StateDetail = $"Own mount retained: {retainReason}";
            }
            return;
        }

        if (desiredMountState == FrenMountPolicy.PreserveCurrent)
        {
            State = selfOnMountOrPillion ? MountState.Mounted : MountState.Idle;
            StateDetail = $"Mount state preserved: {correctionReason}";
            return;
        }

        // MOUNT LOGIC: If fren is mounted, mount up or ride pillion
        if (fren.IsMounted && !selfMountBusy && !inCombat)
        {
            if (!wasFrenMounted)
            {
                wasFrenMounted = true;
                Plugin.Log.Information($"Fren mounted (MountId={fren.MountId}), will {(config.FlyYouFools ? "mount self" : "ride pillion")}");
            }
            
            if (now >= mountCooldownMs)
            {
                State = MountState.Mounting;
                StateDetail = config.FlyYouFools ? "Fren mounted (FlyYouFools), mounting..." : "Fren mounted, riding pillion...";
                MountSelf(config);
            }
            else
            {
                State = MountState.WaitingToMount;
                StateDetail = $"Mount cooldown ({(mountCooldownMs - now) / 1000.0:F1}s)";
            }
            return;
        }

        // Track fren mount state
        if (fren.IsMounted && !wasFrenMounted)
        {
            wasFrenMounted = true;
            Plugin.Log.Information($"Fren mounted (MountId={fren.MountId})");
        }
        else if (!fren.IsMounted && wasFrenMounted)
        {
            wasFrenMounted = false;
            Plugin.Log.Information("Fren dismounted");
        }

        // Update ongoing state
        if (fren.IsMounted && selfOnMountOrPillion)
        {
            State = MountState.Mounted;
            StateDetail = selfRidingPillion
                ? "Riding pillion on fren's mount"
                : selfFlying
                    ? "Flying alongside fren"
                    : "Mounted alongside fren";
        }
        else if (fren.IsMounted && selfMountBusy)
        {
            State = MountState.Mounting;
            StateDetail = "Mounting...";
        }
        else if (fren.IsMounted && !selfMountBusy && config.FlyYouFools)
        {
            // We should be mounted but aren't - waiting for cooldown
            State = MountState.WaitingToMount;
            StateDetail = "Waiting to mount...";
        }
        else
        {
            State = MountState.Idle;
            StateDetail = "";
        }

        // Companion summoning is handled by AutomationService.CheckCompanion()
    }

    private void MountSelf(CharacterConfig config)
    {
        mountCooldownMs = Environment.TickCount64 + 2000; // 2s cooldown

        if (config.FlyYouFools)
        {
            MountOwnMount(config, "Fly You Fools");
        }
        else
        {
            // Pillion riding: target fren and ride pillion
            var fren = tracker.Fren;
            if (fren != null && fren.IsFound)
            {
                // Find fren in ObjectTable and set as target
                var frenObj = Plugin.ObjectTable.FirstOrDefault(obj => 
                    obj != null && obj.Name.ToString() == fren.Name);
                
                if (frenObj != null)
                {
                    Plugin.TargetManager.Target = frenObj;
                    Plugin.Log.Information($"Targeted fren: {fren.Name}");
                    
                    // Send pillion command
                    SendCommand("/ridepillion <t> 1");
                    State = MountState.Mounting;
                    StateDetail = "Riding pillion on fren's mount";
                }
                else
                {
                    State = MountState.Idle;
                    StateDetail = "Can't pillion: fren not in ObjectTable";
                    Plugin.Log.Warning($"Fren {fren.Name} not found in ObjectTable for targeting");
                }
            }
            else
            {
                State = MountState.Idle;
                StateDetail = "Can't pillion: fren not found";
            }
        }
    }

    private void MountOwnMount(CharacterConfig config, string reason)
    {
        var mountName = config.FoolFlier;
        mountCooldownMs = Environment.TickCount64 + 2000;

        if (mountName == "Mount Roulette")
        {
            SendCommand("/generalaction \"Mount Roulette\"");
        }
        else if (string.IsNullOrEmpty(mountName))
        {
            mountName = "Company Chocobo";
            SendCommand("/mount \"Company Chocobo\"");
        }
        else
        {
            SendCommand($"/mount \"{mountName}\"");
        }

        State = MountState.Mounting;
        StateDetail = $"{reason}: mounting {mountName}";
    }

    private void UpdateFarChaseMountOwnership(bool selfOnOwnMount, long now)
    {
        if (farChaseMountPending && selfOnOwnMount)
        {
            farChaseMountPending = false;
            farChaseMountOwned = true;
            farChaseMountPendingUntilMs = 0;
            Plugin.Log.Information("[FR][FarChase] Own mount acquired");
            return;
        }

        if (farChaseMountOwned && !selfOnOwnMount)
        {
            farChaseMountOwned = false;
            Plugin.Log.Information("[FR][FarChase] Own mount released after dismount");
        }

        if (farChaseMountPending && now >= farChaseMountPendingUntilMs)
        {
            farChaseMountPending = false;
            farChaseMountPendingUntilMs = 0;
            Plugin.Log.Information("[FR][FarChase] Own mount request timed out");
        }
    }

    private void UpdateFarChaseTransition(bool requested)
    {
        if (requested)
        {
            wasFarChaseRequested = true;
            return;
        }

        if (!wasFarChaseRequested)
            return;

        wasFarChaseRequested = false;
        ClearFarChaseMountPending();
        Plugin.Log.Information(
            $"[FR][FarChase] Mount handoff retired; ownMountTracked={farChaseMountOwned}");
    }

    private void ClearFarChaseMountPending()
    {
        farChaseMountPending = false;
        farChaseMountPendingUntilMs = 0;
    }

    private bool CanIssueFarChaseMountCommand(CharacterConfig config, out string reason)
    {
        if (Plugin.Condition[ConditionFlag.InCombat])
        {
            reason = "in combat";
            return false;
        }

        if (Plugin.Condition[ConditionFlag.BoundByDuty]
            || Plugin.Condition[ConditionFlag.BoundByDuty56])
        {
            reason = "in duty";
            return false;
        }

        if (Plugin.Condition[ConditionFlag.BetweenAreas]
            || Plugin.Condition[ConditionFlag.BetweenAreas51])
        {
            reason = "area transition";
            return false;
        }

        if (Plugin.Condition[ConditionFlag.Unconscious])
        {
            reason = "unconscious";
            return false;
        }

        if (config.Formation)
        {
            reason = "formation";
            return false;
        }

        if (plugin.AdsIntegrationService.ShouldPauseDutySystems
            || plugin.AdsIntegrationService.IsHandoffPending)
        {
            reason = "ADS";
            return false;
        }

        if (plugin.AutomationService.IsUtilityGateActive)
        {
            reason = "repair";
            return false;
        }

        if (HasTeleportOrDialogActivity())
        {
            reason = "teleport or dialog";
            return false;
        }

        return GameHelpers.CanUseMountActionNow(out reason);
    }

    private FrenMountPolicy GetDesiredMountState(
        CharacterConfig config,
        FrenTracker.FrenState fren,
        out string reason)
    {
        if (plugin.FollowService.IsFarChaseRequested)
        {
            reason = "far chase active";
            return FrenMountPolicy.OwnMount;
        }

        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        var clingDistance = plugin.FollowService.GetEffectiveClingDistance(config);
        var xzDistance = localPlayer == null
            ? float.PositiveInfinity
            : FollowService.GetXzDistance(localPlayer.Position, fren.Position);
        var desiredPolicy = FrenRiderMountPolicy.GetNormalPolicy(
            plugin.FollowService.IsFarChaseRequested,
            localPlayer != null,
            xzDistance,
            clingDistance,
            fren.IsMounted,
            config.FlyYouFools);

        if (desiredPolicy == FrenMountPolicy.PreserveCurrent)
        {
            reason = localPlayer == null
                ? "local player unavailable"
                : "outside effective cling range";
            return desiredPolicy;
        }

        if (desiredPolicy == FrenMountPolicy.OnFoot)
        {
            reason = "near fren on foot";
            return desiredPolicy;
        }

        if (desiredPolicy == FrenMountPolicy.OwnMount)
        {
            reason = "near mounted fren with Fly You Fools enabled";
            return desiredPolicy;
        }

        reason = "near mounted fren with Fly You Fools disabled";
        return desiredPolicy;
    }

    private void LogDesiredMountStateIfChanged(FrenMountPolicy desiredMountState, string reason)
    {
        var logKey = $"{desiredMountState}:{reason}";
        if (logKey == lastDesiredMountStateLogKey)
            return;

        lastDesiredMountStateLogKey = logKey;
        Plugin.Log.Information(
            $"[FR][MountCorrection] reason={reason}; desired={desiredMountState}");
    }

    private bool CanSafelyCorrectOwnMount(
        CharacterConfig config,
        FrenTracker.FrenState fren,
        out string reason)
    {
        if (plugin.FollowService.IsFarChaseRequested)
        {
            reason = "far chase active";
            return false;
        }

        if (!fren.IsFound || !fren.IsVisible)
        {
            reason = "fren unavailable";
            return false;
        }

        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null)
        {
            reason = "local player unavailable";
            return false;
        }

        var clingDistance = plugin.FollowService.GetEffectiveClingDistance(config);
        var xzDistance = FollowService.GetXzDistance(localPlayer.Position, fren.Position);
        if (xzDistance > clingDistance)
        {
            reason = $"{xzDistance:F1}y XZ from fren, handoff {clingDistance:F1}y";
            return false;
        }

        if (fren.IsFlying)
        {
            reason = "fren flying";
            return false;
        }

        if (Plugin.Condition[ConditionFlag.InCombat])
        {
            reason = "in combat";
            return false;
        }

        if (Plugin.Condition[ConditionFlag.Unconscious]
            || Plugin.Condition[ConditionFlag.Mounting71])
        {
            reason = "player busy";
            return false;
        }

        if (zoneService.CurrentZone == ZoneType.Duty
            || Plugin.Condition[ConditionFlag.BoundByDuty]
            || Plugin.Condition[ConditionFlag.BoundByDuty56]
            || Plugin.Condition[ConditionFlag.BetweenAreas]
            || Plugin.Condition[ConditionFlag.BetweenAreas51])
        {
            reason = "duty or transition";
            return false;
        }

        if (localPlayer?.IsCasting == true
            || Plugin.Condition[ConditionFlag.OccupiedInQuestEvent]
            || Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]
            || Plugin.Condition[ConditionFlag.Occupied33]
            || Plugin.Condition[ConditionFlag.Occupied39]
            || Plugin.Condition[ConditionFlag.WatchingCutscene])
        {
            reason = "casting or occupied";
            return false;
        }

        if (config.Formation
            || plugin.AdsIntegrationService.ShouldPauseDutySystems
            || plugin.AdsIntegrationService.IsHandoffPending
            || plugin.AutomationService.IsUtilityGateActive
            || HasTeleportOrDialogActivity())
        {
            reason = "automation preempted";
            return false;
        }

        reason = "safe and in cling range";
        return true;
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

    private void DismountSelf()
    {
        mountCooldownMs = Environment.TickCount64 + 1500; // 1.5s cooldown
        // /mount toggles mount on/off - when mounted, it dismounts
        SendCommand("/mount");
        State = MountState.Dismounting;
        StateDetail = "Dismounting...";
    }

    private void LandSelf()
    {
        mountCooldownMs = Environment.TickCount64 + 1000;
        SendCommand("/mount");
        State = MountState.Dismounting;
        StateDetail = "Landing...";
    }

    /// <summary>
    /// Summon chocobo companion via Gysahl Greens (manual trigger).
    /// Auto-summoning is handled by AutomationService.CheckCompanion().
    /// </summary>
    public void SummonCompanion(CharacterConfig config)
    {
        if (GameHelpers.GetInventoryItemCount(GameHelpers.GysahlGreensItemId) <= 0)
        {
            Plugin.Log.Warning("SummonCompanion: No Gysahl Greens in inventory");
            return;
        }

        Plugin.Log.Information("SummonCompanion: Using Gysahl Greens");
        GameHelpers.UseItem(GameHelpers.GysahlGreensItemId);
    }

    private static unsafe void SendCommand(string command)
    {
        try
        {
            Plugin.Log.Information($"MountService sending command: {command}");
            
            // Use UIModule to send command directly to game
            var uiModule = UIModule.Instance();
            if (uiModule == null)
            {
                Plugin.Log.Error("UIModule is null, cannot send command");
                return;
            }

            // Create Utf8String for the command
            var bytes = Encoding.UTF8.GetBytes(command);
            var utf8String = Utf8String.FromSequence(bytes);
            
            // Send command through ProcessChatBoxEntry
            uiModule->ProcessChatBoxEntry(utf8String, nint.Zero);
            
            Plugin.Log.Information($"Mount command sent to game: {command}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Mount command failed [{command}]: {ex.Message}");
        }
    }
}
