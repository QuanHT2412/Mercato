using Mercato.Catalog.Domain.Categories;

namespace Mercato.Catalog.Application.Abstractions;

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken);

    Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);

    void Add(Category category);

    void Remove(Category category);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
