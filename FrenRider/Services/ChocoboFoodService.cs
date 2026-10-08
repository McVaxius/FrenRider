using System;
using FrenRider.Models;

namespace FrenRider.Services;

internal readonly record struct CompanionFoodSnapshot(uint BuddyEntityId, int Stock, byte EffectHolders);

/// <summary>Feed one owned battle companion and require both consumption and its selected effect.</summary>
internal sealed class ChocoboFoodService
{
    private readonly Func<bool> canRun;
    private readonly Func<ulong> identity;
    private readonly Func<CharacterConfig?> currentProfile;
    private readonly Func<uint, CompanionFoodSnapshot?> read;
    private readonly Func<uint, CompanionFoodSnapshot, bool> dispatch;
    private readonly Action<string> log;
    private readonly Func<long> clock;
    private CharacterConfig? profile;
    private CharacterConfig? suspendedProfile;
    private uint suspendedFood;
    private ulong owner;
    private uint food;
    private CompanionFoodSnapshot before;
    private long deadline;
    private bool automatic;

    internal bool IsActive { get; private set; }
    internal string Status { get; private set; } = "No companion feeding is pending.";

    internal ChocoboFoodService(Func<bool> canRun, Func<ulong> identity,
        Func<CharacterConfig?> currentProfile, Func<uint, CompanionFoodSnapshot?> read,
        Func<uint, CompanionFoodSnapshot, bool> dispatch, Action<string> log, Func<long> clock)
    {
        this.canRun = canRun;
        this.identity = identity;
        this.currentProfile = currentProfile;
        this.read = read;
        this.dispatch = dispatch;
        this.log = log;
        this.clock = clock;
    }

    internal static bool IsSupportedFood(int item)
        => item is 7894 or 7895 or 7897 or 7898 or 7900;

    internal bool Start(bool automatically = false)
    {
        if (IsActive) return false;
        try
        {
            var active = currentProfile();
            if (!canRun() || identity() == 0 || active == null
                || (automatically && (!active.Enabled || !active.ChocoboAutoFeed)))
            { Status = "Companion feeding is unavailable in the current context."; return false; }
            if (!IsSupportedFood(active.ChocoboFoodItemId))
            { Status = "Select a supported companion food."; return false; }
            var snapshot = read((uint)active.ChocoboFoodItemId);
            if (!snapshot.HasValue)
            { Status = "Companion feeding is unavailable in the current context."; return false; }
            if (snapshot.Value.BuddyEntityId == 0)
            { Status = "Summon the companion before feeding."; return false; }
            if (snapshot.Value.Stock < 1)
            { Status = "No selected companion food in inventory."; return false; }
            if (snapshot.Value.EffectHolders != 0)
            { Status = "Selected companion food effect is already active."; return false; }
            profile = active;
            owner = identity();
            food = (uint)active.ChocoboFoodItemId;
            before = snapshot.Value;
            automatic = automatically;
            deadline = clock() + 5_000;
            IsActive = true; // Consume before dispatch; this service instance never repeats a pending request.
            suspendedProfile = active;
            suspendedFood = food;
            Status = "Waiting for observed companion feeding.";
            log($"dispatch feeding: item={food}");
            if (!dispatch(food, before)) Stop("Companion feeding request was rejected; no retry.");
            return IsActive;
        }
        catch (Exception ex)
        { Stop("Companion feeding failed."); log($"feeding failed: {ex.GetType().Name}"); return false; }
    }

    internal void CheckAutomatic()
    {
        var active = currentProfile();
        if (active?.Enabled != true || !active.ChocoboAutoFeed)
        { suspendedProfile = null; return; }
        if (IsActive || !canRun()
            || (ReferenceEquals(active, suspendedProfile) && active.ChocoboFoodItemId == suspendedFood)) return;
        Start(automatically: true);
    }

    internal void Tick()
    {
        try
        {
            var active = currentProfile();
            if (active?.Enabled != true || !active.ChocoboAutoFeed) suspendedProfile = null;
            if (!IsActive) return;
            if (!canRun() || identity() != owner || !ReferenceEquals(active, profile)
                || active?.ChocoboFoodItemId != food || (automatic && (!active.Enabled || !active.ChocoboAutoFeed)))
            { Stop("Companion feeding cancelled: context or profile changed."); return; }
            var snapshot = read(food);
            if (!snapshot.HasValue || snapshot.Value.BuddyEntityId != before.BuddyEntityId)
            { Stop("Companion feeding cancelled: context or profile changed."); return; }
            if (snapshot.Value.Stock == before.Stock - 1 && snapshot.Value.EffectHolders != 0)
            {
                log($"observed feeding: item={food}; one-item-debit=True; effect-holders={snapshot.Value.EffectHolders}");
                suspendedProfile = null; // A later absent effect can use the existing fifteen-second cadence.
                Stop("Companion feeding completed; food and effect were observed.");
            }
            else if (snapshot.Value.Stock != before.Stock && snapshot.Value.Stock != before.Stock - 1)
                Stop("Companion feeding stopped: unexpected inventory change.");
            else if (clock() >= deadline)
                Stop("Companion feeding stopped: consumption and effect were not observed; no retry.");
        }
        catch (Exception ex) { Stop("Companion feeding failed."); log($"feeding failed: {ex.GetType().Name}"); }
    }

    internal void Stop(string reason = "Companion feeding stopped.")
    {
        var wasActive = IsActive;
        IsActive = false;
        profile = null;
        Status = reason;
        if (wasActive) log(reason);
    }
}
