using System.Collections.Concurrent;
using Mercato.Ordering.Application.Abstractions;

namespace Ordering.IntegrationTests;

// Stands in for Catalog. These tests are about Ordering's side of the checkout flow —
// what it asks Catalog for and what it does with each answer — so Catalog is replaced at
// the IStockGateway port. Catalog's own half (real row locks, real holds) is covered by
// Catalog.IntegrationTests against a real database.
public sealed class FakeStockGateway : IStockGateway
{
    private sealed record Listing(string ProductName, string VariantName, decimal Price, int Stock);

    private readonly ConcurrentDictionary<Guid, Listing> _listings = new();

    public ConcurrentQueue<Guid> ReservedOrderIds { get; } = new();

    public ConcurrentQueue<Guid> ConfirmedOrderIds { get; } = new();

    public ConcurrentQueue<Guid> ReleasedOrderIds { get; } = new();

    public Guid AddVariant(string productName, string variantName, decimal price, int stock)
    {
        var variantId = Guid.CreateVersion7();
        _listings[variantId] = new Listing(productName, variantName, price, stock);

        return variantId;
    }

    public void ChangePrice(Guid variantId, decimal price) =>
        _listings[variantId] = _listings[variantId] with { Price = price };

    public Task<StockReservationResult> ReserveAsync(
        Guid orderId, IReadOnlyList<StockLine> lines, TimeSpan holdFor, CancellationToken cancellationToken)
    {
        var reserved = new List<ReservedLine>(lines.Count);

        foreach (var line in lines)
        {
            if (!_listings.TryGetValue(line.VariantId, out var listing))
            {
                return Task.FromResult(new StockReservationResult(false, [], StockFailure.VariantNotFound, line.VariantId, 0));
            }

            if (line.Quantity > listing.Stock)
            {
                return Task.FromResult(new StockReservationResult(false, [], StockFailure.InsufficientStock, line.VariantId, listing.Stock));
            }

            reserved.Add(new ReservedLine(line.VariantId, listing.ProductName, listing.VariantName, listing.Price, line.Quantity));
        }

        ReservedOrderIds.Enqueue(orderId);

        return Task.FromResult(new StockReservationResult(true, reserved, StockFailure.None, null, 0));
    }

    public Task ConfirmAsync(Guid orderId, CancellationToken cancellationToken)
    {
        ConfirmedOrderIds.Enqueue(orderId);
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(Guid orderId, CancellationToken cancellationToken)
    {
        ReleasedOrderIds.Enqueue(orderId);
        return Task.CompletedTask;
    }
}
