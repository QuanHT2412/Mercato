using Mercato.Catalog.Domain.Exceptions;

namespace Mercato.Catalog.Domain.Categories;

public sealed class Category
{
    public Guid Id { get; private set; }
    public Guid? ParentId { get; private set; }
    public string Name { get; private set; }
    public string Slug { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // EF Core materialization only — real instances are created via Create.
    private Category()
    {
        Name = null!;
        Slug = null!;
    }

    private Category(Guid id, string name, string slug, Guid? parentId, DateTimeOffset now)
    {
        Id = id;
        Name = name;
        Slug = slug;
        ParentId = parentId;
        CreatedAt = now;
    }

    public static Category Create(string name, Guid? parentId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainValidationException("Category name must not be empty.");
        }

        name = name.Trim();

        if (name.Length > 100)
        {
            throw new DomainValidationException("Category name must not exceed 100 characters.");
        }

        var slug = Categories.Slug.From(name);

        if (slug.Length == 0)
        {
            throw new DomainValidationException("Category name must contain at least one letter or digit.");
        }

        return new Category(Guid.CreateVersion7(), name, slug, parentId, now);
    }
}
