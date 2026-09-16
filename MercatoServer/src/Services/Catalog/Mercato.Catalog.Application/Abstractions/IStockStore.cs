using Mercato.Catalog.Domain.Products;
using Mercato.Catalog.Domain.Stock;

namespace Mercato.Catalog.Application.Abstractions;

public sealed record LockedVariant(ProductVariant Variant, string ProductName, bool ProductIsActive);

// Everything the stock handlers need from persistence. The interesting part is
// LockVariantsAsync: checking availability and writing the reservation must be one
// atomic step, otherwise two concurrent checkouts both see "1 left" and both reserve it.
public interface IStockStore
{
    // Runs `work` inside one database transaction (retried as a whole on transient
    // failures); the row locks taken by LockVariantsAsync are held until it ends.
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);

    // SELECT ... FOR UPDATE on the variant rows, in a stable order so two transactions
    // locking overlapping sets cannot deadlock. Unknown ids are simply absent.
    Task<IReadOnlyList<LockedVariant>> LockVariantsAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, int>> GetReservedQuantitiesAsync(
        IReadOnlyCollection<Guid> variantIds, DateTimeOffset now, CancellationToken cancellationToken);

    Task<IReadOnlyList<StockReservation>> GetReservationsAsync(Guid orderId, CancellationToken cancellationToken);

    void AddReservations(IEnumerable<StockReservation> reservations);

    void RemoveReservations(IEnumerable<StockReservation> reservations);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
