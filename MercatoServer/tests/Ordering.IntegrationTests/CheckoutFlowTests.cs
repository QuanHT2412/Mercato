using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mercato.Ordering.Api.Contracts;
using Mercato.Ordering.Application.Carts;
using Mercato.Ordering.Application.Orders;

namespace Ordering.IntegrationTests;

// The checkout flow end to end through HTTP: cart -> checkout -> pay | cancel | expire.
public class CheckoutFlowTests(OrderingApiFactory factory) : IClassFixture<OrderingApiFactory>
{
    private static readonly CheckoutRequest Shipping = new("Nguyen Van A", "0901234567", "12 Lê Lợi, Quận 1, TP.HCM", "Giao giờ hành chính");

    private static Task<HttpResponseMessage> PutItemAsync(HttpClient client, Guid variantId, int quantity, CancellationToken ct) =>
        client.PutAsJsonAsync($"/cart/items/{variantId}", new SetCartItemRequest(quantity), ct);

    private static async Task<OrderResult> GetOrderAsync(HttpClient client, Guid orderId, CancellationToken ct) =>
        (await client.GetFromJsonAsync<OrderResult>($"/orders/{orderId}", ct))!;

    // A customer with one item in the cart who has just checked out.
    private async Task<(HttpClient Client, OrderResult Order)> CheckoutAsync(CancellationToken ct)
    {
        var client = factory.CreateClientAs(Guid.CreateVersion7());
        var variantId = factory.Stock.AddVariant("Áo thun", "Trắng / M", 199_000m, stock: 10);
        await PutItemAsync(client, variantId, 2, ct);

        var response = await client.PostAsJsonAsync("/orders/checkout", Shipping, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (client, (await response.Content.ReadFromJsonAsync<OrderResult>(ct))!);
    }

    [Fact]
    public async Task Checkout_CreatesAnOrderAwaitingPayment_WithSnapshottedPrices_AndEmptiesTheCart()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClientAs(Guid.CreateVersion7());
        var shirt = factory.Stock.AddVariant("Áo thun", "Trắng / M", 199_000m, stock: 10);
        var jeans = factory.Stock.AddVariant("Quần jean", "Xanh / 30", 450_000m, stock: 3);
        await PutItemAsync(client, shirt, 2, ct);
        await PutItemAsync(client, jeans, 1, ct);

        var response = await client.PostAsJsonAsync("/orders/checkout", Shipping, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<OrderResult>(ct))!;
        Assert.Equal($"/orders/{order.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal("AwaitingPayment", order.Status);
        Assert.Equal(848_000m, order.TotalAmount);
        Assert.Equal(factory.Clock.GetUtcNow().AddMinutes(15), order.PaymentDeadline);
        Assert.Equal("Nguyen Van A", order.Receiver);
        Assert.Equal(["Quần jean", "Áo thun"], order.Items.Select(i => i.ProductName));
        Assert.Equal(398_000m, order.Items.Single(i => i.VariantId == shirt).Subtotal);

        // The stock was reserved for exactly this order, and the cart is now empty.
        Assert.Contains(order.Id, factory.Stock.ReservedOrderIds);
        Assert.Empty((await client.GetFromJsonAsync<CartResult>("/cart", ct))!.Items);

        // A later price change in the catalog does not touch the order.
        factory.Stock.ChangePrice(shirt, 999_000m);
        var reloaded = await GetOrderAsync(client, order.Id, ct);
        Assert.Equal(848_000m, reloaded.TotalAmount);
        Assert.Equal(199_000m, reloaded.Items.Single(i => i.VariantId == shirt).UnitPrice);

        var list = await client.GetFromJsonAsync<List<OrderSummary>>("/orders", ct);
        var summary = Assert.Single(list!);
        Assert.Equal(order.Id, summary.Id);
        Assert.Equal(3, summary.ItemCount);
    }

    [Fact]
    public async Task Checkout_WithAnEmptyCart_OrBadShippingDetails_IsABadRequest_AndReservesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClientAs(Guid.CreateVersion7());
        var reservedBefore = factory.Stock.ReservedOrderIds.Count;

        var emptyCart = await client.PostAsJsonAsync("/orders/checkout", Shipping, ct);
        Assert.Equal(HttpStatusCode.BadRequest, emptyCart.StatusCode);

        await PutItemAsync(client, factory.Stock.AddVariant("Áo thun", "Trắng / M", 199_000m, stock: 10), 1, ct);
        var noAddress = await client.PostAsJsonAsync("/orders/checkout", Shipping with { Address = " " }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, noAddress.StatusCode);

        Assert.Equal(reservedBefore, factory.Stock.ReservedOrderIds.Count);
        Assert.Empty((await client.GetFromJsonAsync<List<OrderSummary>>("/orders", ct))!);
    }

    [Fact]
    public async Task Checkout_WhenAnItemIsOutOfStock_IsAConflict_CreatesNoOrder_AndKeepsTheCart()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClientAs(Guid.CreateVersion7());
        var scarce = factory.Stock.AddVariant("Giày sneaker", "Trắng / 42", 890_000m, stock: 1);
        await PutItemAsync(client, scarce, 3, ct);

        var response = await client.PostAsJsonAsync("/orders/checkout", Shipping, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(scarce, problem.GetProperty("variantId").GetGuid());
        Assert.Equal(1, problem.GetProperty("availableQuantity").GetInt32());

        Assert.Empty((await client.GetFromJsonAsync<List<OrderSummary>>("/orders", ct))!);
        Assert.Single((await client.GetFromJsonAsync<CartResult>("/cart", ct))!.Items);
    }

    [Fact]
    public async Task Pay_ConfirmsTheOrder_CommitsTheStock_AndCannotBeRepeatedOrCancelledAfterwards()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, order) = await CheckoutAsync(ct);

        // No body at all: the mock provider approves by default.
        var pay = await client.PostAsync($"/orders/{order.Id}/pay", content: null, ct);

        Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
        Assert.Equal("Confirmed", (await pay.Content.ReadFromJsonAsync<OrderResult>(ct))!.Status);
        Assert.Contains(order.Id, factory.Stock.ConfirmedOrderIds);
        Assert.DoesNotContain(order.Id, factory.Stock.ReleasedOrderIds);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/orders/{order.Id}/pay", null, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/orders/{order.Id}/cancel", null, ct)).StatusCode);
        Assert.Equal("Confirmed", (await GetOrderAsync(client, order.Id, ct)).Status);
    }

    [Fact]
    public async Task Pay_Declined_Answers402_LeavesTheOrderPayable_AndARetrySucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, order) = await CheckoutAsync(ct);

        var declined = await client.PostAsJsonAsync($"/orders/{order.Id}/pay", new PayOrderRequest("tok_decline"), ct);

        Assert.Equal(HttpStatusCode.PaymentRequired, declined.StatusCode);
        Assert.Equal("AwaitingPayment", (await GetOrderAsync(client, order.Id, ct)).Status);
        Assert.DoesNotContain(order.Id, factory.Stock.ConfirmedOrderIds);
        Assert.DoesNotContain(order.Id, factory.Stock.ReleasedOrderIds);

        var retry = await client.PostAsJsonAsync($"/orders/{order.Id}/pay", new PayOrderRequest(null), ct);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("Confirmed", (await GetOrderAsync(client, order.Id, ct)).Status);
    }

    [Fact]
    public async Task Cancel_CancelsTheOrder_ReleasesTheStock_AndTheOrderCannotBePaidAfterwards()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, order) = await CheckoutAsync(ct);

        var cancel = await client.PostAsync($"/orders/{order.Id}/cancel", null, ct);

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal("Cancelled", (await cancel.Content.ReadFromJsonAsync<OrderResult>(ct))!.Status);
        Assert.Contains(order.Id, factory.Stock.ReleasedOrderIds);

        // A cancelled order is never reactivated.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/orders/{order.Id}/pay", null, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/orders/{order.Id}/cancel", null, ct)).StatusCode);
        Assert.DoesNotContain(order.Id, factory.Stock.ConfirmedOrderIds);
    }

    [Fact]
    public async Task AnotherUsersOrder_LooksLikeItDoesNotExist()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, order) = await CheckoutAsync(ct);
        var stranger = factory.CreateClientAs(Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/orders/{order.Id}", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/orders/{order.Id}/pay", null, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/orders/{order.Id}/cancel", null, ct)).StatusCode);
        Assert.Empty((await stranger.GetFromJsonAsync<List<OrderSummary>>("/orders", ct))!);

        Assert.DoesNotContain(order.Id, factory.Stock.ConfirmedOrderIds);
        Assert.DoesNotContain(order.Id, factory.Stock.ReleasedOrderIds);
    }

    [Fact]
    public async Task AnUnpaidOrder_ExpiresAfterFifteenMinutes_AndItsStockIsReleased()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, order) = await CheckoutAsync(ct);

        // One minute before the deadline the job leaves it alone.
        factory.Clock.Advance(TimeSpan.FromMinutes(14));
        await factory.RunExpiryJobAsync(ct);
        Assert.Equal("AwaitingPayment", (await GetOrderAsync(client, order.Id, ct)).Status);

        // At the deadline the order is unpayable at once — even though the job, which
        // only ticks once a minute, has not marked it Expired yet.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/orders/{order.Id}/pay", null, ct)).StatusCode);

        await factory.RunExpiryJobAsync(ct);

        Assert.Equal("Expired", (await GetOrderAsync(client, order.Id, ct)).Status);
        Assert.Contains(order.Id, factory.Stock.ReleasedOrderIds);
        Assert.DoesNotContain(order.Id, factory.Stock.ConfirmedOrderIds);

        // Expired is terminal, and running the job again does not release twice.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/orders/{order.Id}/pay", null, ct)).StatusCode);
        await factory.RunExpiryJobAsync(ct);
        Assert.Single(factory.Stock.ReleasedOrderIds, id => id == order.Id);
    }
}
