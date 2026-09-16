using Mercato.Catalog.Application.Abstractions;
using Mercato.Catalog.Application.Exceptions;

namespace Mercato.Catalog.Application.Products;

public sealed record UpdateProductCommand(Guid Id, string Name, string? Description, Guid? CategoryId, bool IsActive);

public sealed class UpdateProductHandler(
    IProductRepository productRepository,
    ICategoryRepository categoryRepository,
    ICatalogCache cache,
    TimeProvider timeProvider)
{
    public async Task<ProductDetails> HandleAsync(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        var product = await productRepository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new CatalogNotFoundException("Product", command.Id);

        if (command.CategoryId is { } categoryId && !await categoryRepository.ExistsAsync(categoryId, cancellationToken))
        {
            throw new CatalogNotFoundException("Category", categoryId);
        }

        var now = timeProvider.GetUtcNow();
        product.UpdateDetails(command.Name, command.Description, command.CategoryId, now);

        if (command.IsActive)
        {
            product.Activate(now);
        }
        else
        {
            product.Deactivate(now);
        }

        await productRepository.SaveChangesAsync(cancellationToken);
        await cache.InvalidateProductAsync(product.Id, cancellationToken);

        return ProductDetails.From(product);
    }
}

// "Delete" in the admin UI. See Product.Deactivate for why this is not a hard delete.
public sealed class DeactivateProductHandler(IProductRepository productRepository, ICatalogCache cache, TimeProvider timeProvider)
{
    public async Task HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await productRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new CatalogNotFoundException("Product", id);

        product.Deactivate(timeProvider.GetUtcNow());

        await productRepository.SaveChangesAsync(cancellationToken);
        await cache.InvalidateProductAsync(product.Id, cancellationToken);
    }
}

internal static class CatalogCacheInvalidation
{
    // Explicit invalidation on write (CLAUDE.md 3.2): drop the product's own entry and
    // orphan every cached list page, since any of them might contain it.
    public static async Task InvalidateProductAsync(this ICatalogCache cache, Guid productId, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(CatalogCacheKeys.Product(productId), cancellationToken);
        await cache.BumpListVersionAsync(cancellationToken);
    }
}
