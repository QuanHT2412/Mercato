using Mercato.Catalog.Application.Abstractions;
using Mercato.Catalog.Application.Products;
using Microsoft.EntityFrameworkCore;

namespace Mercato.Catalog.Infrastructure.Persistence;

internal sealed class ProductQueries(CatalogDbContext dbContext) : IProductQueries
{
    public async Task<ProductDetails?> GetDetailsAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        return product is null ? null : ProductDetails.From(product);
    }

    public async Task<PagedResult<ProductSummary>> SearchAsync(ProductSearchQuery query, CancellationToken cancellationToken)
    {
        var products = dbContext.Products.AsNoTracking();

        if (!query.IncludeInactive)
        {
            products = products.Where(p => p.IsActive);
        }

        if (query.CategoryId is { } categoryId)
        {
            products = products.Where(p => p.CategoryId == categoryId);
        }

        if (query.Search is { Length: > 0 } search)
        {
            // First cut: substring match on the name. Good enough to wire the endpoint
            // end to end; cannot use an index and ignores the description.
            var pattern = $"%{EscapeLike(search)}%";
            products = products.Where(p => EF.Functions.ILike(p.Name, pattern, "\\"));
        }

        var totalCount = await products.CountAsync(cancellationToken);

        var items = await products
            .OrderByDescending(p => p.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new ProductSummary(
                p.Id,
                p.Name,
                p.CategoryId,
                p.Variants.Where(v => v.IsActive).Min(v => (decimal?)v.Price),
                p.Variants.Where(v => v.IsActive).OrderBy(v => v.Price).Select(v => v.ImageUrl).FirstOrDefault(),
                p.Variants.Any(v => v.IsActive && v.Stock > 0),
                p.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductSummary>(items, query.Page, query.PageSize, totalCount);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
