using System;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FrenRider.Models;

namespace FrenRider.Services;

public enum RespawnState
{
    Off,
    Idle,
    Waiting,
    Returning,
    Blocked,
}

public sealed class RespawnService
{
    private const long ActionThrottleMs = 1000;

    private readonly Plugin plugin;
    private readonly RespawnNotificationRecoveryPolicy notificationRecovery = new();
    private long unconsciousStartedMs;
    private long lastActionMs;
    private bool settingsInitialized;
    private bool lastEnabled;
    private int lastDelaySeconds;

    public RespawnState State { get; private set; } = RespawnState.Off;
    public string StatusText { get; private set; } = "Off";
    public bool OwnsUnconsciousReviveFlow { get; private set; }

    public RespawnService(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var delaySeconds = Math.Max(1, config.RespawnOutsideDutiesDelaySeconds);

        if (SettingsChanged(config.RespawnOutsideDuties, delaySeconds))
        {
            ResetTimer();
            SetState(config.RespawnOutsideDuties ? RespawnState.Idle : RespawnState.Off, "Setting changed");
            return;
        }

        if (!Plugin.ClientState.IsLoggedIn)
        {
            Reset(RespawnState.Off, "Not logged in");
            return;
        }

        if (!config.Enabled)
        {
            Reset(RespawnState.Off, "FrenRider disabled");
            return;
        }

        if (plugin.AutomationService.IsUtilityGateActive)
        {
            Reset(RespawnState.Blocked, "Blocked: ADS utility active");
            return;
        }

        if (!config.RespawnOutsideDuties)
        {
            Reset(RespawnState.Off, "Off");
            return;
        }

        if (IsInDuty())
        {
            Reset(RespawnState.Blocked, "Blocked: in duty");
            return;
        }

        if (IsAreaTransitionActive())
        {
            Reset(RespawnState.Blocked, "Blocked: area transition");
            return;
        }

        if (!Plugin.Condition[ConditionFlag.Unconscious])
        {
            Reset(RespawnState.Idle, "Waiting for death");
            return;
        }

        var now = Environment.TickCount64;
        if (unconsciousStartedMs == 0)
        {
            unconsciousStartedMs = now;
            lastActionMs = 0;
            SetState(RespawnState.Waiting, $"Unconscious; return in {delaySeconds}s");
        }

        if (ShouldOwnCurrentUnconsciousReviveFlow(config))
        {
            OwnsUnconsciousReviveFlow = true;
            if (State != RespawnState.Returning)
                SetState(RespawnState.Returning, "Handling revive/Return notification");

            HandleReviveNotificationFlow(now);
            return;
        }

        ClearNotificationRecovery();

        var delayMs = delaySeconds * 1000L;
        var elapsedMs = now - unconsciousStartedMs;
        if (elapsedMs < delayMs)
        {
            var remainingSeconds = Math.Max(1, (int)Math.Ceiling((delayMs - elapsedMs) / 1000.0));
            SetState(RespawnState.Waiting, $"Unconscious; return in {remainingSeconds}s");
            return;
        }

        if (State != RespawnState.Returning)
            SetState(RespawnState.Returning, "Opening Return prompt");
        if (now - lastActionMs < ActionThrottleMs)
            return;

        lastActionMs = now;
        TryReturn();
    }

    public void ResetForAreaTransition()
        => Reset(RespawnState.Blocked, "Blocked: area transition");

    public void ResetForDisable()
        => Reset(RespawnState.Off, "FrenRider disabled");

    private bool SettingsChanged(bool enabled, int delaySeconds)
    {
        if (!settingsInitialized)
        {
            settingsInitialized = true;
            lastEnabled = enabled;
            lastDelaySeconds = delaySeconds;
            return false;
        }

        if (lastEnabled == enabled && lastDelaySeconds == delaySeconds)
            return false;

        lastEnabled = enabled;
        lastDelaySeconds = delaySeconds;
        return true;
    }

    private static bool IsInDuty()
        => Plugin.Condition[ConditionFlag.BoundByDuty]
            || Plugin.Condition[ConditionFlag.BoundByDuty56];

    private static bool IsAreaTransitionActive()
        => Plugin.Condition[ConditionFlag.BetweenAreas]
            || Plugin.Condition[ConditionFlag.BetweenAreas51];

    public bool ShouldOwnCurrentUnconsciousReviveFlow(CharacterConfig config)
        => RespawnNotificationRecoveryPolicy.ShouldOwnFlow(
            Plugin.ClientState.IsLoggedIn,
            config.Enabled,
            config.RespawnOutsideDuties,
            plugin.AutomationService.IsUtilityGateActive,
            IsInDuty(),
            IsAreaTransitionActive(),
            Plugin.Condition[ConditionFlag.Unconscious],
            GameHelpers.IsAddonVisible("_NotificationRevive"));

    private void HandleReviveNotificationFlow(long now)
    {
        var reviveVisible = GameHelpers.IsAddonVisible("_NotificationRevive");
        var telepoVisible = GameHelpers.IsAddonVisible("_NotificationTelepo");
        var action = notificationRecovery.GetNextAction(now, reviveVisible, telepoVisible);

        switch (action)
        {
            case RespawnNotificationRecoveryAction.None:
                StatusText = "Waiting for revive/Return notification";
                return;

            case RespawnNotificationRecoveryAction.ToggleNotification:
                var callbackDispatched = GameHelpers.TryFireAddonCallback(
                    "_Notification",
                    true,
                    out var callbackFailureReason,
                    0,
                    16);
                notificationRecovery.RecordToggle(now);

                var callbackFailure = string.IsNullOrEmpty(callbackFailureReason) ? "none" : callbackFailureReason;
                Plugin.Log.Debug($"[Respawn] _NotificationTelepo blocks revive/Return; callback addon=_Notification; updateState=true; args=[Int=0, Int=16]; callback dispatched={callbackDispatched.ToString().ToLowerInvariant()}; callbackFailureReason={callbackFailure}");
                StatusText = callbackDispatched
                    ? "Surfacing revive/Return prompt"
                    : "Waiting for revive/Return prompt";
                return;

            case RespawnNotificationRecoveryAction.TryYes:
                var accepted = GameHelpers.ClickYesIfVisible(logClick: false);
                notificationRecovery.RecordYesAttempt(accepted, now);
                StatusText = accepted
                    ? "Revive/Return accepted"
                    : "Waiting for revive/Return confirmation";
                return;

            case RespawnNotificationRecoveryAction.OpenReturnPrompt:
                ClearNotificationRecovery();
                TryReturn();
                return;
        }
    }

    private unsafe void TryReturn()
    {
        if (GameHelpers.IsAddonVisible("SelectYesno"))
        {
            if (GameHelpers.ClickYesIfVisible(logClick: false))
                StatusText = "Return accepted; waiting for revive";
            return;
        }

        try
        {
            var agent = AgentRevive.Instance();
            if (agent == null)
            {
                StatusText = "Return agent unavailable";
                return;
            }

            if (!agent->IsAddonShown())
            {
                agent->ShowAddon();
                StatusText = "Opened Return prompt";
            }
            else
            {
                StatusText = "Waiting for Return confirmation";
            }
        }
        catch (Exception ex)
        {
            StatusText = "Return prompt failed";
            Plugin.Log.Warning(ex, "[Respawn] Failed to open Return prompt");
        }
    }

    private void Reset(RespawnState state, string status)
    {
        ResetTimer();
        SetState(state, status);
    }

    private void ResetTimer()
    {
        unconsciousStartedMs = 0;
        lastActionMs = 0;
        ClearNotificationRecovery();
    }

    private void ClearNotificationRecovery()
    {
        OwnsUnconsciousReviveFlow = false;
        notificationRecovery.Reset();
    }

    private void SetState(RespawnState state, string status)
    {
        var previous = State;
        State = state;
        StatusText = status;

        if (previous != state)
            Plugin.Log.Information($"[Respawn] State {previous} -> {state}: {status}");
    }
}
