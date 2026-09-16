using Mercato.Catalog.Domain.Products;

namespace Mercato.Catalog.Application.Products;

public sealed record ProductSummary(
    Guid Id,
    string Name,
    Guid? CategoryId,
    decimal? PriceFrom,
    string? ImageUrl,
    bool InStock,
    bool IsActive);

public sealed record ProductVariantDetails(
    Guid Id,
    string Sku,
    string Name,
    decimal Price,
    int Stock,
    string? ImageUrl,
    bool IsActive);

public sealed record ProductDetails(
    Guid Id,
    string Name,
    string? Description,
    Guid? CategoryId,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ProductVariantDetails> Variants)
{
    public static ProductDetails From(Product product) => new(
        product.Id,
        product.Name,
        product.Description,
        product.CategoryId,
        product.IsActive,
        product.CreatedAt,
        product.UpdatedAt,
        [.. product.Variants
            .OrderBy(v => v.CreatedAt)
            .Select(v => new ProductVariantDetails(v.Id, v.Sku, v.Name, v.Price, v.Stock, v.ImageUrl, v.IsActive))]);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record ProductSearchQuery(string? Search, Guid? CategoryId, int Page, int PageSize, bool IncludeInactive);
