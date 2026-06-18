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

        var now = Environment.TickCount64;

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
        TryReturn(now);
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
    {
        var visiblePromptKind = TryGetVisibleSelectYesnoPromptKind(out _);

        return RespawnNotificationRecoveryPolicy.ShouldOwnFlow(
            Plugin.ClientState.IsLoggedIn,
            config.Enabled,
            config.RespawnOutsideDuties,
            plugin.AutomationService.IsUtilityGateActive,
            IsInDuty(),
            IsAreaTransitionActive(),
            Plugin.Condition[ConditionFlag.Unconscious],
            notificationRecovery.HasPendingPromptAttempt
                || GameHelpers.IsAddonVisible("_NotificationRevive"),
            GameHelpers.IsAddonVisible("_NotificationTelepo"),
            visiblePromptKind);
    }

    private void HandleReviveNotificationFlow(long now)
    {
        var reviveVisible = GameHelpers.IsAddonVisible("_NotificationRevive");
        var telepoVisible = GameHelpers.IsAddonVisible("_NotificationTelepo");
        var selectYesnoVisible = GameHelpers.IsAddonVisible("SelectYesno");
        var visiblePromptKind = TryGetVisibleSelectYesnoPromptKind(out var promptText);
        var observation = notificationRecovery.ObservePrompt(
            now,
            selectYesnoVisible,
            visiblePromptKind,
            promptText);

        switch (observation.Outcome)
        {
            case RespawnPromptAttemptOutcome.Waiting:
                StatusText = BuildPromptAttemptWaitingStatus(observation.Attempt);
                return;

            case RespawnPromptAttemptOutcome.Confirmed:
                LogPromptAttemptConfirmation(observation.Attempt);
                if (observation.Attempt.ResponseYes)
                {
                    lastActionMs = now;
                    StatusText = "Revive/Return confirmed; waiting for transition";
                    return;
                }
                break;

            case RespawnPromptAttemptOutcome.TimedOut:
                LogPromptAttemptTimeout(observation.Attempt);
                break;
        }

        var action = notificationRecovery.GetNextAction(now, visiblePromptKind, reviveVisible, telepoVisible);

        switch (action)
        {
            case RespawnNotificationRecoveryAction.None:
                StatusText = visiblePromptKind.HasValue
                    ? $"Waiting for {visiblePromptKind.Value} SelectYesno dialog"
                    : "Waiting for revive/Return notification";
                return;

            case RespawnNotificationRecoveryAction.ExpandTeleportNotification:
                var callbackDispatched = GameHelpers.TryFireAddonCallback(
                    "_Notification",
                    true,
                    out var callbackFailureReason,
                    0,
                    16);
                notificationRecovery.RecordNotificationAction(now);

                var callbackFailure = string.IsNullOrEmpty(callbackFailureReason) ? "none" : callbackFailureReason;
                Plugin.Log.Debug($"[Respawn] _NotificationTelepo blocks revive/Return; callback addon=_Notification; updateState=true; args=[Int=0, Int=16]; callback dispatched={callbackDispatched.ToString().ToLowerInvariant()}; callbackFailureReason={callbackFailure}");
                StatusText = callbackDispatched
                    ? "Surfacing teleport prompt"
                    : "Waiting for teleport prompt";
                return;

            case RespawnNotificationRecoveryAction.ClickNo:
                AttemptPromptResponse(
                    responseYes: false,
                    SelectYesnoPromptKind.Teleport,
                    promptText,
                    now);
                return;

            case RespawnNotificationRecoveryAction.SurfaceRevivePrompt:
                var reviveCallbackDispatched = GameHelpers.TryFireAddonCallback(
                    "_Notification",
                    true,
                    out var reviveCallbackFailureReason,
                    0,
                    1,
                    2);
                notificationRecovery.RecordNotificationAction(now);

                var reviveCallbackFailure = string.IsNullOrEmpty(reviveCallbackFailureReason) ? "none" : reviveCallbackFailureReason;
                Plugin.Log.Debug($"[Respawn] Surfacing revive/Return prompt; callback addon=_Notification; updateState=true; args=[Int=0, Int=1, Int=2]; callback dispatched={reviveCallbackDispatched.ToString().ToLowerInvariant()}; callbackFailureReason={reviveCallbackFailure}");
                StatusText = reviveCallbackDispatched
                    ? "Surfacing revive/Return prompt"
                    : "Waiting for revive/Return prompt";
                return;

            case RespawnNotificationRecoveryAction.ClickYes:
                AttemptPromptResponse(
                    responseYes: true,
                    visiblePromptKind ?? SelectYesnoPromptKind.DeathReturn,
                    promptText,
                    now);
                return;

            case RespawnNotificationRecoveryAction.OpenReturnPrompt:
                ClearNotificationRecovery();
                TryReturn(now);
                return;
        }
    }

    private unsafe void TryReturn(long now)
    {
        if (GameHelpers.TryReadSelectYesnoPrompt(out var promptText))
        {
            var promptKind = SelectYesnoPromptClassifier.Classify(promptText);
            switch (promptKind)
            {
                case SelectYesnoPromptKind.Teleport:
                    AttemptPromptResponse(
                        responseYes: false,
                        promptKind,
                        promptText,
                        now);
                    return;

                case SelectYesnoPromptKind.DeathReturn:
                case SelectYesnoPromptKind.Raise:
                    AttemptPromptResponse(
                        responseYes: true,
                        promptKind,
                        promptText,
                        now);
                    return;

                default:
                    StatusText = $"Waiting for {promptKind} SelectYesno dialog";
                    return;
            }
        }

        if (GameHelpers.IsAddonVisible("SelectYesno"))
        {
            StatusText = "Waiting for readable Return confirmation";
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

    private void AttemptPromptResponse(
        bool responseYes,
        SelectYesnoPromptKind promptKind,
        string promptText,
        long now)
    {
        var callbackDispatched = responseYes
            ? GameHelpers.ClickYesIfVisible(logClick: false)
            : GameHelpers.ClickNoIfVisible(logClick: false);

        notificationRecovery.RecordPromptAttempt(
            callbackDispatched,
            promptKind,
            promptText,
            responseYes,
            now);

        var response = responseYes ? "Yes" : "No";
        Plugin.Log.Information(
            $"[Respawn] SelectYesno {response} attempt; kind={promptKind}; callback dispatched={callbackDispatched.ToString().ToLowerInvariant()}; prompt={promptText}");

        StatusText = callbackDispatched
            ? responseYes
                ? "Revive/Return accept attempted; waiting for dialog to close"
                : "Teleport decline attempted; waiting for dialog to close"
            : responseYes
                ? "Revive/Return accept callback failed; waiting to retry"
                : "Teleport decline callback failed; waiting to retry";
    }

    private static string BuildPromptAttemptWaitingStatus(RespawnPromptAttempt attempt)
        => attempt.ResponseYes
            ? "Waiting for revive/Return dialog to close"
            : "Waiting for teleport dialog to close";

    private static void LogPromptAttemptConfirmation(RespawnPromptAttempt attempt)
    {
        var response = attempt.ResponseYes ? "Yes" : "No";
        Plugin.Log.Information(
            $"[Respawn] SelectYesno {response} confirmed; dialog closed or changed; kind={attempt.PromptKind}; prompt={attempt.PromptText}");
    }

    private static void LogPromptAttemptTimeout(RespawnPromptAttempt attempt)
    {
        var response = attempt.ResponseYes ? "Yes" : "No";
        Plugin.Log.Warning(
            $"[Respawn] SelectYesno {response} attempt timed out; dialog unchanged after {RespawnNotificationRecoveryPolicy.RetryDelayMs}ms; kind={attempt.PromptKind}; prompt={attempt.PromptText}");
    }

    private void Reset(RespawnState state, string status)
    {
        ConfirmPendingPromptAttemptIfDialogChanged(Environment.TickCount64);
        ResetTimer();
        SetState(state, status);
    }

    private void ConfirmPendingPromptAttemptIfDialogChanged(long now)
    {
        if (!notificationRecovery.HasPendingPromptAttempt)
            return;

        var selectYesnoVisible = GameHelpers.IsAddonVisible("SelectYesno");
        var visiblePromptKind = TryGetVisibleSelectYesnoPromptKind(out var promptText);
        var observation = notificationRecovery.ObservePrompt(
            now,
            selectYesnoVisible,
            visiblePromptKind,
            promptText);

        if (observation.Outcome == RespawnPromptAttemptOutcome.Confirmed)
            LogPromptAttemptConfirmation(observation.Attempt);
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

    private static SelectYesnoPromptKind? TryGetVisibleSelectYesnoPromptKind(out string promptText)
    {
        if (!GameHelpers.TryReadSelectYesnoPrompt(out promptText))
            return null;

        return SelectYesnoPromptClassifier.Classify(promptText);
    }
}
