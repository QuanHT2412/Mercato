using Mercato.Catalog.Application.Products;

namespace Mercato.Catalog.Application.Abstractions;

// Read side: projects straight to read models, untracked. Not a separate read store —
// same database, same tables (full CQRS is out of scope), just a different shape of query.
public interface IProductQueries
{
    Task<ProductDetails?> GetDetailsAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<ProductSummary>> SearchAsync(ProductSearchQuery query, CancellationToken cancellationToken);
}
