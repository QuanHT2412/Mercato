using Mercato.Catalog.Application.Abstractions;
using Mercato.Catalog.Application.Products;

namespace Mercato.Catalog.Application.Stock;

// Compensation: the order was cancelled or expired, give the held units back. Releasing
// never touches Stock (a hold never decremented it) — it just removes the hold.
public sealed class ReleaseStockHandler(IStockStore stockStore)
{
    // Returns false when the order holds nothing — already released, or never reserved.
    // Both are fine: release is idempotent so the caller can always retry it safely.
    public async Task<bool> HandleAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var reservations = await stockStore.GetReservationsAsync(orderId, cancellationToken);

        if (reservations.Count == 0)
        {
            return false;
        }

        stockStore.RemoveReservations(reservations);
        await stockStore.SaveChangesAsync(cancellationToken);

        return true;
    }
}

// The order was paid: turn the hold into a real stock decrement.
public sealed class ConfirmStockHandler(IStockStore stockStore, ICatalogCache cache, TimeProvider timeProvider)
{
    // Returns false when the order holds nothing — i.e. it was already confirmed (or
    // released). Deleting the hold in the same transaction as the decrement is what makes
    // a repeated confirm a no-op instead of a second decrement.
    public async Task<bool> HandleAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var productIds = await stockStore.ExecuteInTransactionAsync(
            async ct =>
            {
                var reservations = await stockStore.GetReservationsAsync(orderId, ct);

                if (reservations.Count == 0)
                {
                    return [];
                }

                var locked = await stockStore.LockVariantsAsync([.. reservations.Select(r => r.VariantId)], ct);
                var byId = locked.ToDictionary(l => l.Variant.Id, l => l.Variant);
                var now = timeProvider.GetUtcNow();

                foreach (var reservation in reservations)
                {
                    byId[reservation.VariantId].CommitReservation(reservation, now);
                }

                stockStore.RemoveReservations(reservations);
                await stockStore.SaveChangesAsync(ct);

                return byId.Values.Select(v => v.ProductId).Distinct().ToList();
            },
            cancellationToken);

        // Only after the transaction committed: cached product pages show the old stock.
        foreach (var productId in productIds)
        {
            await cache.InvalidateProductAsync(productId, cancellationToken);
        }

        return productIds.Count > 0;
    }
}
