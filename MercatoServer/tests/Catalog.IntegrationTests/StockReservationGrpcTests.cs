using System.Net;
using System.Net.Http.Json;
using Grpc.Core;
using Mercato.Catalog.Api.Contracts;
using Mercato.Catalog.Application.Products;
using Mercato.Catalog.Grpc;

namespace Catalog.IntegrationTests;

public class StockReservationGrpcTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    private const int HoldSeconds = 900;

    private async Task<ProductVariantDetails> CreateVariantAsync(string sku, int stock, CancellationToken ct)
    {
        var admin = factory.CreateClientAs("Admin");
        var response = await admin.PostAsJsonAsync(
            "/products",
            new CreateProductRequest("Giày sneaker", null, null, [new VariantRequest(sku, "Trắng / 42", 890_000m, stock, null)]),
            ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ProductDetails>(ct))!.Variants.Single();
    }

    private async Task<int> GetStockAsync(Guid variantId, CancellationToken ct)
    {
        // Through the admin view so the answer comes from Postgres, not from the cache.
        var page = await factory.CreateClientAs("Admin")
            .GetFromJsonAsync<PagedResult<ProductSummary>>("/products?pageSize=50&includeInactive=true", ct);

        foreach (var summary in page!.Items)
        {
            var detail = await factory.CreateClientAs("Admin").GetFromJsonAsync<ProductDetails>($"/products/{summary.Id}", ct);
            var variant = detail!.Variants.FirstOrDefault(v => v.Id == variantId);

            if (variant is not null)
            {
                return variant.Stock;
            }
        }

        throw new InvalidOperationException($"Variant {variantId} not found.");
    }

    private static ReserveStockRequest Reserve(Guid orderId, Guid variantId, int quantity)
    {
        var request = new ReserveStockRequest { OrderId = orderId.ToString(), HoldSeconds = HoldSeconds };
        request.Lines.Add(new StockLine { VariantId = variantId.ToString(), Quantity = quantity });

        return request;
    }

    [Fact]
    public async Task Reserve_Release_Confirm_FollowTheHoldLifecycle()
    {
        var ct = TestContext.Current.CancellationToken;
        var stock = factory.CreateStockClient();
        var variant = await CreateVariantAsync("GRPC-LIFECYCLE", stock: 5, ct);
        var firstOrder = Guid.CreateVersion7();
        var secondOrder = Guid.CreateVersion7();

        // Order 1 holds 3 of 5. The reply carries the snapshot Ordering will store.
        var first = await stock.CheckAndReserveStockAsync(Reserve(firstOrder, variant.Id, 3), cancellationToken: ct);
        Assert.True(first.Reserved);
        var line = Assert.Single(first.Lines);
        Assert.Equal("Giày sneaker", line.ProductName);
        Assert.Equal("Trắng / 42", line.VariantName);
        Assert.Equal(890_000, line.UnitPrice);

        // Order 2 wants 3 but only 2 are left — a business outcome, not an RPC error.
        var second = await stock.CheckAndReserveStockAsync(Reserve(secondOrder, variant.Id, 3), cancellationToken: ct);
        Assert.False(second.Reserved);
        Assert.Equal(ReserveFailureReason.InsufficientStock, second.FailureReason);
        Assert.Equal(variant.Id.ToString(), second.FailedVariantId);
        Assert.Equal(2, second.AvailableQuantity);

        // A hold never touches on-hand stock.
        Assert.Equal(5, await GetStockAsync(variant.Id, ct));

        // Order 1 is cancelled: its units come back, order 2 now fits.
        Assert.True((await stock.ReleaseReservationAsync(new ReservationRequest { OrderId = firstOrder.ToString() }, cancellationToken: ct)).Found);
        Assert.False((await stock.ReleaseReservationAsync(new ReservationRequest { OrderId = firstOrder.ToString() }, cancellationToken: ct)).Found);
        Assert.True((await stock.CheckAndReserveStockAsync(Reserve(secondOrder, variant.Id, 3), cancellationToken: ct)).Reserved);

        // Order 2 is paid: stock really drops, exactly once even if confirmed twice.
        Assert.True((await stock.ConfirmReservationAsync(new ReservationRequest { OrderId = secondOrder.ToString() }, cancellationToken: ct)).Found);
        Assert.False((await stock.ConfirmReservationAsync(new ReservationRequest { OrderId = secondOrder.ToString() }, cancellationToken: ct)).Found);
        Assert.Equal(2, await GetStockAsync(variant.Id, ct));
    }

    [Fact]
    public async Task Reserve_RetriedForTheSameOrder_DoesNotHoldTwice()
    {
        var ct = TestContext.Current.CancellationToken;
        var stock = factory.CreateStockClient();
        var variant = await CreateVariantAsync("GRPC-IDEMPOTENT", stock: 4, ct);
        var orderId = Guid.CreateVersion7();

        Assert.True((await stock.CheckAndReserveStockAsync(Reserve(orderId, variant.Id, 3), cancellationToken: ct)).Reserved);
        Assert.True((await stock.CheckAndReserveStockAsync(Reserve(orderId, variant.Id, 3), cancellationToken: ct)).Reserved);

        // Had the retry held another 3, nothing would be left; exactly 1 still is.
        var other = await stock.CheckAndReserveStockAsync(Reserve(Guid.CreateVersion7(), variant.Id, 2), cancellationToken: ct);
        Assert.False(other.Reserved);
        Assert.Equal(1, other.AvailableQuantity);
    }

    [Fact]
    public async Task ConcurrentReservations_ForTheLastUnits_NeverOversell()
    {
        var ct = TestContext.Current.CancellationToken;
        var stock = factory.CreateStockClient();
        var variant = await CreateVariantAsync("GRPC-RACE", stock: 5, ct);

        // 20 checkouts race for 5 units, 1 each. Without the row lock, several of them
        // would read "5 available" at the same time and all succeed.
        var replies = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            stock.CheckAndReserveStockAsync(Reserve(Guid.CreateVersion7(), variant.Id, 1), cancellationToken: ct).ResponseAsync));

        Assert.Equal(5, replies.Count(r => r.Reserved));
        Assert.All(replies.Where(r => !r.Reserved), r => Assert.Equal(ReserveFailureReason.InsufficientStock, r.FailureReason));
    }

    [Fact]
    public async Task Reserve_WithAnUnknownVariant_SaysSo_AndAMalformedIdIsAnInvalidArgument()
    {
        var ct = TestContext.Current.CancellationToken;
        var stock = factory.CreateStockClient();
        var unknown = Guid.CreateVersion7();

        var reply = await stock.CheckAndReserveStockAsync(Reserve(Guid.CreateVersion7(), unknown, 1), cancellationToken: ct);
        Assert.False(reply.Reserved);
        Assert.Equal(ReserveFailureReason.VariantNotFound, reply.FailureReason);

        var malformed = new ReserveStockRequest { OrderId = "not-a-uuid", HoldSeconds = HoldSeconds };
        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await stock.CheckAndReserveStockAsync(malformed, cancellationToken: ct));
        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }
}
