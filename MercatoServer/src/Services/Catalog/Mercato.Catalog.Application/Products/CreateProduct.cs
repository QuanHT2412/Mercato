using Mercato.Catalog.Application.Abstractions;
using Mercato.Catalog.Application.Exceptions;
using Mercato.Catalog.Domain.Exceptions;
using Mercato.Catalog.Domain.Products;

namespace Mercato.Catalog.Application.Products;

public sealed record NewVariant(string Sku, string Name, decimal Price, int Stock, string? ImageUrl);

public sealed record CreateProductCommand(string Name, string? Description, Guid? CategoryId, IReadOnlyList<NewVariant> Variants);

public sealed class CreateProductHandler(
    IProductRepository productRepository,
    ICategoryRepository categoryRepository,
    ICatalogCache cache,
    TimeProvider timeProvider)
{
    public async Task<ProductDetails> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken)
    {
        if (command.Variants is null || command.Variants.Count == 0)
        {
            throw new DomainValidationException("A product needs at least one variant.");
        }

        if (command.CategoryId is { } categoryId && !await categoryRepository.ExistsAsync(categoryId, cancellationToken))
        {
            throw new CatalogNotFoundException("Category", categoryId);
        }

        var now = timeProvider.GetUtcNow();
        var product = Product.Create(command.Name, command.Description, command.CategoryId, now);

        foreach (var variant in command.Variants)
        {
            product.AddVariant(variant.Sku, variant.Name, variant.Price, variant.Stock, variant.ImageUrl, now);
        }

        await SkuGuard.EnsureUnusedAsync(productRepository, [.. product.Variants.Select(v => v.Sku)], cancellationToken);

        productRepository.Add(product);
        await productRepository.SaveChangesAsync(cancellationToken);

        // A new product changes what every list/search page should return.
        await cache.BumpListVersionAsync(cancellationToken);

        return ProductDetails.From(product);
    }
}

internal static class SkuGuard
{
    // Friendly 409 for the common case; the unique index on product_variants.sku is what
    // actually guarantees it under concurrency.
    public static async Task EnsureUnusedAsync(IProductRepository repository, IReadOnlyCollection<string> skus, CancellationToken cancellationToken)
    {
        var taken = await repository.FindExistingSkusAsync(skus, cancellationToken);

        if (taken.Count > 0)
        {
            throw new CatalogConflictException($"SKU already in use: {string.Join(", ", taken)}.");
        }
    }
}
