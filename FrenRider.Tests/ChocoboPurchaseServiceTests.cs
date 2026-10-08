using System.Text.Json;
using System.Text.Json.Nodes;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class ChocoboPurchaseServiceTests
{
    [Theory]
    [InlineData(4868, 2, 7, 10000, 5)]
    [InlineData(4868, 2, 7, 179, 4)]
    [InlineData(4868, 2, 7, 71, 1)]
    [InlineData(4868, 0, int.MaxValue, 999999999, 9999)]
    [InlineData(7897, 1, 7, 10000, 6)]
    [InlineData(7897, 1, 7, 695, 1)]
    public void AdditionalQuantityIsBoundedByDeficitGilAndAdsLimit(int item, int current,
        int target, long gil, int expected)
    {
        var test = new Scenario { Gil = gil };
        test.Stocks[item] = current;

        Assert.True(test.Service.Start(item, target));

        var request = Assert.Single(test.Requests);
        Assert.Equal(item, request.ItemId);
        Assert.Equal(expected, request.Quantity);
        Assert.Equal((long)expected * ChocoboPurchaseService.UnitPrice(item), request.MaximumCurrencySpend);
        Assert.InRange(request.MaximumCurrencySpend, 1, gil);
        Assert.False(test.Service.Start(item, target));
    }

    [Theory]
    [InlineData(4868, 5, 5, 10000)]
    [InlineData(4868, 6, 5, 10000)]
    [InlineData(4868, -1, 5, 10000)]
    [InlineData(4868, 0, 0, 10000)]
    [InlineData(4868, 0, -1, 10000)]
    [InlineData(4868, 0, 5, 0)]
    [InlineData(4868, 0, 5, 35)]
    [InlineData(4868, 0, 5, -1)]
    [InlineData(7897, 0, 5, 347)]
    [InlineData(7894, 0, 5, 10000)]
    [InlineData(8166, 0, 5, 10000)]
    public void MetInvalidOrUnaffordableTargetsDoNotHandOff(int item, int current,
        int target, long gil)
    {
        var test = new Scenario { Gil = gil };
        test.Stocks[item] = current;

        Assert.False(test.Service.Start(item, target));
        Assert.False(test.Service.HasOwnedFlow);
        Assert.False(test.Service.HoldsMovement);
        Assert.Empty(test.Requests);
        Assert.Empty(test.Cancellations);
    }

    [Fact]
    public void ManualRequestJsonCarriesCorrelatedGilAuthorizationAndExplicitTravel()
    {
        var test = new Scenario();
        Assert.True(test.Service.Start(7897, 4));
        var request = Assert.Single(test.Requests);

        using var document = JsonDocument.Parse(request.ToJson());
        var payload = document.RootElement;
        Assert.Equal(7, payload.EnumerateObject().Count());
        Assert.Equal(request.OperationId, payload.GetProperty("operationId").GetString());
        Assert.StartsWith("fr-companion-", request.OperationId);
        Assert.True(Guid.TryParseExact(request.OperationId["fr-companion-".Length..], "N", out _));
        Assert.Equal(7897, payload.GetProperty("itemId").GetInt32());
        Assert.Equal(3, payload.GetProperty("quantity").GetInt32());
        Assert.Equal("Gil", payload.GetProperty("currencyKind").GetString());
        Assert.Equal(1, payload.GetProperty("currencyItemId").GetInt32());
        Assert.Equal(1044L, payload.GetProperty("maximumCurrencySpend").GetInt64());
        Assert.True(payload.GetProperty("allowTravel").GetBoolean());
        Assert.True(test.Service.AllowsOwnedTravel);
    }

    [Theory]
    [InlineData(4868)]
    [InlineData(7895)]
    [InlineData(7897)]
    [InlineData(7898)]
    [InlineData(7900)]
    public void MatchedSuccessfulAdsTerminalQuoteAndStockCompleteExactlyOnce(int item)
    {
        var test = new Scenario();
        test.Stocks[item] = 1;
        Assert.True(test.Service.Start(item, 3));
        test.Service.Tick();
        Assert.True(test.Service.IsActive);
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("ADS purchase observed:"));

        test.PublishSuccess();
        test.ObservedJson = JsonSerializer.Serialize(test.Observed,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        test.Service.Tick();
        test.Service.Tick();

        Assert.False(test.Service.HasOwnedFlow);
        Assert.False(test.Service.AllowsOwnedTravel);
        Assert.False(test.Service.HoldsMovement);
        Assert.Single(test.Requests);
        Assert.Empty(test.Cancellations);
        Assert.Single(test.Logs, line => line.StartsWith("ADS purchase observed:"));
    }

    [Theory]
    [InlineData("operationId")]
    [InlineData("itemId")]
    [InlineData("requestedQuantity")]
    [InlineData("acquiredQuantity")]
    [InlineData("remainingQuantity")]
    [InlineData("running")]
    [InlineData("done")]
    [InlineData("succeeded")]
    [InlineData("navigationReleased")]
    [InlineData("selectedOffer")]
    public void MissingTerminalJsonFieldsCannotBecomeDefaultSuccessEvidence(string field)
    {
        var test = new Scenario();
        test.Profile.ChocoboGreensStockTarget = 5;
        test.Profile.ChocoboFoodStockTarget = 3;
        test.Service.Start(4868, 5);
        var request = Assert.Single(test.Requests);
        test.PublishSuccess();
        var payload = JsonNode.Parse(JsonSerializer.Serialize(test.Observed,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }))!.AsObject();
        Assert.True(payload.Remove(field));
        test.ObservedJson = payload.ToJsonString();

        Assert.Throws<JsonException>(() => CompanionAdsPurchaseStatus.Parse(test.ObservedJson!));
        test.Service.Tick();

        Assert.False(test.Service.IsActive);
        Assert.True(test.Service.HasOwnedFlow);
        Assert.True(test.Service.HoldsMovement);
        Assert.Single(test.Requests);
        Assert.Equal(request.OperationId, Assert.Single(test.Cancellations));
        Assert.False(test.Service.Start(4868, 5));
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("ADS purchase observed:"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualFoodPurchaseIgnoresOldSwitchesAndDoesNotRestockGreensOrReplay(bool oldTravel)
    {
        var test = new Scenario();
        test.Profile = JsonSerializer.Deserialize<CharacterConfig>(JsonSerializer.Serialize(new
        {
            ChocoboAutoPurchase = true, ChocoboPurchaseGilCap = 0, ChocoboPurchaseTravel = oldTravel,
            ChocoboGreensStockTarget = 5, ChocoboFoodStockTarget = 4, ChocoboFoodItemId = 7897,
        }))!;
        test.Service.Tick();
        Assert.Empty(test.Requests);
        Assert.True(test.Service.Start(test.Profile.ChocoboFoodItemId, test.Profile.ChocoboFoodStockTarget));
        var food = Assert.Single(test.Requests);
        Assert.Equal(7897, food.ItemId);
        Assert.Equal(3, food.Quantity);
        Assert.Equal(1044L, food.MaximumCurrencySpend);
        Assert.True(food.AllowTravel);
        test.PublishSuccess();
        test.Service.Tick();
        test.Service.Tick();
        Assert.False(test.Service.Start(test.Profile.ChocoboFoodItemId, test.Profile.ChocoboFoodStockTarget));

        Assert.False(test.Service.HasOwnedFlow);
        Assert.Equal(2, test.Stocks[4868]);
        Assert.False(test.Service.HoldsMovement);
        Assert.Equal(4, test.Stocks[7897]);
        Assert.Equal(8956L, test.Gil);
        Assert.Single(test.Requests);
        Assert.Empty(test.Cancellations);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("read-exception")]
    [InlineData("operation")]
    [InlineData("item")]
    [InlineData("quantity")]
    [InlineData("failed")]
    [InlineData("unknown-success")]
    [InlineData("partial")]
    [InlineData("remaining")]
    [InlineData("not-done")]
    [InlineData("running-and-done")]
    [InlineData("navigation-not-released")]
    [InlineData("stock-short")]
    [InlineData("stock-extra")]
    public void UncertainOutcomesCannotStartAnotherPurchaseOrReplay(string change)
    {
        var test = new Scenario();
        test.Profile.ChocoboGreensStockTarget = 5;
        test.Profile.ChocoboFoodStockTarget = 3;
        test.Service.Start(4868, 5);
        var request = Assert.Single(test.Requests);
        test.PublishSuccess();
        switch (change)
        {
            case "missing": test.Observed = null; break;
            case "read-exception": test.ThrowOnRead = true; break;
            case "operation": test.Observed!.OperationId = "unrelated-operation"; break;
            case "item": test.Observed!.ItemId = 7897; break;
            case "quantity": test.Observed!.RequestedQuantity++; break;
            case "failed": test.Observed!.Succeeded = false; break;
            case "unknown-success": test.Observed!.Succeeded = null; break;
            case "partial": test.Observed!.AcquiredQuantity--; break;
            case "remaining": test.Observed!.RemainingQuantity = 1; break;
            case "not-done": test.Observed!.Done = false; break;
            case "running-and-done": test.Observed!.Running = true; break;
            case "navigation-not-released": test.Observed!.NavigationReleased = false; break;
            case "stock-short": test.Stocks[request.ItemId]--; break;
            case "stock-extra": test.Stocks[request.ItemId]++; break;
        }

        test.Service.Tick();
        Assert.False(test.Service.IsActive);
        Assert.True(test.Service.HasOwnedFlow);
        Assert.True(test.Service.HoldsMovement);
        Assert.Equal(request.OperationId, Assert.Single(test.Cancellations));
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("ADS purchase observed:"));
        if (change == "navigation-not-released")
        {
            test.Service.Tick();
            Assert.True(test.Service.HoldsMovement);
            Assert.Single(test.Cancellations);
        }
        test.ThrowOnRead = false;
        test.Stocks[4868] = 0;
        Assert.False(test.Service.Start(4868, 5));
        Assert.Single(test.Requests);
    }

    [Theory]
    [InlineData("missing-offer")]
    [InlineData("bundle")]
    [InlineData("shop")]
    [InlineData("missing-currency")]
    [InlineData("extra-currency")]
    [InlineData("unknown-availability")]
    [InlineData("currency-kind")]
    [InlineData("currency-id")]
    [InlineData("cost-short")]
    [InlineData("cost-extra")]
    public void UnverifiedQuotesCannotCompleteOrReleaseOwnership(string change)
    {
        var test = new Scenario();
        test.Profile.ChocoboGreensStockTarget = 5;
        test.Profile.ChocoboFoodStockTarget = 3;
        test.Service.Start(4868, 5);
        var request = Assert.Single(test.Requests);
        test.PublishSuccess();
        var offer = test.Observed!.SelectedOffer!;
        var currency = Assert.Single(offer.Currencies!);
        switch (change)
        {
            case "missing-offer": test.Observed.SelectedOffer = null; break;
            case "bundle": offer.ReceiveCount = 2; break;
            case "shop": offer.ShopId = 999999; break;
            case "missing-currency": offer.Currencies = null; break;
            case "extra-currency": offer.Currencies = [currency, currency]; break;
            case "unknown-availability": currency.AvailabilityKnown = false; break;
            case "currency-kind": currency.Kind = "tomestone"; break;
            case "currency-id": currency.ItemId = 28; break;
            case "cost-short": currency.RequiredAmount--; break;
            case "cost-extra": currency.RequiredAmount++; break;
        }

        test.Service.Tick();

        Assert.False(test.Service.IsActive);
        Assert.True(test.Service.HasOwnedFlow);
        Assert.True(test.Service.HoldsMovement);
        Assert.Single(test.Requests);
        Assert.Equal(request.OperationId, Assert.Single(test.Cancellations));
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("ADS purchase observed:"));
    }

    [Fact]
    public void RejectedAdsStartDoesNotCancelOrTryFood()
    {
        var test = new Scenario { Accept = false };
        test.Profile.ChocoboGreensStockTarget = 5;
        test.Profile.ChocoboFoodStockTarget = 3;
        test.Service.Start(4868, 5);
        test.Service.Tick();

        Assert.False(test.Service.HasOwnedFlow);
        Assert.Single(test.Requests);
        Assert.Empty(test.Cancellations);
        Assert.Contains("did not accept", test.Service.Status);
        Assert.False(test.Service.HoldsMovement);
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("ADS purchase observed:"));
    }

    [Fact]
    public void ThrowingStartPreservesUnknownOwnershipAndCancelsOnlyItsOperation()
    {
        var test = new Scenario { ThrowOnStart = true };
        test.Profile.ChocoboGreensStockTarget = 5;
        test.Profile.ChocoboFoodStockTarget = 3;
        test.Service.Start(4868, 5);

        var request = Assert.Single(test.Requests);
        Assert.False(test.Service.IsActive);
        Assert.True(test.Service.HasOwnedFlow);
        Assert.True(test.Service.HoldsMovement);
        Assert.Equal(request.OperationId, Assert.Single(test.Cancellations));
        Assert.False(test.Service.Start(4868, 5));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("profile")]
    [InlineData("context")]
    [InlineData("enabled")]
    [InlineData("food")]
    [InlineData("greens-target")]
    [InlineData("food-target")]
    public void OwnerProfileOrPolicyChangesCancelOnlyThePendingOperation(string change)
    {
        var test = new Scenario();
        test.Profile.ChocoboGreensStockTarget = 5;
        test.Profile.ChocoboFoodStockTarget = 3;
        test.Service.Start(4868, 5);
        var request = Assert.Single(test.Requests);
        switch (change)
        {
            case "owner": test.Owner++; break;
            case "profile": test.Profile = test.Profile.Clone(); break;
            case "context": test.Safe = false; break;
            case "enabled": test.Profile.Enabled = false; break;
            case "food": test.Profile.ChocoboFoodItemId = 7895; break;
            case "greens-target": test.Profile.ChocoboGreensStockTarget++; break;
            case "food-target": test.Profile.ChocoboFoodStockTarget++; break;
        }

        test.Service.Tick();

        Assert.False(test.Service.IsActive);
        Assert.True(test.Service.HasOwnedFlow);
        Assert.True(test.Service.HoldsMovement);
        Assert.Single(test.Requests);
        Assert.Equal(request.OperationId, Assert.Single(test.Cancellations));
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("ADS purchase observed:"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StopPreservesUnresolvedIdentityEvenWhenCancellationThrows(bool throws)
    {
        var test = new Scenario { ThrowOnCancel = throws };
        Assert.True(test.Service.Start(4868, 5));
        var request = Assert.Single(test.Requests);

        test.Service.Stop();
        test.Service.Tick();

        Assert.False(test.Service.IsActive);
        Assert.True(test.Service.HasOwnedFlow);
        Assert.True(test.Service.HoldsMovement);
        Assert.Equal(request.OperationId, Assert.Single(test.Cancellations));
        Assert.False(test.Service.Start(4868, 5));
        Assert.Single(test.Requests);
    }

    [Fact]
    public void RunningAdsPurchaseWaitsUntilTheBoundaryThenCancelsWithoutReplay()
    {
        var test = new Scenario();
        Assert.True(test.Service.Start(4868, 5));
        var request = Assert.Single(test.Requests);
        test.Now = 309999;
        test.Service.Tick();
        Assert.True(test.Service.IsActive);
        Assert.Empty(test.Cancellations);

        test.Now = 310000;
        test.Service.Tick();

        Assert.False(test.Service.IsActive);
        Assert.True(test.Service.HasOwnedFlow);
        Assert.True(test.Service.HoldsMovement);
        Assert.Equal(request.OperationId, Assert.Single(test.Cancellations));
        Assert.False(test.Service.Start(4868, 5));
        Assert.Single(test.Requests);
    }

    [Theory]
    [InlineData("stop", true)]
    [InlineData("stop", false)]
    [InlineData("stop", null)]
    [InlineData("missing", true)]
    [InlineData("missing", false)]
    [InlineData("timeout", false)]
    [InlineData("policy", false)]
    public void OwnedTerminalReconciliationReleasesMovementWithoutCertifyingOrRearmingPurchase(
        string cause, bool? succeeded)
    {
        var test = new Scenario();
        test.Profile.ChocoboGreensStockTarget = 5;
        test.Profile.ChocoboFoodStockTarget = 3;
        test.Service.Start(4868, 5);
        var request = Assert.Single(test.Requests);
        switch (cause)
        {
            case "missing": test.Observed = null; test.Service.Tick(); break;
            case "timeout": test.Now = 310000; test.Service.Tick(); break;
            case "policy": test.Profile.ChocoboFoodStockTarget++; test.Service.Tick(); break;
            default: test.Service.Stop(); break;
        }
        Assert.False(test.Service.IsActive);
        Assert.True(test.Service.HoldsMovement);
        Assert.True(test.Service.HasOwnedFlow);
        Assert.True(test.Service.AllowsOwnedTravel);

        test.Observed = new()
        {
            OperationId = request.OperationId, ItemId = (uint)request.ItemId,
            RequestedQuantity = request.Quantity, Done = true, Running = false,
            NavigationReleased = true, Succeeded = succeeded, AcquiredQuantity = 0, RemainingQuantity = request.Quantity,
        };
        test.Service.Tick();
        test.Service.Tick();
        test.Stocks[4868] = 0;

        Assert.False(test.Service.IsActive);
        Assert.False(test.Service.HoldsMovement);
        Assert.False(test.Service.HasOwnedFlow);
        Assert.False(test.Service.AllowsOwnedTravel);
        Assert.False(test.Service.Start(4868, 5));
        Assert.Single(test.Requests);
        Assert.Equal(request.OperationId, Assert.Single(test.Cancellations));
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("ADS purchase observed:"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("read-exception")]
    [InlineData("operation")]
    [InlineData("item")]
    [InlineData("quantity")]
    [InlineData("running")]
    [InlineData("not-done")]
    [InlineData("running-and-done")]
    [InlineData("navigation-not-released")]
    [InlineData("missing-json-field")]
    [InlineData("missing-navigation-field")]
    public void ForeignMissingOrNonterminalStatusCannotReleaseTheRetainedMovementHold(string change)
    {
        var test = new Scenario();
        test.Profile.ChocoboGreensStockTarget = 5;
        test.Profile.ChocoboFoodStockTarget = 3;
        test.Service.Start(4868, 5);
        var request = Assert.Single(test.Requests);
        test.Service.Stop();
        test.Observed = new()
        {
            OperationId = request.OperationId, ItemId = (uint)request.ItemId,
            RequestedQuantity = request.Quantity, Done = true, Running = false,
            NavigationReleased = true, Succeeded = false,
        };
        switch (change)
        {
            case "missing": test.Observed = null; break;
            case "read-exception": test.ThrowOnRead = true; break;
            case "operation": test.Observed.OperationId = "unrelated-operation"; break;
            case "item": test.Observed.ItemId = 7897; break;
            case "quantity": test.Observed.RequestedQuantity++; break;
            case "running": test.Observed.Running = true; test.Observed.Done = false; break;
            case "not-done": test.Observed.Done = false; break;
            case "running-and-done": test.Observed.Running = true; break;
            case "navigation-not-released": test.Observed.NavigationReleased = false; break;
            case "missing-json-field":
            case "missing-navigation-field":
                var payload = JsonNode.Parse(JsonSerializer.Serialize(test.Observed,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }))!.AsObject();
                Assert.True(payload.Remove(change == "missing-navigation-field" ? "navigationReleased" : "running"));
                test.ObservedJson = payload.ToJsonString();
                break;
        }

        test.Service.Tick();
        test.Service.Tick();

        Assert.False(test.Service.IsActive);
        Assert.True(test.Service.HoldsMovement);
        Assert.True(test.Service.HasOwnedFlow);
        Assert.False(test.Service.Start(4868, 5));
        Assert.Single(test.Requests);
        Assert.Equal(request.OperationId, Assert.Single(test.Cancellations));
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("ADS purchase observed:"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoppedOrUnknownFlowRetainsMovementUntilNavigationIsReleasedWithoutReplaying(bool unknown)
    {
        var test = new Scenario();
        test.Profile.ChocoboGreensStockTarget = 5;
        test.Profile.ChocoboFoodStockTarget = 3;
        test.Service.Start(4868, 5);
        var request = Assert.Single(test.Requests);
        if (unknown)
        {
            test.Observed = null;
            test.Service.Tick();
        }
        else test.Service.Stop();
        test.Observed = new()
        {
            OperationId = request.OperationId, ItemId = (uint)request.ItemId,
            RequestedQuantity = request.Quantity, Done = true, Running = false,
            NavigationReleased = false, Succeeded = false, RemainingQuantity = request.Quantity,
        };

        test.Service.Tick();
        test.Service.Tick();

        Assert.False(test.Service.IsActive);
        Assert.True(test.Service.HoldsMovement);
        Assert.True(test.Service.HasOwnedFlow);
        Assert.False(test.Service.Start(4868, 5));
        Assert.Single(test.Requests);
        Assert.Equal(request.OperationId, Assert.Single(test.Cancellations));
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("ADS purchase observed:"));

        test.Observed.NavigationReleased = true;
        test.Service.Tick();

        Assert.False(test.Service.HoldsMovement);
        Assert.False(test.Service.HasOwnedFlow);
        Assert.False(test.Service.Start(4868, 5));
        Assert.Single(test.Requests);
        Assert.Single(test.Cancellations);
        Assert.DoesNotContain(test.Logs, line => line.StartsWith("ADS purchase observed:"));
    }

    [Fact]
    public void OwnedAdsTravelCanChangeTerritoryWithoutChangingPurchaseOwnership()
    {
        var test = new Scenario();
        Assert.True(test.Service.Start(7897, 3));
        var request = Assert.Single(test.Requests);

        test.Territory = 398;
        test.Service.Tick();
        Assert.Equal(398u, test.Territory);
        Assert.True(test.Service.IsActive);
        Assert.True(test.Service.AllowsOwnedTravel);
        Assert.Empty(test.Cancellations);
        test.PublishSuccess();
        test.Service.Tick();

        Assert.Equal(request.OperationId, Assert.Single(test.Requests).OperationId);
        Assert.False(test.Service.HasOwnedFlow);
        Assert.Empty(test.Cancellations);
    }

    private sealed class Scenario
    {
        internal CharacterConfig Profile = new() { Enabled = true, ChocoboFoodItemId = 7897 };
        internal readonly Dictionary<int, int> Stocks = new() { [4868] = 2, [7897] = 1 };
        internal readonly List<CompanionAdsPurchaseRequest> Requests = [];
        internal readonly List<string> Cancellations = [];
        internal readonly List<string> Logs = [];
        internal CompanionAdsPurchaseStatus? Observed;
        internal string? ObservedJson;
        internal bool Safe = true;
        internal ulong Owner = 1;
        internal long Gil = 10000;
        internal long Now;
        internal uint Territory = 132;
        internal bool Accept = true;
        internal bool ThrowOnStart;
        internal bool ThrowOnRead;
        internal bool ThrowOnCancel;
        internal ChocoboPurchaseService Service { get; }

        internal Scenario()
        {
            Service = new(() => Safe, () => Owner, () => Profile,
                item => Stocks.GetValueOrDefault(item, -1), () => Gil,
                request =>
                {
                    Requests.Add(request);
                    Observed = new()
                    {
                        OperationId = request.OperationId, ItemId = (uint)request.ItemId,
                        RequestedQuantity = request.Quantity, RemainingQuantity = request.Quantity, Running = true,
                    };
                    if (ThrowOnStart) throw new InvalidOperationException("Synthetic start failure.");
                    return Accept;
                },
                () => ThrowOnRead ? throw new InvalidOperationException("Synthetic status failure.")
                    : ObservedJson == null ? Observed : CompanionAdsPurchaseStatus.Parse(ObservedJson),
                operationId =>
                {
                    Cancellations.Add(operationId);
                    if (ThrowOnCancel) throw new InvalidOperationException("Synthetic cancel failure.");
                }, Logs.Add, () => Now);
        }

        internal void PublishSuccess()
        {
            var request = Requests[^1];
            var cost = (long)request.Quantity * ChocoboPurchaseService.UnitPrice(request.ItemId);
            Stocks[request.ItemId] += request.Quantity;
            Gil -= cost;
            Observed = new()
            {
                OperationId = request.OperationId, ItemId = (uint)request.ItemId,
                RequestedQuantity = request.Quantity, AcquiredQuantity = request.Quantity,
                RemainingQuantity = 0, Running = false, Done = true, NavigationReleased = true, Succeeded = true,
                SelectedOffer = new()
                {
                    ShopId = request.ItemId == 4868 ? 262151u : 262698u, ReceiveCount = 1,
                    Currencies = [new() { Kind = "gil", ItemId = 1, RequiredAmount = cost, AvailabilityKnown = true }],
                },
            };
        }
    }
}
