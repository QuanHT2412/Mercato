using Mercato.Catalog.Domain.Exceptions;

namespace Mercato.Catalog.Domain.Products;

// Aggregate root. A product is what a customer browses; its variants are what they
// actually buy (each has its own SKU, price and stock), so a product always has at
// least one.
public sealed class Product
{
    private readonly List<ProductVariant> _variants = [];

    public Guid Id { get; private set; }
    public Guid? CategoryId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<ProductVariant> Variants => _variants;

    // EF Core materialization only — real instances are created via Create.
    private Product()
    {
        Name = null!;
    }

    private Product(Guid id, string name, string? description, Guid? categoryId, DateTimeOffset now)
    {
        Id = id;
        Name = name;
        Description = description;
        CategoryId = categoryId;
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static Product Create(string name, string? description, Guid? categoryId, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), ValidateName(name), NormalizeDescription(description), categoryId, now);

    public void UpdateDetails(string name, string? description, Guid? categoryId, DateTimeOffset now)
    {
        Name = ValidateName(name);
        Description = NormalizeDescription(description);
        CategoryId = categoryId;
        UpdatedAt = now;
    }

    // Products are never hard-deleted: existing orders keep referring to their variants.
    // "Delete" from the admin's point of view is deactivation — hidden from browse/search
    // and no longer reservable.
    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        UpdatedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        UpdatedAt = now;
    }

    public ProductVariant AddVariant(string sku, string name, decimal price, int stock, string? imageUrl, DateTimeOffset now)
    {
        var variant = ProductVariant.Create(Id, sku, name, price, stock, imageUrl, now);

        if (_variants.Any(v => string.Equals(v.Sku, variant.Sku, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainValidationException($"SKU '{variant.Sku}' is already used by another variant of this product.");
        }

        _variants.Add(variant);
        UpdatedAt = now;

        return variant;
    }

    public ProductVariant? FindVariant(Guid variantId) => _variants.FirstOrDefault(v => v.Id == variantId);

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainValidationException("Product name must not be empty.");
        }

        name = name.Trim();

        if (name.Length > 255)
        {
            throw new DomainValidationException("Product name must not exceed 255 characters.");
        }

        return name;
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
