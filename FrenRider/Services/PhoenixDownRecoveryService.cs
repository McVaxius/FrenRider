using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FrenRider.Models;

namespace FrenRider.Services;

internal enum PhoenixRecoveryScope { None, Outdoors, Dungeon }
internal enum PhoenixApproachResult { Moving, Blocked, Unreachable }
internal readonly record struct PhoenixActor(ulong Id, ulong ContentId, bool Party, bool Dead,
    bool Healer, Vector3 Position, bool PendingRaise = false, ulong CastTarget = 0, bool PhoenixCast = false);
internal sealed record PhoenixRecoveryFrame
{
    internal CharacterConfig Config { get; init; } = new();
    internal ulong CharacterId { get; init; }
    internal uint TerritoryId { get; init; }
    internal uint DutyId { get; init; }
    internal bool Ready { get; init; }
    internal bool ContextEnded { get; init; }
    internal PhoenixRecoveryScope Scope { get; init; }
    internal ulong LocalId { get; init; }
    internal bool InCombat { get; init; }
    internal bool Flying { get; init; }
    internal bool Mounted { get; init; }
    internal bool Casting { get; init; }
    internal bool PartyHasDeadMember { get; init; }
    internal bool PartyHasLivingMember { get; init; }
    internal IReadOnlyList<PhoenixActor> Actors { get; init; } = Array.Empty<PhoenixActor>();
    internal IReadOnlyList<ulong> PartyContentIds { get; init; } = Array.Empty<ulong>();
}
internal readonly record struct PhoenixItemReadiness(bool HasItem, bool Cooldown, bool Available, bool LineOfSight, bool Known = true);

// The same coordinator runs against native state and the offline recovery lab.
internal interface IPhoenixRecoveryRuntime
{
    PhoenixRecoveryFrame ReadFrame();
    PhoenixItemReadiness ReadItem(ulong targetId);
    bool TryGetAdsAcknowledgement(out bool acknowledged);
    bool AdsOwnsDuty { get; }
    void SetHolds(bool movement, bool actions, bool preventPulls);
    PhoenixApproachResult Approach(PhoenixActor target, float stopRange, long now);
    void StopApproach();
    void Dismount();
    bool UsePhoenixDown(ulong targetId);
}

internal sealed class PhoenixDownRecoveryService : IDisposable
{
    internal const uint ItemId = 4570;
    internal const float ItemRange = 15f;
    internal const float StopRange = 14.5f;
    private readonly IPhoenixRecoveryRuntime runtime;
    private (ulong Character, uint Territory, uint Duty) identity;
    private ulong recipientId;
    private long recoveryStartedMs;
    private long nextAttemptMs;
    private long attemptStartedMs;
    private bool attemptPending;
    private bool castObserved;
    private bool requestTracked;
    private readonly Dictionary<(ulong Id, bool Content), bool> observedDeaths = new();
    private bool episodeActive;
    private bool episodeSuppressed;
    private long episodeStartedMs;
    private int episodeStartedCasts;
    private bool movementHeld;
    internal bool HoldMovement => movementHeld;
    internal bool HoldActions { get; private set; }
    internal bool PartyRecoveryActive { get; private set; }
    internal bool DeferReturn { get; private set; }
    internal string StatusText { get; private set; } = "Off";

    internal PhoenixDownRecoveryService(IPhoenixRecoveryRuntime runtime) => this.runtime = runtime;

    internal bool ShouldPauseDutyProgression()
    {
        // ADS can tick before Fren Rider. Refresh death truth at the IPC boundary so
        // ADS cannot select a fresh pull using the previous Fren Rider frame.
        try
        {
            var frame = runtime.ReadFrame();
            Observe(frame, Environment.TickCount64);
            return PartyRecoveryActive && frame.Scope == PhoenixRecoveryScope.Dungeon;
        }
        catch
        {
            Suspend();
            return false;
        }
    }

    internal static PhoenixRecoveryScope ResolveScope(bool inInstance, uint contentType, int partySize, bool outdoors)
        => inInstance ? contentType == 2 && partySize == 4 ? PhoenixRecoveryScope.Dungeon : PhoenixRecoveryScope.None
            : outdoors ? PhoenixRecoveryScope.Outdoors : PhoenixRecoveryScope.None;

    internal static bool HasNearbyHealer(PhoenixActor recipient, IEnumerable<PhoenixActor> actors)
        => actors.Any(actor => actor.Party && actor.Healer && !actor.Dead
            && Vector3.DistanceSquared(actor.Position, recipient.Position) <= 20f * 20f);

    private bool Observe(PhoenixRecoveryFrame frame, long now)
    {
        var currentIdentity = (frame.CharacterId, frame.TerritoryId, frame.DutyId);
        if (frame.CharacterId != 0 && frame.TerritoryId != 0 && identity != currentIdentity)
        {
            Reset();
            identity = currentIdentity;
        }
        if (frame.ContextEnded || !frame.Config.Enabled || !frame.Config.UsePhoenixDownsForRecovery
            || frame.Ready && frame.Scope == PhoenixRecoveryScope.None)
        {
            Reset();
            return false;
        }
        if (!frame.Ready || frame.Scope == PhoenixRecoveryScope.None)
        {
            Suspend();
            return false;
        }

        var local = frame.Actors.FirstOrDefault(actor => actor.Id == frame.LocalId);
        if (local.Id == 0 || frame.CharacterId == 0 || local.ContentId != frame.CharacterId)
        {
            Suspend();
            return false;
        }
        var deadParty = frame.Actors.Where(actor => actor.Party && actor.Dead).ToArray();
        var subjects = frame.Actors.Where(actor => actor.Party || frame.Scope == PhoenixRecoveryScope.Outdoors
            && frame.Config.ReviveAnyoneOutdoors
            && Vector3.DistanceSquared(local.Position, actor.Position) <= ItemRange * ItemRange).ToArray();
        var newDeath = false;
        foreach (var actor in subjects)
        {
            var key = (actor.ContentId != 0 ? actor.ContentId : actor.Id, actor.ContentId != 0);
            if (observedDeaths.TryGetValue(key, out var dead) && !dead && actor.Dead)
                newDeath = true;
            observedDeaths[key] = actor.Dead;
        }
        var hasDeaths = subjects.Any(actor => actor.Dead) || frame.PartyHasDeadMember;
        if (newDeath || hasDeaths && !episodeActive && !episodeSuppressed)
        {
            episodeActive = true;
            episodeSuppressed = false;
            episodeStartedMs = now;
            episodeStartedCasts = 0;
        }
        if (requestTracked && !castObserved && local.PhoenixCast && local.CastTarget == recipientId)
        {
            castObserved = attemptPending = true;
            episodeStartedCasts++;
        }
        if (episodeActive && !frame.Config.PhoenixFullStickiness &&
            (frame.Config.PhoenixRecoveryLimit == PhoenixRecoveryLimitMode.Time
                ? now - episodeStartedMs >= Math.Max(1, frame.Config.PhoenixRecoverySeconds) * 1000L
                : episodeStartedCasts >= Math.Max(1, frame.Config.PhoenixRecoveryStartedCasts)))
            GiveUp("Recovery limit reached");
        if (!hasDeaths && !attemptPending)
            episodeActive = false;
        PartyRecoveryActive = !episodeSuppressed && (deadParty.Length == 0 && frame.PartyHasDeadMember
                || deadParty.Any(actor => !HasNearbyHealer(actor, frame.Actors)))
            && (frame.PartyHasLivingMember || frame.Actors.Any(actor => actor.Party && !actor.Dead));
        DeferReturn = local.Dead && (PartyRecoveryActive || local.PendingRaise || HasObservedReviveCast(local, frame.Actors));
        SetHolds(PartyRecoveryActive || attemptPending,
            attemptPending && (HoldActions || local.PhoenixCast && local.CastTarget == recipientId), PartyRecoveryActive);
        return true;
    }

    internal void Update() => Update(Environment.TickCount64);
    internal void Update(long now)
    {
        try
        {
            var frame = runtime.ReadFrame();
            if (!Observe(frame, now))
                return;
            var local = frame.Actors.First(actor => actor.Id == frame.LocalId);
            if (local.Dead)
            {
                EndAttempt(now);
                StatusText = DeferReturn ? "Waiting for party revival; Return deferred" : "No living party rescuer";
                return;
            }

            if (attemptPending || requestTracked)
            {
                // Track the cast independently of UseAction's return and recipient revival.
                if (local.PhoenixCast && local.CastTarget == recipientId)
                {
                    castObserved = true;
                    StatusText = "Casting Phoenix Down";
                    return;
                }
                if (castObserved || now - attemptStartedMs >= 1000)
                {
                    EndAttempt(now);
                    StatusText = castObserved ? "Cast ended; checking revival" : "Attempt did not start; rechecking readiness";
                }
                else
                    return;
            }

            if (episodeSuppressed)
            {
                runtime.StopApproach();
                SetHolds(false, false, false);
                StatusText = "Recovery given up; waiting for a new death";
                return;
            }

            var target = frame.Actors.Where(actor => actor.Dead && actor.Id != local.Id
                    && (actor.Party || frame.Scope == PhoenixRecoveryScope.Outdoors && frame.Config.ReviveAnyoneOutdoors
                        && Vector3.DistanceSquared(local.Position, actor.Position) <= ItemRange * ItemRange))
                .OrderByDescending(actor => actor.Party).ThenByDescending(actor => actor.Healer)
                .ThenBy(actor => actor.ContentId).ThenBy(actor => actor.Id)
                .FirstOrDefault(actor => !actor.PendingRaise && !HasObservedReviveCast(actor, frame.Actors)
                    && !HasNearbyHealer(actor, frame.Actors));

            if (target.Id == 0)
            {
                recipientId = 0;
                runtime.StopApproach();
                StatusText = PartyRecoveryActive
                    ? frame.Actors.Any(actor => actor.Party && actor.Dead)
                        ? "Waiting for healer, Raise cast or pending revival"
                        : "Blocked: waiting for a visible, confirmed party corpse"
                    : "Ready";
                return;
            }
            if (recipientId != target.Id)
            {
                runtime.StopApproach();
                recipientId = target.Id;
                recoveryStartedMs = now;
            }

            if (frame.Scope == PhoenixRecoveryScope.Dungeon && runtime.AdsOwnsDuty)
            {
                if (!runtime.TryGetAdsAcknowledgement(out var acknowledged))
                {
                    if (frame.Config.PhoenixGiveUpWithoutAds) { GiveUp("ADS recovery support unavailable"); return; }
                    Block("ADS recovery hold IPC unavailable; update ADS");
                    return;
                }
                if (!acknowledged)
                {
                    Block("Waiting for ADS recovery hold acknowledgement");
                    return;
                }
            }
            if (frame.InCombat && !frame.Config.AllowPhoenixDownInCombat)
            {
                Block("Waiting to leave combat before approaching or using Phoenix Down");
                return;
            }
            if (frame.Flying)
            {
                Block("Waiting to land");
                return;
            }
            if (frame.Mounted)
            {
                runtime.StopApproach();
                if (now >= nextAttemptMs)
                {
                    nextAttemptMs = now + 1000;
                    runtime.Dismount();
                }
                StatusText = "Dismounting for recovery";
                return;
            }

            var readiness = runtime.ReadItem(target.Id);
            if (!readiness.Known) { Block("Phoenix Down readiness unavailable"); return; }
            if (!readiness.HasItem || readiness.Cooldown)
            {
                if (!readiness.HasItem && frame.Config.PhoenixGiveUpWithoutItem) { GiveUp("No Phoenix Down available"); return; }
                Block(!readiness.HasItem ? "No Phoenix Downs; waiting for party recovery" : "Medicine cooldown; waiting for recovery");
                return;
            }
            if (frame.Casting)
            {
                Block("Waiting for current cast");
                return;
            }
            if (Vector3.DistanceSquared(local.Position, target.Position) > StopRange * StopRange)
            {
                if (!target.Party)
                {
                    // Outdoor strangers are eligible through 15y; never navigate to them.
                    if (Vector3.DistanceSquared(local.Position, target.Position) > ItemRange * ItemRange)
                        return;
                }
                else
                {
                    var approach = runtime.Approach(target, StopRange, now);
                    if (approach == PhoenixApproachResult.Unreachable && frame.Config.PhoenixGiveUpUnreachable)
                    { GiveUp("Corpse is unreachable"); return; }
                    StatusText = approach == PhoenixApproachResult.Moving
                        ? "Approaching party corpse" : "Blocked: navigation unavailable or corpse unreachable";
                    return;
                }
            }
            runtime.StopApproach();

            var rank = target.Party ? Array.IndexOf(frame.PartyContentIds.Where(id => id != 0).Distinct().Order().ToArray(), frame.CharacterId) : 0;
            if (now < nextAttemptMs || now - recoveryStartedMs < Math.Max(0, rank) * 500L)
            {
                StatusText = "Waiting for recovery attempt slot";
                return;
            }

            SetHolds(true, true, PartyRecoveryActive);
            // Read every gate again after acquiring the action hold, immediately before use.
            var fresh = runtime.ReadFrame();
            var corpse = fresh.Actors.FirstOrDefault(actor => actor.Id == target.Id && actor.ContentId == target.ContentId && actor.Dead);
            var rescuer = fresh.Actors.FirstOrDefault(actor => actor.Id == frame.LocalId);
            readiness = runtime.ReadItem(target.Id);
            if (!fresh.Ready || !fresh.Config.Enabled || !fresh.Config.UsePhoenixDownsForRecovery
                || (fresh.CharacterId, fresh.TerritoryId, fresh.DutyId) != identity || fresh.Scope != frame.Scope
                || rescuer.Id == 0 || rescuer.Dead || fresh.Flying || fresh.Mounted || fresh.Casting
                || fresh.InCombat && !fresh.Config.AllowPhoenixDownInCombat
                || corpse.Id == 0 || corpse.PendingRaise || HasNearbyHealer(corpse, fresh.Actors)
                || HasObservedReviveCast(corpse, fresh.Actors)
                || !corpse.Party && !fresh.Config.ReviveAnyoneOutdoors
                || Vector3.DistanceSquared(rescuer.Position, corpse.Position) > ItemRange * ItemRange
                || !readiness.Known || !readiness.HasItem || readiness.Cooldown || !readiness.Available || !readiness.LineOfSight
                || frame.Scope == PhoenixRecoveryScope.Dungeon && runtime.AdsOwnsDuty
                    && (!runtime.TryGetAdsAcknowledgement(out var ack) || !ack))
            {
                Observe(fresh, now);
                SetHolds(PartyRecoveryActive, false, PartyRecoveryActive);
                nextAttemptMs = now + 1000;
                StatusText = "Blocked: Phoenix Down readiness changed";
                return;
            }

            nextAttemptMs = now + 1000;
            attemptStartedMs = now;
            castObserved = false;
            requestTracked = true;
            attemptPending = runtime.UsePhoenixDown(target.Id);
            StatusText = attemptPending ? "Phoenix Down requested; waiting for actual cast" : "Item attempt rejected; rechecking in one second";
            if (!attemptPending)
                SetHolds(PartyRecoveryActive, false, PartyRecoveryActive);
        }
        catch (Exception)
        {
            Suspend();
            StatusText = "Blocked: recovery state unavailable";
        }
    }

    private static bool HasObservedReviveCast(PhoenixActor target, IEnumerable<PhoenixActor> actors)
        => actors.Any(actor => !actor.Dead && actor.CastTarget == target.Id);

    private void Block(string reason)
    {
        runtime.StopApproach();
        SetHolds(PartyRecoveryActive, false, PartyRecoveryActive);
        StatusText = "Blocked: " + reason;
    }

    private void EndAttempt(long now)
    {
        if (attemptPending)
            nextAttemptMs = Math.Max(nextAttemptMs, now + 1000);
        attemptPending = false;
        requestTracked = false;
        SetHolds(PartyRecoveryActive, false, PartyRecoveryActive);
        runtime.StopApproach();
    }

    private void SetHolds(bool movement, bool actions, bool preventPulls)
    {
        movementHeld = movement;
        HoldActions = actions;
        runtime.SetHolds(movement, actions, preventPulls);
    }

    private void GiveUp(string reason)
    {
        episodeSuppressed = true;
        PartyRecoveryActive = DeferReturn = false;
        runtime.StopApproach();
        attemptPending &= castObserved;
        SetHolds(attemptPending, attemptPending, false);
        StatusText = reason + "; waiting for a new death";
    }

    private void Suspend()
    {
        // A missing actor or unreadable frame is not proof of a new death/run.
        // Release owned controls while retaining the episode's budget/suppression.
        runtime.StopApproach();
        PartyRecoveryActive = DeferReturn = false;
        SetHolds(false, false, false);
        StatusText = "Blocked: recovery state unavailable";
    }

    internal void Reset()
    {
        runtime.StopApproach();
        attemptPending = castObserved = requestTracked = false;
        episodeActive = episodeSuppressed = false;
        episodeStartedMs = 0;
        episodeStartedCasts = 0;
        observedDeaths.Clear();
        recipientId = 0;
        PartyRecoveryActive = DeferReturn = false;
        SetHolds(false, false, false);
        StatusText = "Off";
    }

    public void Dispose()
    {
        Reset();
        if (runtime is IDisposable disposable)
            disposable.Dispose();
    }
}
