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
    private long unconsciousStartedMs;
    private long lastActionMs;
    private bool settingsInitialized;
    private bool lastEnabled;
    private int lastDelaySeconds;

    public RespawnState State { get; private set; } = RespawnState.Off;
    public string StatusText { get; private set; } = "Off";

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
            return;
        }

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
