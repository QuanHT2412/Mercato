using Mercato.Catalog.Application.Abstractions;
using Mercato.Catalog.Application.Exceptions;
using Mercato.Catalog.Domain.Categories;

namespace Mercato.Catalog.Application.Categories;

public sealed record CategoryResult(Guid Id, Guid? ParentId, string Name, string Slug)
{
    public static CategoryResult From(Category category) => new(category.Id, category.ParentId, category.Name, category.Slug);
}

public sealed record CreateCategoryCommand(string Name, Guid? ParentId);

public sealed class CreateCategoryHandler(ICategoryRepository categoryRepository, TimeProvider timeProvider)
{
    public async Task<CategoryResult> HandleAsync(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        if (command.ParentId is { } parentId && !await categoryRepository.ExistsAsync(parentId, cancellationToken))
        {
            throw new CatalogNotFoundException("Category", parentId);
        }

        var category = Category.Create(command.Name, command.ParentId, timeProvider.GetUtcNow());

        if (await categoryRepository.SlugExistsAsync(category.Slug, cancellationToken))
        {
            throw new CatalogConflictException($"A category with slug '{category.Slug}' already exists.");
        }

        categoryRepository.Add(category);
        await categoryRepository.SaveChangesAsync(cancellationToken);

        return CategoryResult.From(category);
    }
}

public sealed class ListCategoriesHandler(ICategoryRepository categoryRepository)
{
    public async Task<IReadOnlyList<CategoryResult>> HandleAsync(CancellationToken cancellationToken)
    {
        var categories = await categoryRepository.ListAsync(cancellationToken);

        return [.. categories.Select(CategoryResult.From)];
    }
}

public sealed class DeleteCategoryHandler(ICategoryRepository categoryRepository, ICatalogCache cache)
{
    public async Task HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var category = await categoryRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new CatalogNotFoundException("Category", id);

        // Products and child categories are not deleted with it: their category/parent
        // is set to NULL by the database (ON DELETE SET NULL).
        categoryRepository.Remove(category);
        await categoryRepository.SaveChangesAsync(cancellationToken);

        // Cached list pages may be filtered by, or show, the deleted category.
        await cache.BumpListVersionAsync(cancellationToken);
    }
}
