using System.Text;
using Mercato.Catalog.Application.Abstractions;
using Mercato.Catalog.Application.Products;
using Mercato.Catalog.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Mercato.Catalog.Infrastructure.Persistence;

internal sealed class ProductQueries(CatalogDbContext dbContext) : IProductQueries
{
    private const string SearchConfig = "simple";

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

        var tsQuery = ToPrefixTsQuery(query.Search);

        if (tsQuery is not null)
        {
            // search_vector @@ to_tsquery(...) — answered from the GIN index.
            products = products
                .Where(p => EF.Property<NpgsqlTsVector>(p, ProductConfiguration.SearchVectorProperty)
                    .Matches(EF.Functions.ToTsQuery(SearchConfig, tsQuery)));
        }

        var totalCount = await products.CountAsync(cancellationToken);

        // Searching: best match first (ts_rank honours the A/B weights, so a hit in the
        // name beats a hit in the description). Browsing: newest first.
        var ordered = tsQuery is not null
            ? products
                .OrderByDescending(p => EF.Property<NpgsqlTsVector>(p, ProductConfiguration.SearchVectorProperty)
                    .Rank(EF.Functions.ToTsQuery(SearchConfig, tsQuery)))
                .ThenByDescending(p => p.CreatedAt)
            : products.OrderByDescending(p => p.CreatedAt);

        var items = await ordered
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

    // "áo thu" -> "áo:* & thu:*": every word must match, each as a prefix, so results
    // narrow as the user types in the search box. Built by hand instead of passing the
    // raw text to to_tsquery, whose operator syntax (& | ! :* parentheses) would turn
    // arbitrary user input into syntax errors — only letters and digits survive here.
    internal static string? ToPrefixTsQuery(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var builder = new StringBuilder();
        var word = new StringBuilder();

        foreach (var c in search.ToLowerInvariant() + " ")
        {
            if (char.IsLetterOrDigit(c))
            {
                word.Append(c);
                continue;
            }

            if (word.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(" & ");
            }

            builder.Append(word).Append(":*");
            word.Clear();
        }

        return builder.Length == 0 ? null : builder.ToString();
    }
}
