using System;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FrenRider.Models;
using Lumina.Excel.Sheets;

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
    private long soloUnconsciousStartedMs;
    private (ulong Character, uint Territory, uint Duty) soloDutyIdentity;
    private long lastActionMs;
    private bool settingsInitialized;
    private bool lastEnabled;
    private int lastDelaySeconds;
    private bool lastInDuty;
    private CharacterConfig? lastConfig;
    private string lastAccount = string.Empty;
    private string lastCharacter = string.Empty;

    public RespawnState State { get; private set; } = RespawnState.Off;
    public string StatusText { get; private set; } = "Off";
    public bool OwnsUnconsciousReviveFlow { get; private set; }

    public RespawnService(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public void Update()
    {
        if (plugin.AutoYesService.RaiseOfferActive)
        {
            Reset(RespawnState.Waiting, "Raise offer handled by AutoYes");
            return;
        }

        if (plugin.PhoenixDownRecoveryService.DeferReturn)
        {
            Reset(RespawnState.Waiting, "Waiting for party recovery; Return deferred");
            return;
        }

        var config = plugin.ConfigManager.GetActiveConfig();
        var inDuty = IsInDuty();
        var respawnEnabled = RespawnNotificationRecoveryPolicy.IsRespawnEnabledForDutyState(
            config.RespawnOutsideDuties,
            config.RespawnInsideDuties,
            inDuty);
        var delaySeconds = Math.Max(
            1,
            inDuty
                ? config.RespawnInsideDutiesDelaySeconds
                : config.RespawnOutsideDutiesDelaySeconds);

        if (SettingsChanged(config, respawnEnabled, delaySeconds, inDuty))
        {
            ResetTimer();
            SetState(respawnEnabled ? RespawnState.Idle : RespawnState.Off, inDuty ? "Duty scope changed" : "Setting changed");
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

        if (!respawnEnabled)
        {
            Reset(RespawnState.Off, inDuty ? "Off inside duties" : "Off outside duties");
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

        var soloIdentity = ReadSoloDutyIdentity(config);
        UpdateSoloDelay(soloIdentity, now);
        var delayStartedMs = soloUnconsciousStartedMs != 0 ? soloUnconsciousStartedMs : unconsciousStartedMs;
        if (soloUnconsciousStartedMs != 0)
            delaySeconds = 5;
        var delayMs = delaySeconds * 1000L;
        if (!HasRespawnDelayElapsed(delayStartedMs, now, delayMs))
        {
            var remainingSeconds = Math.Max(1, (int)Math.Ceiling((delayMs - (now - delayStartedMs)) / 1000.0));
            SetState(RespawnState.Waiting, $"Unconscious; return in {remainingSeconds}s");
            return;
        }

        if (ShouldOwnCurrentUnconsciousReviveFlow(config))
        {
            OwnsUnconsciousReviveFlow = true;
            if (State != RespawnState.Returning)
                SetState(RespawnState.Returning, "Handling revive/Return notification");

            HandleReviveNotificationFlow(now, respawnEnabled);
            return;
        }

        ClearNotificationRecovery();

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

    internal static bool HasRespawnDelayElapsed(long unconsciousStartedMs, long nowMs, long delayMs)
        => nowMs - unconsciousStartedMs >= delayMs;

    private bool SettingsChanged(CharacterConfig config, bool enabled, int delaySeconds, bool inDuty)
    {
        if (!settingsInitialized)
        {
            settingsInitialized = true;
            lastEnabled = enabled;
            lastDelaySeconds = delaySeconds;
            lastInDuty = inDuty;
            lastConfig = config;
            lastAccount = plugin.ConfigManager.CurrentAccountId;
            lastCharacter = plugin.ConfigManager.ActiveCharacterKey;
            return false;
        }

        if (lastEnabled == enabled && lastDelaySeconds == delaySeconds && lastInDuty == inDuty
            && ReferenceEquals(lastConfig, config)
            && lastAccount == plugin.ConfigManager.CurrentAccountId
            && lastCharacter == plugin.ConfigManager.ActiveCharacterKey)
            return false;

        lastEnabled = enabled;
        lastDelaySeconds = delaySeconds;
        lastInDuty = inDuty;
        lastConfig = config;
        lastAccount = plugin.ConfigManager.CurrentAccountId;
        lastCharacter = plugin.ConfigManager.ActiveCharacterKey;
        return true;
    }

    private static bool IsInDuty()
        => Plugin.Condition[ConditionFlag.BoundByDuty]
            || Plugin.Condition[ConditionFlag.BoundByDuty56]
            || Plugin.Condition[ConditionFlag.BoundByDuty95]
            || TryReadConfirmedDutyIdentity(out _, out _);

    private static unsafe bool TryReadConfirmedDutyIdentity(out uint territory, out uint duty)
    {
        territory = 0;
        duty = 0;
        try
        {
            var gameMain = GameMain.Instance();
            if (gameMain == null || gameMain->CurrentContentFinderConditionId == 0
                || gameMain->CurrentTerritoryTypeId == 0
                || gameMain->CurrentTerritoryTypeId != Plugin.ClientState.TerritoryType)
                return false;
            var row = Plugin.DataManager.GetExcelSheet<ContentFinderCondition>()?
                .GetRowOrDefault(gameMain->CurrentContentFinderConditionId);
            if (row?.TerritoryType.RowId != gameMain->CurrentTerritoryTypeId)
                return false;
            territory = gameMain->CurrentTerritoryTypeId;
            duty = gameMain->CurrentContentFinderConditionId;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private unsafe (ulong Character, uint Territory, uint Duty) ReadSoloDutyIdentity(CharacterConfig config)
    {
        if (!config.Enabled || !config.RespawnInsideDuties || !Plugin.ClientState.IsLoggedIn
            || IsAreaTransitionActive() || !TryReadConfirmedDutyIdentity(out var territory, out var duty)
            || !plugin.ConfigManager.TryGetLocalActiveConfig(out _))
            return default;
        try
        {
            var local = Plugin.ObjectTable.LocalPlayer;
            var characterId = Plugin.PlayerState.ContentId;
            if (local == null || local.Address == 0 || characterId == 0
                || ((Character*)local.Address)->ContentId != characterId)
                return default;

            // Native membership precedes enumeration: the Dalamud enumerator
            // skips unreadable entries, which must never make a group look solo.
            var nativeCount = Plugin.PartyList.Length;
            var partyId = Plugin.PartyList.PartyId;
            var alliance = Plugin.PartyList.IsAlliance;
            if (nativeCount is < 0 or > 1 || alliance || nativeCount == 0 && partyId != 0)
                return default;
            var readCount = 0;
            var rosterMemberIsLocal = false;
            foreach (var member in Plugin.PartyList)
            {
                readCount++;
                rosterMemberIsLocal = member.ContentId == characterId && member.EntityId == local.EntityId;
            }

            // The HUD roster also includes duty NPC companions. An absent,
            // inconsistent or larger companion roster retains the saved delay.
            var hud = AgentHUD.Instance();
            if (hud == null)
                return default;
            var hudCount = hud->PartyMemberCount;
            var hudMemberIsLocal = hudCount == 1
                && hud->PartyMembers[0].ContentId == characterId
                && hud->PartyMembers[0].EntityId == local.EntityId;
            if (!RespawnNotificationRecoveryPolicy.IsPositivelySoloRoster(
                    nativeCount, partyId, alliance, readCount, rosterMemberIsLocal,
                    hudCount, hudMemberIsLocal, hud->RaidGroupSize)
                || Plugin.PartyList.Length != nativeCount || Plugin.PartyList.PartyId != partyId
                || Plugin.PartyList.IsAlliance != alliance
                || !TryReadConfirmedDutyIdentity(out var currentTerritory, out var currentDuty)
                || currentTerritory != territory || currentDuty != duty)
                return default;
            return (characterId, territory, duty);
        }
        catch
        {
            return default;
        }
    }

    private void UpdateSoloDelay((ulong Character, uint Territory, uint Duty) identity, long now)
    {
        soloUnconsciousStartedMs = ResolveSoloDelayStart(soloUnconsciousStartedMs, soloDutyIdentity, identity, now);
        soloDutyIdentity = identity;
    }

    internal static long ResolveSoloDelayStart(long startedMs,
        (ulong Character, uint Territory, uint Duty) previous,
        (ulong Character, uint Territory, uint Duty) current, long now)
        => current == default ? 0 : startedMs == 0 || current != previous ? now : startedMs;

    private bool CanActAfterRecheck(long now)
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var inDuty = IsInDuty();
        var enabled = inDuty ? config.RespawnInsideDuties : config.RespawnOutsideDuties;
        var delay = Math.Max(1, inDuty ? config.RespawnInsideDutiesDelaySeconds : config.RespawnOutsideDutiesDelaySeconds);
        if (SettingsChanged(config, enabled, delay, inDuty))
        {
            ResetTimer();
            return false;
        }
        if (!Plugin.ClientState.IsLoggedIn || !config.Enabled || !enabled
            || IsAreaTransitionActive() || !Plugin.Condition[ConditionFlag.Unconscious]
            || plugin.AutoYesService.RaiseOfferActive || plugin.PhoenixDownRecoveryService.DeferReturn
            || plugin.AutomationService.IsUtilityGateActive)
            return false;
        var previousSolo = soloDutyIdentity;
        UpdateSoloDelay(ReadSoloDutyIdentity(config), now);
        if (previousSolo != soloDutyIdentity)
            return false;
        return HasRespawnDelayElapsed(
            soloUnconsciousStartedMs != 0 ? soloUnconsciousStartedMs : unconsciousStartedMs,
            now, soloUnconsciousStartedMs != 0 ? 5000 : delay * 1000L);
    }

    private static bool IsAreaTransitionActive()
        => Plugin.Condition[ConditionFlag.BetweenAreas]
            || Plugin.Condition[ConditionFlag.BetweenAreas51];

    public bool ShouldOwnCurrentUnconsciousReviveFlow(CharacterConfig config)
    {
        if (plugin.PhoenixDownRecoveryService.DeferReturn)
            return false;
        var visiblePromptKind = TryGetVisibleSelectYesnoPromptKind(out _);

        return RespawnNotificationRecoveryPolicy.ShouldOwnFlow(
            Plugin.ClientState.IsLoggedIn,
            config.Enabled,
            config.RespawnOutsideDuties,
            config.RespawnInsideDuties,
            plugin.AutomationService.IsUtilityGateActive,
            IsInDuty(),
            IsAreaTransitionActive(),
            Plugin.Condition[ConditionFlag.Unconscious],
            notificationRecovery.HasPendingPromptAttempt
                || GameHelpers.IsAddonVisible("_NotificationRevive"),
            GameHelpers.IsAddonVisible("_NotificationTelepo"),
            visiblePromptKind);
    }

    private void HandleReviveNotificationFlow(long now, bool respawnEnabled)
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

        var action = notificationRecovery.GetNextAction(
            now,
            visiblePromptKind,
            reviveVisible,
            telepoVisible,
            respawnEnabled);

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
        if (!CanActAfterRecheck(now))
            return;
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
        if (responseYes && promptKind == SelectYesnoPromptKind.DeathReturn && !CanActAfterRecheck(now))
            return;
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
        soloUnconsciousStartedMs = 0;
        soloDutyIdentity = default;
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
