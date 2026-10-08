using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrenRider.Models;

namespace FrenRider.Services;

internal sealed record CompanionAdsPurchaseRequest(string OperationId, int ItemId, int Quantity,
    long MaximumCurrencySpend, bool AllowTravel)
{
    internal string ToJson() => JsonSerializer.Serialize(new
    {
        operationId = OperationId, itemId = ItemId, quantity = Quantity, currencyKind = "Gil",
        currencyItemId = 1, maximumCurrencySpend = MaximumCurrencySpend, allowTravel = AllowTravel,
    });
}

internal sealed class CompanionAdsPurchaseStatus
{
    [JsonRequired] public string? OperationId { get; set; }
    [JsonRequired] public uint ItemId { get; set; }
    [JsonRequired] public int RequestedQuantity { get; set; }
    [JsonRequired] public int AcquiredQuantity { get; set; }
    [JsonRequired] public int RemainingQuantity { get; set; }
    [JsonRequired] public bool Running { get; set; }
    [JsonRequired] public bool Done { get; set; }
    [JsonRequired] public bool? Succeeded { get; set; }
    [JsonRequired] public bool NavigationReleased { get; set; }
    [JsonRequired] public CompanionAdsPurchaseOffer? SelectedOffer { get; set; }
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    internal static CompanionAdsPurchaseStatus? Parse(string json)
        => JsonSerializer.Deserialize<CompanionAdsPurchaseStatus>(json, Options);
}

internal sealed class CompanionAdsPurchaseOffer
{
    public uint ShopId { get; set; }
    public uint ReceiveCount { get; set; }
    public CompanionAdsPurchaseCurrency[]? Currencies { get; set; }
}

internal sealed class CompanionAdsPurchaseCurrency
{
    public string? Kind { get; set; }
    public uint ItemId { get; set; }
    public long RequiredAmount { get; set; }
    public bool AvailabilityKnown { get; set; }
}

/// <summary>One correlated ADS handoff per request. ADS owns vendors, travel and exact native transaction verification.</summary>
internal sealed class ChocoboPurchaseService(Func<bool> canRun, Func<ulong> playerId,
    Func<CharacterConfig?> profile, Func<int, int> stock, Func<long> gil,
    Func<CompanionAdsPurchaseRequest, bool> start, Func<CompanionAdsPurchaseStatus?> read,
    Action<string> cancel, Action<string> log, Func<long> clock)
{
    private CompanionAdsPurchaseRequest? pending;
    private ulong owner;
    private CharacterConfig? ownedProfile;
    private (bool Enabled, int Food, int Greens, int FoodStock) policy;
    private int initialStock;
    private long deadline;
    private bool unresolved;
    private bool movementHeld;

    internal bool IsActive { get; private set; }
    internal bool HasOwnedFlow => movementHeld;
    internal bool HoldsMovement => movementHeld;
    internal bool AllowsOwnedTravel => movementHeld && pending?.AllowTravel == true;
    internal string Status { get; private set; } = "No companion purchase is pending.";
    internal static bool IsSupportedItem(int itemId) => itemId is 4868 or 7895 or 7897 or 7898 or 7900;
    internal static int UnitPrice(int itemId) => itemId == 4868 ? 36 : IsSupportedItem(itemId) ? 348 : 0;
    internal static bool SupportsTravelPolicy(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("shopPurchaseTravelPolicy", out var capability)
                && capability.TryGetInt32(out var version) && version >= 1;
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    internal bool Start(int itemId, int desiredStock)
    {
        if (IsActive || unresolved) return false;
        return StartRequest(itemId, desiredStock);
    }

    private bool StartRequest(int itemId, int target)
    {
        try
        {
            if (!IsSupportedItem(itemId)) { Status = "Select a supported companion purchase item."; return false; }
            if (target <= 0) { Status = "Set a positive stock target."; return false; }
            var currentProfile = profile(); var currentPlayer = playerId();
            if (!canRun() || currentProfile == null || currentPlayer == 0)
            { Status = "Companion purchasing is unavailable in the current context."; return false; }
            var before = stock(itemId); var availableGil = gil();
            if (before < 0 || availableGil < 0)
            { Status = "Companion purchasing is unavailable in the current context."; return false; }
            if (before >= target) { Status = "The companion purchase stock target is already met."; return false; }
            var quantity = (int)Math.Min(9999, Math.Min(target - (long)before,
                availableGil / UnitPrice(itemId)));
            if (quantity <= 0) { Status = "Companion purchase has no affordable quantity or inventory room."; return false; }
            if (!ReferenceEquals(profile(), currentProfile) || playerId() != currentPlayer || !canRun()) return false;
            ownedProfile = currentProfile; policy = Policy(currentProfile); owner = currentPlayer;
            initialStock = before;
            var cost = (long)quantity * UnitPrice(itemId);
            pending = new($"fr-companion-{Guid.NewGuid():N}", itemId, quantity, cost, AllowTravel: true);
            deadline = clock() + 310_000;
            unresolved = movementHeld = IsActive = true; // Consume before the existing ADS IPC call.
            Status = "Waiting for ADS companion purchase.";
            log($"ADS purchase request: item={itemId}; quantity={quantity}; exact-gil-cost={cost}");
            if (start(pending)) return true;
            unresolved = movementHeld = IsActive = false; pending = null;
            Status = "ADS did not accept the companion purchase.";
            return false;
        }
        catch { Stop("Companion purchase failed; no retry."); return false; }
    }

    internal void Tick()
    {
        if (!IsActive)
        {
            // Cancel can fail across an IPC boundary. Read-only reconciliation may release movement,
            // but a terminal failure does not authorize replay or certify paid acquisition.
            if (movementHeld && pending != null)
            {
                try
                {
                    var terminal = read();
                    if (terminal != null && terminal.OperationId == pending.OperationId
                        && terminal.ItemId == pending.ItemId && terminal.RequestedQuantity == pending.Quantity
                        && terminal.Done && !terminal.Running && terminal.NavigationReleased) movementHeld = false;
                }
                catch { }
            }
            return;
        }
        if (pending == null) return;
        try
        {
            if (!canRun() || playerId() != owner || !ReferenceEquals(profile(), ownedProfile)
                || ownedProfile == null || Policy(ownedProfile) != policy)
            { Stop("Companion purchase cancelled: context, profile, or settings changed."); return; }
            var observed = read();
            if (observed == null || observed.OperationId != pending.OperationId
                || observed.ItemId != pending.ItemId || observed.RequestedQuantity != pending.Quantity)
            { Stop("ADS companion purchase outcome is unknown; no retry."); return; }
            if (observed.Running && !observed.Done)
            {
                if (clock() >= deadline) Stop("ADS companion purchase outcome is unknown; no retry.");
                return;
            }
            var currencies = observed.SelectedOffer?.Currencies;
            var cost = (long)UnitPrice(pending.ItemId) * pending.Quantity;
            if (!observed.Done || observed.Running || !observed.NavigationReleased || observed.Succeeded != true
                || observed.AcquiredQuantity != pending.Quantity || observed.RemainingQuantity != 0
                || stock(pending.ItemId) != initialStock + pending.Quantity
                || observed.SelectedOffer is not { ReceiveCount: 1 } offer
                || (pending.ItemId != 4868 ? offer.ShopId != 262698 : offer.ShopId is not (262151 or 262158 or 262164 or 262698))
                || currencies is not { Length: 1 } || !currencies[0].AvailabilityKnown || currencies[0].ItemId != 1
                || !string.Equals(currencies[0].Kind, "Gil", StringComparison.OrdinalIgnoreCase)
                || currencies[0].RequiredAmount != cost || cost > pending.MaximumCurrencySpend)
            { Stop("ADS companion purchase outcome is unknown; no retry."); return; }
            log($"ADS purchase observed: item={pending.ItemId}; quantity={pending.Quantity}; exact-gil-cost={cost}");
            unresolved = movementHeld = IsActive = false; pending = null;
            Status = "Companion purchase completed; exact items and gil were observed.";
        }
        catch { Stop("ADS companion purchase outcome is unknown; no retry."); }
    }

    internal void Stop(string reason = "Companion purchase stopped.")
    {
        var operationId = pending?.OperationId;
        IsActive = false; Status = reason;
        if (operationId == null) return;
        try { cancel(operationId); } catch { }
        log(reason);
        // Preserve unresolved identity in this instance; cancellation does not prove nothing was spent.
    }

    private static (bool, int, int, int) Policy(CharacterConfig value)
        => (value.Enabled, value.ChocoboFoodItemId,
            value.ChocoboGreensStockTarget, value.ChocoboFoodStockTarget);
}
