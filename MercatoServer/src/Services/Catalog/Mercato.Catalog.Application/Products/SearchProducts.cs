using Mercato.Catalog.Application.Abstractions;

namespace Mercato.Catalog.Application.Products;

public sealed class SearchProductsHandler(IProductQueries productQueries, ICatalogCache cache)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public async Task<PagedResult<ProductSummary>> HandleAsync(ProductSearchQuery query, CancellationToken cancellationToken)
    {
        query = query with
        {
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            Page = Math.Max(1, query.Page),
            PageSize = query.PageSize <= 0 ? DefaultPageSize : Math.Min(query.PageSize, MaxPageSize),
        };

        // Admin listing (includes inactive products) always reads the database.
        if (query.IncludeInactive)
        {
            return await productQueries.SearchAsync(query, cancellationToken);
        }

        var version = await cache.GetListVersionAsync(cancellationToken);
        var key = CatalogCacheKeys.ProductList(version, query.Search, query.CategoryId, query.Page, query.PageSize);
        var cached = await cache.GetAsync<PagedResult<ProductSummary>>(key, cancellationToken);

        if (cached is not null)
        {
            return cached;
        }

        var result = await productQueries.SearchAsync(query, cancellationToken);
        await cache.SetAsync(key, result, CatalogCacheKeys.ListTimeToLive, cancellationToken);

        return result;
    }
}
