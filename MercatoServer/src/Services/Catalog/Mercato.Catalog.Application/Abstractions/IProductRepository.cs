using Mercato.Catalog.Domain.Products;

namespace Mercato.Catalog.Application.Abstractions;

// Write side: loads the whole aggregate (product + variants), tracked, for commands.
// Reads for browse/search go through IProductQueries instead.
public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    // SKUs are unique across the whole catalog, not just within one product.
    Task<IReadOnlyList<string>> FindExistingSkusAsync(IReadOnlyCollection<string> skus, CancellationToken cancellationToken);

    // Units of this variant currently held by live (unexpired) reservations.
    Task<int> GetReservedQuantityAsync(Guid variantId, DateTimeOffset now, CancellationToken cancellationToken);

    void Add(Product product);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
