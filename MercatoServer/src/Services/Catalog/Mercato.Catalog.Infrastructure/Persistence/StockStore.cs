using Mercato.Catalog.Application.Abstractions;
using Mercato.Catalog.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace Mercato.Catalog.Infrastructure.Persistence;

internal sealed class StockStore(CatalogDbContext dbContext) : IStockStore
{
    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        // Aspire's Npgsql component enables retry-on-failure, and a retrying execution
        // strategy refuses user-started transactions unless the WHOLE unit is handed to
        // it — so that it can re-run begin/work/commit together after a transient error.
        var strategy = dbContext.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(
            async ct =>
            {
                // A retry must start clean, not on top of entities tracked by the failed attempt.
                dbContext.ChangeTracker.Clear();

                await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
                var result = await work(ct);
                await transaction.CommitAsync(ct);

                return result;
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<LockedVariant>> LockVariantsAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken)
    {
        var ids = variantIds.ToArray();

        // Pessimistic row locks: a second transaction asking for any of these variants
        // waits here until the first commits, then re-reads — so "check availability,
        // then reserve" cannot interleave. ORDER BY id gives every transaction the same
        // lock order, which rules out deadlocks between overlapping carts.
        var variants = await dbContext.ProductVariants
            .FromSql($"SELECT * FROM product_variants WHERE id = ANY({ids}) ORDER BY id FOR UPDATE")
            .ToListAsync(cancellationToken);

        var productIds = variants.Select(v => v.ProductId).Distinct().ToArray();
        var products = await dbContext.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.IsActive })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        return [.. variants.Select(v => new LockedVariant(v, products[v.ProductId].Name, products[v.ProductId].IsActive))];
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetReservedQuantitiesAsync(
        IReadOnlyCollection<Guid> variantIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ids = variantIds.ToArray();

        // Expired holds are filtered out here rather than deleted by a sweeper: they stop
        // counting the instant they expire, with no background job in the critical path.
        return await dbContext.StockReservations
            .Where(r => ids.Contains(r.VariantId) && r.ExpiresAt > now)
            .GroupBy(r => r.VariantId)
            .Select(g => new { VariantId = g.Key, Quantity = g.Sum(r => r.Quantity) })
            .ToDictionaryAsync(x => x.VariantId, x => x.Quantity, cancellationToken);
    }

    public async Task<IReadOnlyList<StockReservation>> GetReservationsAsync(Guid orderId, CancellationToken cancellationToken) =>
        await dbContext.StockReservations.Where(r => r.OrderId == orderId).ToListAsync(cancellationToken);

    public void AddReservations(IEnumerable<StockReservation> reservations) =>
        dbContext.StockReservations.AddRange(reservations);

    public void RemoveReservations(IEnumerable<StockReservation> reservations) =>
        dbContext.StockReservations.RemoveRange(reservations);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
