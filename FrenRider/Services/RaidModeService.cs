using System;
using System.Text.Json;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FrenRider.Models;

namespace FrenRider.Services;

public sealed record RaidRuntimeFrame
{
    public bool LoggedIn { get; init; }
    public ulong CharacterId { get; init; }
    public uint TerritoryId { get; init; }
    public uint DutyId { get; init; }
    public bool InDuty { get; init; }
    public bool Ready { get; init; }
    public bool Dead { get; init; }
    public bool Transition { get; init; }
    public bool InCombat { get; init; }
    public bool DutyCompleted { get; init; }
    public bool PartyWiped { get; init; }
    public bool AdsOwned { get; init; }
    public bool LootReadable { get; init; }
    public bool LootOutstanding { get; init; }
}

/// <summary>Current-character Raid orchestration. Ordinary profile policies remain unchanged.</summary>
public sealed class RaidModeService : IDisposable
{
    public const string StateEndpoint = "FrenRider.Raid.GetRuntimeStateJson";
    private readonly Plugin? plugin;
    private readonly Func<CharacterConfig> getConfig;
    private readonly Action saveSettings;
    private readonly Func<RaidRuntimeFrame> readFrame;
    private readonly Func<bool> requestStart;
    private readonly Func<bool> requestLeave;
    private readonly Func<long> tickNow;
    private ICallGateProvider<string>? stateProvider;
    private Hook<AgentCountDownSettingDialog.Delegates.Update>? countdownHook;
    private Hook<AgentCountDownSettingDialog.Delegates.ReceiveEvent>? countdownEventHook;
    private Hook<AgentCountDownSettingDialog.Delegates.ReceiveEventWithResult>? countdownResultHook;
    private CharacterConfig? ownerProfile;
    private ulong ownerCharacter;
    private (uint Territory, uint Duty) duty;
    private (ulong Character, uint Territory, uint Duty, long Revision)? pendingCountdown;
    private string sessionId = string.Empty;
    private bool enabled;
    private bool awaitingCountdown = true;
    private bool automaticStartAllowedNow;
    private bool wasDead;
    private bool wasOwned;
    private bool lootSeen;
    private bool leaveRequested;
    private string leaveFailureStatus = string.Empty;
    private long cancellationEpoch;
    private long countdownRevision;
    private int recoveryGeneration;
    private bool recoveryBoundaryLatched;
    private long? completedAtMs;

    public RaidModeService(Plugin plugin)
    {
        this.plugin = plugin;
        getConfig = () => plugin.ConfigManager.GetActiveConfig();
        saveSettings = () => plugin.ConfigManager.SaveCurrentAccount();
        readFrame = ReadNativeFrame;
        requestStart = () => Plugin.PluginInterface.GetIpcSubscriber<bool>("ADS.Raid.StartDutyFromInside").InvokeFunc();
        requestLeave = () => Plugin.PluginInterface.GetIpcSubscriber<bool>("ADS.Raid.LeaveDuty").InvokeFunc();
        tickNow = () => Environment.TickCount64;
    }

    // Uses the real coordinator and preference setters in the existing offline fixtures.
    internal RaidModeService(Func<CharacterConfig> getConfig, Action saveSettings,
        Func<RaidRuntimeFrame> readFrame, Func<bool> requestStart, Func<bool> requestLeave)
        : this(getConfig, saveSettings, readFrame, requestStart, requestLeave, () => Environment.TickCount64)
    {
    }

    internal RaidModeService(Func<CharacterConfig> getConfig, Action saveSettings,
        Func<RaidRuntimeFrame> readFrame, Func<bool> requestStart, Func<bool> requestLeave, Func<long> tickNow)
    {
        this.getConfig = getConfig;
        this.saveSettings = saveSettings;
        this.readFrame = readFrame;
        this.requestStart = requestStart;
        this.requestLeave = requestLeave;
        this.tickNow = tickNow;
    }

    public bool IsEnabled => enabled;
    internal long CancellationEpoch => cancellationEpoch;
    public string StatusText { get; private set; } = "Raid mode disabled";
    public bool AdsLootEnabled
    {
        get => getConfig().RaidAdsLootEnabled;
        set { getConfig().RaidAdsLootEnabled = value; saveSettings(); }
    }
    public bool AcceptRez
    {
        get => getConfig().RaidAcceptRez;
        set { getConfig().RaidAcceptRez = value; saveSettings(); }
    }
    public bool RemainUntilManualExit
    {
        get => getConfig().RaidRemainUntilManualExit;
        set
        {
            getConfig().RaidRemainUntilManualExit = value;
            leaveRequested = false;
            leaveFailureStatus = string.Empty;
            saveSettings();
        }
    }
    public int LootAppearanceWaitSeconds
    {
        get => Math.Clamp(getConfig().RaidLootAppearanceWaitSeconds, 0, 300);
        set { getConfig().RaidLootAppearanceWaitSeconds = Math.Clamp(value, 0, 300); saveSettings(); }
    }

    internal void Initialize()
    {
        stateProvider = Plugin.PluginInterface.GetIpcProvider<string>(StateEndpoint);
        stateProvider.RegisterFunc(GetRuntimeStateJson);
    }

    public void Enable()
    {
        if (enabled) return;
        var frame = readFrame();
        if (!frame.LoggedIn || frame.CharacterId == 0)
        {
            StatusText = "Waiting for duty";
            return;
        }
        if (plugin != null && !EnsureCountdownHook())
        {
            StatusText = "Countdown unavailable";
            return;
        }
        ownerProfile = getConfig();
        ownerCharacter = frame.CharacterId;
        enabled = true;
        ResetCurrentDuty(frame);
        StatusText = frame.InDuty ? "Waiting for countdown" : "Waiting for duty";
    }

    private void ResetCurrentDuty(RaidRuntimeFrame frame)
    {
        cancellationEpoch++;
        sessionId = Guid.NewGuid().ToString("N");
        duty = frame.InDuty && frame.TerritoryId != 0 && frame.DutyId != 0
            ? (frame.TerritoryId, frame.DutyId) : default;
        pendingCountdown = null;
        automaticStartAllowedNow = false;
        awaitingCountdown = true;
        wasDead = frame.Dead;
        wasOwned = frame.AdsOwned;
        completedAtMs = null;
        lootSeen = leaveRequested = false;
        leaveFailureStatus = string.Empty;
        recoveryGeneration = 0;
        countdownRevision = 0;
        recoveryBoundaryLatched = false;
        ResetOrdinaryWork();
    }

    internal void OnDutyStarted()
    {
        if (!enabled) return;
        RaidRuntimeFrame frame;
        try { frame = readFrame(); }
        catch { frame = new RaidRuntimeFrame(); }
        ResetCurrentDuty(frame);
    }

    public void Disable()
    {
        var wasEnabled = enabled;
        cancellationEpoch++;
        enabled = false;
        automaticStartAllowedNow = false;
        pendingCountdown = null;
        completedAtMs = null;
        lootSeen = leaveRequested = false;
        leaveFailureStatus = string.Empty;
        awaitingCountdown = true;
        StatusText = "Raid mode disabled";
        if (wasEnabled) ResetOrdinaryWork();
    }

    private void ResetOrdinaryWork()
    {
        plugin?.AdsIntegrationService?.ResetHandoffForRaid();
        plugin?.ExitBehaviourService?.CancelAutomaticExitForRaid();
        plugin?.RespawnService?.ResetForDisable();
        plugin?.PhoenixDownRecoveryService?.Reset();
    }

    internal static bool IsNativeCountdownCompletion(bool activeBefore, float remainingBefore,
        float remainingAfter, long cancellationBefore = 0, long cancellationAfter = 0,
        uint initiatorBefore = 0, uint initiatorAfter = 0)
        => activeBefore && float.IsFinite(remainingBefore) && remainingBefore > 0
           && float.IsFinite(remainingAfter) && remainingAfter <= 0 && remainingAfter >= -1
           && remainingBefore - remainingAfter <= 1
           && cancellationBefore == cancellationAfter
           && (initiatorAfter == 0 || initiatorBefore == initiatorAfter);

    internal void ObserveNativeCountdownTick(bool activeBefore, float remainingBefore, float remainingAfter,
        RaidRuntimeFrame before, RaidRuntimeFrame after)
        => ObserveNativeCountdownTick(activeBefore, remainingBefore, remainingAfter, before, after,
            cancellationEpoch, cancellationEpoch);

    internal void ObserveNativeCountdownTick(bool activeBefore, float remainingBefore, float remainingAfter,
        RaidRuntimeFrame before, RaidRuntimeFrame after, long cancellationBefore, long cancellationAfter,
        uint initiatorBefore = 0, uint initiatorAfter = 0)
    {
        if (!enabled || !activeBefore || remainingBefore <= 0) return;
        if (cancellationBefore != cancellationEpoch || cancellationAfter != cancellationEpoch
            || !getConfig().Enabled || !IsSameOwner(after) || !IsReadyForStart(before) || !IsReadyForStart(after)
            || before.CharacterId != after.CharacterId || before.TerritoryId != after.TerritoryId
            || before.DutyId != after.DutyId)
        {
            pendingCountdown = null;
            return;
        }
        StatusText = "Countdown running";
        // Only a zero crossing inside the native Update call is completion. An
        // inactive/disappearing agent, elapsed wall time or cancellation is not.
        if (cancellationBefore != cancellationAfter)
            pendingCountdown = null;
        else if (remainingAfter > remainingBefore || initiatorAfter != 0 && initiatorBefore != initiatorAfter)
        {
            if (float.IsFinite(remainingBefore) && float.IsFinite(remainingAfter) && remainingAfter > 0)
                countdownRevision++;
            pendingCountdown = null;
        }
        else if (IsNativeCountdownCompletion(activeBefore, remainingBefore, remainingAfter,
                     cancellationBefore, cancellationAfter, initiatorBefore, initiatorAfter))
            pendingCountdown = (after.CharacterId, after.TerritoryId, after.DutyId, countdownRevision);
    }

    public void Update() => Update(tickNow());

    internal void Update(long nowMs)
    {
        if (!enabled) return;
        RaidRuntimeFrame frame;
        try { frame = readFrame(); }
        catch
        {
            InvalidateCountdownObservation();
            StatusText = leaveFailureStatus.Length > 0 ? leaveFailureStatus : "Waiting for duty";
            return;
        }
        if (!frame.LoggedIn || !ReferenceEquals(ownerProfile, getConfig())
            || frame.CharacterId != 0 && frame.CharacterId != ownerCharacter)
        {
            Disable();
            return;
        }
        if (frame.CharacterId == 0)
        {
            InvalidateCountdownObservation();
            StatusText = leaveFailureStatus.Length > 0 ? leaveFailureStatus : "Waiting for duty";
            return;
        }
        if (duty != default && frame.Ready && !frame.Transition
            && (!frame.InDuty && frame.TerritoryId != duty.Territory
                || frame.InDuty && (frame.TerritoryId, frame.DutyId) != duty))
        {
            ResetCurrentDuty(frame);
            StatusText = frame.InDuty ? "Waiting for countdown" : "Waiting for duty";
            return;
        }
        if (duty == default && frame.InDuty && frame.TerritoryId != 0 && frame.DutyId != 0)
            duty = (frame.TerritoryId, frame.DutyId);

        if (frame.PartyWiped || wasDead && frame.Transition)
        {
            if (!recoveryBoundaryLatched) recoveryGeneration++;
            recoveryBoundaryLatched = true;
            awaitingCountdown = true;
        }
        else if (!frame.Dead && !frame.Transition && frame.InCombat)
            recoveryBoundaryLatched = false;
        if (wasOwned && !frame.AdsOwned && !frame.DutyCompleted)
            awaitingCountdown = true;
        wasDead = frame.Dead;
        wasOwned = frame.AdsOwned;
        if (frame.Transition || !frame.Ready || frame.Dead || frame.DutyCompleted)
            pendingCountdown = null;

        if (frame.DutyCompleted)
        {
            completedAtMs ??= nowMs;
            lootSeen |= frame.LootReadable && frame.LootOutstanding;
        }
        if (frame.Dead)
        {
            StatusText = "Waiting for manual recovery";
            return;
        }
        if (!frame.InDuty || !frame.Ready || frame.Transition)
        {
            StatusText = leaveFailureStatus.Length > 0 ? leaveFailureStatus : "Waiting for duty";
            return;
        }

        if (pendingCountdown is { } pending)
        {
            pendingCountdown = null; // Consume before a synchronous/reentrant IPC call.
            if (pending == (frame.CharacterId, frame.TerritoryId, frame.DutyId, countdownRevision)
                && getConfig().Enabled && IsReadyForStart(frame) && (!frame.AdsOwned || awaitingCountdown))
            {
                automaticStartAllowedNow = true;
                try
                {
                    var accepted = requestStart();
                    awaitingCountdown = !accepted;
                    if (accepted) recoveryBoundaryLatched = false;
                    StatusText = accepted ? "Waiting for ADS" : "ADS start rejected";
                }
                catch
                {
                    awaitingCountdown = true;
                    StatusText = "ADS unavailable";
                }
                finally { automaticStartAllowedNow = false; }
                return;
            }
        }

        if (!frame.DutyCompleted)
        {
            StatusText = frame.AdsOwned && !awaitingCountdown ? "ADS active" : "Waiting for countdown";
            return;
        }
        if (RemainUntilManualExit)
        {
            StatusText = "Remaining until manually exited";
            return;
        }
        if (!frame.LootReadable || frame.LootOutstanding)
        {
            StatusText = "Waiting for loot distribution";
            return;
        }
        if (!CanAutomaticallyExit(frame, nowMs))
        {
            StatusText = "Waiting for loot";
            return;
        }
        if (!leaveRequested)
        {
            // ADS performs the same live permission/death/loot check immediately
            // before dispatch. Rejection is visible, without a retry loop.
            leaveRequested = true;
            try { if (!requestLeave()) leaveFailureStatus = "ADS leave rejected"; }
            catch { leaveFailureStatus = "ADS unavailable"; }
        }
        StatusText = leaveFailureStatus.Length > 0 ? leaveFailureStatus : "Leaving duty";
    }

    private bool IsSameOwner(RaidRuntimeFrame frame)
        => frame.LoggedIn && frame.CharacterId == ownerCharacter && ReferenceEquals(ownerProfile, getConfig());

    private static bool IsReadyForStart(RaidRuntimeFrame frame)
        => frame.LoggedIn && frame.Ready && frame.InDuty && !frame.Dead && !frame.Transition
           && !frame.DutyCompleted && frame.TerritoryId != 0 && frame.DutyId != 0;

    private bool CanAutomaticallyExit(RaidRuntimeFrame frame, long nowMs)
        => enabled && getConfig().Enabled && IsSameOwner(frame) && frame.Ready && frame.InDuty && !frame.Transition
           && !frame.Dead && !frame.InCombat && frame.DutyCompleted && duty == (frame.TerritoryId, frame.DutyId)
           && !RemainUntilManualExit && frame.LootReadable && !frame.LootOutstanding
           && completedAtMs is { } completed
           && (lootSeen || nowMs - completed >= LootAppearanceWaitSeconds * 1000L);

    internal string GetRuntimeStateJson()
    {
        RaidRuntimeFrame frame;
        var readable = true;
        try { frame = readFrame(); }
        catch
        {
            readable = false;
            frame = new RaidRuntimeFrame { CharacterId = ownerCharacter, TerritoryId = duty.Territory, DutyId = duty.Duty };
        }
        var current = enabled && (!readable || frame.LoggedIn
            && (frame.CharacterId == ownerCharacter || frame.CharacterId == 0))
            && ReferenceEquals(ownerProfile, getConfig());
        var sourceReadable = readable && frame.Ready && IsSameOwner(frame)
            && (duty == default || duty == (frame.TerritoryId, frame.DutyId));
        var recoveryBoundaryNow = frame.PartyWiped || wasDead && frame.Transition;
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            enabled = current,
            sourceReadable,
            sessionId,
            territoryTypeId = frame.TerritoryId,
            contentFinderConditionId = frame.DutyId,
            adsLootEnabled = current && sourceReadable && frame.InDuty && getConfig().Enabled && AdsLootEnabled,
            awaitingCountdown = !sourceReadable || !getConfig().Enabled || awaitingCountdown || recoveryBoundaryNow,
            recoveryGeneration = recoveryGeneration + (recoveryBoundaryNow && !recoveryBoundaryLatched ? 1 : 0),
            countdownRevision,
            automaticStartAllowedNow = current && sourceReadable && getConfig().Enabled && automaticStartAllowedNow && IsReadyForStart(frame),
            automaticExitAllowedNow = current && sourceReadable && CanAutomaticallyExit(frame, tickNow()),
        });
    }

    private unsafe bool EnsureCountdownHook()
    {
        if (countdownHook != null) return true;
        try
        {
            var agent = AgentCountDownSettingDialog.Instance();
            if (agent == null || agent->VirtualTable == null || agent->VirtualTable->Update == null
                || agent->VirtualTable->ReceiveEvent == null || agent->VirtualTable->ReceiveEventWithResult == null)
                return false;
            countdownHook = Plugin.GameInteropProvider.HookFromAddress<AgentCountDownSettingDialog.Delegates.Update>(
                (nint)agent->VirtualTable->Update, CountdownUpdateDetour);
            countdownEventHook = Plugin.GameInteropProvider.HookFromAddress<AgentCountDownSettingDialog.Delegates.ReceiveEvent>(
                (nint)agent->VirtualTable->ReceiveEvent, CountdownEventDetour);
            if ((nint)agent->VirtualTable->ReceiveEventWithResult != (nint)agent->VirtualTable->ReceiveEvent)
                countdownResultHook = Plugin.GameInteropProvider.HookFromAddress<AgentCountDownSettingDialog.Delegates.ReceiveEventWithResult>(
                    (nint)agent->VirtualTable->ReceiveEventWithResult, CountdownResultDetour);
            countdownEventHook.Enable();
            countdownResultHook?.Enable();
            countdownHook.Enable();
            return true;
        }
        catch (Exception ex)
        {
            countdownHook?.Dispose();
            countdownEventHook?.Dispose();
            countdownResultHook?.Dispose();
            countdownHook = null;
            countdownEventHook = null;
            countdownResultHook = null;
            Plugin.Log.Warning(ex, "[FrenRider][Raid] Native countdown observation unavailable.");
            return false;
        }
    }

    private unsafe void CountdownUpdateDetour(AgentCountDownSettingDialog* agent, uint frameCount)
    {
        var original = countdownHook!.Original;
        var activeBefore = false;
        var remainingBefore = 0f;
        var initiatorBefore = 0u;
        var epochBefore = cancellationEpoch;
        RaidRuntimeFrame? before = null;
        try
        {
            if (enabled && agent != null && agent == AgentCountDownSettingDialog.Instance())
            {
                activeBefore = agent->Active;
                remainingBefore = agent->TimeRemaining;
                initiatorBefore = agent->InitiatorId;
                if (activeBefore && remainingBefore > 0) before = readFrame();
            }
        }
        catch { InvalidateCountdownObservation(); }
        // Original is deliberately outside observer catches and called exactly once.
        original(agent, frameCount);
        try
        {
            if (enabled && before != null && agent != null && agent == AgentCountDownSettingDialog.Instance())
            {
                ObserveNativeCountdownTick(activeBefore, remainingBefore, agent->TimeRemaining, before, readFrame(),
                    epochBefore, cancellationEpoch, initiatorBefore, agent->InitiatorId);
            }
        }
        catch { InvalidateCountdownObservation(); }
    }

    internal void ObserveNativeCountdownEvent(bool activeBefore, float remainingBefore, bool activeAfter,
        float remainingAfter, uint initiatorBefore, uint initiatorAfter)
    {
        if (!enabled) return;
        var wasCounting = activeBefore && float.IsFinite(remainingBefore) && remainingBefore > 0;
        var newOrReplaced = activeAfter && float.IsFinite(remainingAfter) && remainingAfter > 0
            && (!activeBefore || float.IsFinite(remainingBefore) && (!wasCounting || remainingAfter > remainingBefore)
                || initiatorBefore != 0 && initiatorAfter != 0 && initiatorBefore != initiatorAfter);
        var ended = wasCounting && (!activeAfter || !float.IsFinite(remainingAfter) || remainingAfter <= 0);
        if (newOrReplaced || ended || activeAfter && !float.IsFinite(remainingAfter)
            || activeBefore && !float.IsFinite(remainingBefore))
        {
            if (newOrReplaced) countdownRevision++;
            cancellationEpoch++;
            pendingCountdown = null;
        }
    }

    private void InvalidateCountdownObservation()
    {
        cancellationEpoch++;
        pendingCountdown = null;
    }

    private unsafe AtkValue* CountdownEventDetour(AgentCountDownSettingDialog* agent, AtkValue* result,
        AtkValue* values, uint count, ulong kind)
    {
        var original = countdownEventHook!.Original;
        var observe = false;
        var active = false;
        var remaining = 0f;
        var initiator = 0u;
        try
        {
            observe = enabled && agent != null && agent == AgentCountDownSettingDialog.Instance();
            if (observe)
            {
                active = agent->Active;
                remaining = agent->TimeRemaining;
                initiator = agent->InitiatorId;
            }
        }
        catch { observe = false; InvalidateCountdownObservation(); }
        var returned = original(agent, result, values, count, kind);
        try
        {
            if (enabled && observe && agent != null && agent == AgentCountDownSettingDialog.Instance())
                ObserveNativeCountdownEvent(active, remaining, agent->Active, agent->TimeRemaining, initiator, agent->InitiatorId);
        }
        catch { InvalidateCountdownObservation(); }
        return returned;
    }

    private unsafe AtkValue* CountdownResultDetour(AgentCountDownSettingDialog* agent, AtkValue* result,
        AtkValue* values, uint count, ulong kind)
    {
        var original = countdownResultHook!.Original;
        var observe = false;
        var active = false;
        var remaining = 0f;
        var initiator = 0u;
        try
        {
            observe = enabled && agent != null && agent == AgentCountDownSettingDialog.Instance();
            if (observe)
            {
                active = agent->Active;
                remaining = agent->TimeRemaining;
                initiator = agent->InitiatorId;
            }
        }
        catch { observe = false; InvalidateCountdownObservation(); }
        var returned = original(agent, result, values, count, kind);
        try
        {
            if (enabled && observe && agent != null && agent == AgentCountDownSettingDialog.Instance())
                ObserveNativeCountdownEvent(active, remaining, agent->Active, agent->TimeRemaining, initiator, agent->InitiatorId);
        }
        catch { InvalidateCountdownObservation(); }
        return returned;
    }

    private unsafe RaidRuntimeFrame ReadNativeFrame()
    {
        var local = Plugin.ObjectTable.LocalPlayer;
        var identity = AdsIntegrationService.ReadLiveDutyIdentity();
        var transition = Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51];
        var inDuty = Plugin.Condition[ConditionFlag.BoundByDuty] || Plugin.Condition[ConditionFlag.BoundByDuty56]
            || Plugin.Condition[ConditionFlag.BoundByDuty95] || identity.ContentFinderConditionId != 0;
        var ready = Plugin.ClientState.IsLoggedIn && local != null && local.MaxHp > 0 && !transition
            && !Plugin.Condition[ConditionFlag.WatchingCutscene] && !Plugin.Condition[ConditionFlag.WatchingCutscene78]
            && !Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]
            && identity.TerritoryTypeId == Plugin.ClientState.TerritoryType;
        var readable = TryReadOutstandingLoot(out var outstanding);
        return new RaidRuntimeFrame
        {
            LoggedIn = Plugin.ClientState.IsLoggedIn,
            CharacterId = Plugin.ClientState.IsLoggedIn ? Plugin.PlayerState.ContentId : 0,
            TerritoryId = identity.TerritoryTypeId, DutyId = identity.ContentFinderConditionId,
            InDuty = inDuty, Ready = ready, Transition = transition,
            Dead = local?.IsDead == true || Plugin.Condition[ConditionFlag.Unconscious],
            InCombat = Plugin.Condition[ConditionFlag.InCombat],
            DutyCompleted = plugin!.AdsIntegrationService.DutySession.IsCompleted,
            PartyWiped = inDuty && plugin.RespawnService?.IsConfirmedFullPartyWipe(getConfig()) == true,
            AdsOwned = plugin.AdsIntegrationService.IsControllingDuty,
            LootReadable = readable, LootOutstanding = outstanding,
        };
    }

    internal static unsafe bool TryReadOutstandingLoot(out bool outstanding)
    {
        outstanding = false;
        try
        {
            var loot = Loot.Instance();
            if (loot == null) return false;
            foreach (var item in loot->Items)
            {
                // Rolled/unavailable rows still await distribution. UI visibility
                // and eligibility for another roll do not establish clearance.
                if (IsOutstandingLoot(item))
                    outstanding = true;
            }
            return true;
        }
        catch { return false; }
    }

    internal static bool IsOutstandingLoot(LootItem item)
        => item.ItemId != 0 && item.ChestObjectId is not (0 or 0xE0000000)
           && item.RollResult == RollResult.UnAwarded;

    public void Dispose()
    {
        Disable();
        stateProvider?.UnregisterFunc();
        countdownHook?.Dispose();
        countdownEventHook?.Dispose();
        countdownResultHook?.Dispose();
        countdownHook = null;
        countdownEventHook = null;
        countdownResultHook = null;
    }
}
