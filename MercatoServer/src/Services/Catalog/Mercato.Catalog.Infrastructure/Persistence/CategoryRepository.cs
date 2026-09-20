using Mercato.Catalog.Application.Abstractions;
using Mercato.Catalog.Domain.Categories;
using Microsoft.EntityFrameworkCore;

namespace Mercato.Catalog.Infrastructure.Persistence;

internal sealed class CategoryRepository(CatalogDbContext dbContext) : ICategoryRepository
{
    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);

    public Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Categories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Categories.AnyAsync(c => c.Id == id, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        dbContext.Categories.AnyAsync(c => c.Slug == slug, cancellationToken);

    public void Add(Category category) => dbContext.Categories.Add(category);

    public void Remove(Category category) => dbContext.Categories.Remove(category);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
