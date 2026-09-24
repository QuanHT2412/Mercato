namespace Mercato.Catalog.Api.Contracts;

// Request bodies only. Responses reuse the Application read models (ProductDetails,
// ProductSummary, CategoryResult) as they are — they are already flat DTOs, and a second
// identical set of records plus a mapper would add nothing.

public sealed record CreateCategoryRequest(string Name, Guid? ParentId);

public sealed record VariantRequest(string Sku, string Name, decimal Price, int Stock, string? ImageUrl);

public sealed record CreateProductRequest(
    string Name,
    string? Description,
    Guid? CategoryId,
    IReadOnlyList<VariantRequest> Variants);

public sealed record UpdateProductRequest(string Name, string? Description, Guid? CategoryId, bool IsActive);

public sealed record UpdateVariantRequest(string Name, decimal Price, int Stock, string? ImageUrl, bool IsActive);

// Query string of GET /products. All optional; paging is clamped by the handler.
public sealed record ProductListRequest(string? Search, Guid? CategoryId, int? Page, int? PageSize, bool? IncludeInactive);
