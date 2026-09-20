using Mercato.Catalog.Application.Abstractions;
using Mercato.Catalog.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Mercato.Catalog.Infrastructure.Persistence;

internal sealed class ProductRepository(CatalogDbContext dbContext) : IProductRepository
{
    public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Products.Include(p => p.Variants).SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<string>> FindExistingSkusAsync(IReadOnlyCollection<string> skus, CancellationToken cancellationToken) =>
        await dbContext.ProductVariants
            .Where(v => skus.Contains(v.Sku))
            .Select(v => v.Sku)
            .ToListAsync(cancellationToken);

    public async Task<int> GetReservedQuantityAsync(Guid variantId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await dbContext.StockReservations
            .Where(r => r.VariantId == variantId && r.ExpiresAt > now)
            .SumAsync(r => (int?)r.Quantity, cancellationToken) ?? 0;

    public void Add(Product product) => dbContext.Products.Add(product);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
