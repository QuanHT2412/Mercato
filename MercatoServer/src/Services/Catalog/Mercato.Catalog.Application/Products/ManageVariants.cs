using Mercato.Catalog.Application.Abstractions;
using Mercato.Catalog.Application.Exceptions;

namespace Mercato.Catalog.Application.Products;

public sealed record AddVariantCommand(Guid ProductId, string Sku, string Name, decimal Price, int Stock, string? ImageUrl);

public sealed class AddVariantHandler(IProductRepository productRepository, ICatalogCache cache, TimeProvider timeProvider)
{
    public async Task<ProductDetails> HandleAsync(AddVariantCommand command, CancellationToken cancellationToken)
    {
        var product = await productRepository.GetByIdAsync(command.ProductId, cancellationToken)
            ?? throw new CatalogNotFoundException("Product", command.ProductId);

        var variant = product.AddVariant(
            command.Sku, command.Name, command.Price, command.Stock, command.ImageUrl, timeProvider.GetUtcNow());

        await SkuGuard.EnsureUnusedAsync(productRepository, [variant.Sku], cancellationToken);

        await productRepository.SaveChangesAsync(cancellationToken);
        await cache.InvalidateProductAsync(product.Id, cancellationToken);

        return ProductDetails.From(product);
    }
}

public sealed record UpdateVariantCommand(
    Guid ProductId,
    Guid VariantId,
    string Name,
    decimal Price,
    int Stock,
    string? ImageUrl,
    bool IsActive);

public sealed class UpdateVariantHandler(IProductRepository productRepository, ICatalogCache cache, TimeProvider timeProvider)
{
    public async Task<ProductDetails> HandleAsync(UpdateVariantCommand command, CancellationToken cancellationToken)
    {
        var product = await productRepository.GetByIdAsync(command.ProductId, cancellationToken)
            ?? throw new CatalogNotFoundException("Product", command.ProductId);

        var variant = product.FindVariant(command.VariantId)
            ?? throw new CatalogNotFoundException("Variant", command.VariantId);

        var now = timeProvider.GetUtcNow();
        variant.Update(command.Name, command.Price, command.ImageUrl, command.IsActive, now);

        if (command.Stock != variant.Stock)
        {
            // Changing the price never affects orders already placed (they snapshot it at
            // checkout); changing the stock must respect units already held for them.
            var reserved = await productRepository.GetReservedQuantityAsync(variant.Id, now, cancellationToken);
            variant.SetStock(command.Stock, reserved, now);
        }

        await productRepository.SaveChangesAsync(cancellationToken);
        await cache.InvalidateProductAsync(product.Id, cancellationToken);

        return ProductDetails.From(product);
    }
}
