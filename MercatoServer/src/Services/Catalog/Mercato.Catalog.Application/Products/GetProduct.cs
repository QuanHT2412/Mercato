using Mercato.Catalog.Application.Abstractions;

namespace Mercato.Catalog.Application.Products;

public sealed class GetProductHandler(IProductQueries productQueries, ICatalogCache cache)
{
    // includeInactive = the caller is an admin: they must see deactivated products and
    // must never be shown a stale copy of what they just edited, so they skip the cache.
    public async Task<ProductDetails?> HandleAsync(Guid id, bool includeInactive, CancellationToken cancellationToken)
    {
        if (includeInactive)
        {
            return await productQueries.GetDetailsAsync(id, cancellationToken);
        }

        // Cache-aside: look in the cache, fall back to the database, then populate.
        var key = CatalogCacheKeys.Product(id);
        var cached = await cache.GetAsync<ProductDetails>(key, cancellationToken);

        if (cached is not null)
        {
            return cached;
        }

        var product = await productQueries.GetDetailsAsync(id, cancellationToken);

        // Only what the public may see is cached; "not found" is not cached at all
        // (negative caching would need its own invalidation on create/activate).
        if (product is null || !product.IsActive)
        {
            return null;
        }

        var visible = product with { Variants = [.. product.Variants.Where(v => v.IsActive)] };
        await cache.SetAsync(key, visible, CatalogCacheKeys.ProductTimeToLive, cancellationToken);

        return visible;
    }
}
