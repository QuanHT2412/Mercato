using Mercato.Catalog.Application.Abstractions;
using Mercato.Catalog.Domain.Products;
using Mercato.Catalog.Domain.Stock;

namespace Catalog.Tests.Application;

// In-memory IStockStore. It cannot reproduce row locking (that is what the Postgres
// integration tests are for) — it exists so the reservation RULES can be tested fast.
internal sealed class FakeStockStore : IStockStore
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly List<LockedVariant> _variants = [];

    public List<StockReservation> Reservations { get; } = [];

    public int SaveCount { get; private set; }

    public ProductVariant AddVariant(string productName, string variantName, decimal price, int stock, bool productIsActive = true)
    {
        var product = Product.Create(productName, null, null, CreatedAt);
        var variant = product.AddVariant($"SKU-{_variants.Count + 1}", variantName, price, stock, null, CreatedAt);
        _variants.Add(new LockedVariant(variant, productName, productIsActive));

        return variant;
    }

    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken) =>
        work(cancellationToken);

    public Task<IReadOnlyList<LockedVariant>> LockVariantsAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LockedVariant>>([.. _variants.Where(v => variantIds.Contains(v.Variant.Id))]);

    public Task<IReadOnlyDictionary<Guid, int>> GetReservedQuantitiesAsync(
        IReadOnlyCollection<Guid> variantIds, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, int>>(Reservations
            .Where(r => variantIds.Contains(r.VariantId) && r.IsLive(now))
            .GroupBy(r => r.VariantId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity)));

    public Task<IReadOnlyList<StockReservation>> GetReservationsAsync(Guid orderId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StockReservation>>([.. Reservations.Where(r => r.OrderId == orderId)]);

    public void AddReservations(IEnumerable<StockReservation> reservations) => Reservations.AddRange(reservations);

    public void RemoveReservations(IEnumerable<StockReservation> reservations)
    {
        foreach (var reservation in reservations.ToList())
        {
            Reservations.Remove(reservation);
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
