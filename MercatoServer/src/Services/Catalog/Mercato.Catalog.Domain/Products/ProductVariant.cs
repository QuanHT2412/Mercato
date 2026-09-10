using Mercato.Catalog.Domain.Exceptions;
using Mercato.Catalog.Domain.Stock;

namespace Mercato.Catalog.Domain.Products;

public sealed class ProductVariant
{
    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public string Sku { get; private set; }
    public string Name { get; private set; }

    // VND: decimal, never floating point, and no fractional part (CLAUDE.md section 6).
    public decimal Price { get; private set; }

    // Units physically on hand. What a customer can still buy is Stock minus the
    // quantity held by live reservations — see Reserve.
    public int Stock { get; private set; }
    public string? ImageUrl { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // EF Core materialization only — real instances are created via Product.AddVariant.
    private ProductVariant()
    {
        Sku = null!;
        Name = null!;
    }

    private ProductVariant(Guid id, Guid productId, string sku, string name, decimal price, int stock, string? imageUrl, DateTimeOffset now)
    {
        Id = id;
        ProductId = productId;
        Sku = sku;
        Name = name;
        Price = price;
        Stock = stock;
        ImageUrl = imageUrl;
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    internal static ProductVariant Create(Guid productId, string sku, string name, decimal price, int stock, string? imageUrl, DateTimeOffset now) =>
        new(
            Guid.CreateVersion7(),
            productId,
            ValidateSku(sku),
            ValidateName(name),
            ValidatePrice(price),
            ValidateStock(stock),
            NormalizeImageUrl(imageUrl),
            now);

    public void Update(string name, decimal price, string? imageUrl, bool isActive, DateTimeOffset now)
    {
        Name = ValidateName(name);
        Price = ValidatePrice(price);
        ImageUrl = NormalizeImageUrl(imageUrl);
        IsActive = isActive;
        UpdatedAt = now;
    }

    // Admin stock correction (restock / inventory count). `reservedQuantity` is what live
    // reservations currently hold: the shelf cannot be set below what is already promised
    // to customers who are in their payment window.
    public void SetStock(int stock, int reservedQuantity, DateTimeOffset now)
    {
        ValidateStock(stock);

        if (stock < reservedQuantity)
        {
            throw new DomainValidationException(
                $"Stock cannot be set to {stock}: {reservedQuantity} unit(s) are reserved by orders awaiting payment.");
        }

        Stock = stock;
        UpdatedAt = now;
    }

    public int AvailableQuantity(int reservedQuantity) => Math.Max(0, Stock - reservedQuantity);

    // Holds `quantity` units for an order during its payment window WITHOUT touching
    // Stock: nothing has been sold yet. `reservedQuantity` is the total already held by
    // other live reservations of this variant.
    public StockReservation Reserve(Guid orderId, int quantity, int reservedQuantity, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (quantity <= 0)
        {
            throw new DomainValidationException("Reserved quantity must be positive.");
        }

        var available = IsActive ? AvailableQuantity(reservedQuantity) : 0;

        if (quantity > available)
        {
            throw new InsufficientStockException(Id, quantity, available);
        }

        return StockReservation.Create(Id, orderId, quantity, expiresAt, now);
    }

    // The order was paid: the held units really leave the shelf. Stock can never go
    // negative (CLAUDE.md section 6) — reachable only if a reservation is committed after
    // it expired and its units were sold to someone else in the meantime.
    public void CommitReservation(StockReservation reservation, DateTimeOffset now)
    {
        if (reservation.VariantId != Id)
        {
            throw new DomainValidationException("Reservation belongs to a different variant.");
        }

        if (reservation.Quantity > Stock)
        {
            throw new InsufficientStockException(Id, reservation.Quantity, Stock);
        }

        Stock -= reservation.Quantity;
        UpdatedAt = now;
    }

    private static string ValidateSku(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new DomainValidationException("SKU must not be empty.");
        }

        sku = sku.Trim().ToUpperInvariant();

        if (sku.Length > 100)
        {
            throw new DomainValidationException("SKU must not exceed 100 characters.");
        }

        return sku;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainValidationException("Variant name must not be empty.");
        }

        name = name.Trim();

        if (name.Length > 255)
        {
            throw new DomainValidationException("Variant name must not exceed 255 characters.");
        }

        return name;
    }

    private static decimal ValidatePrice(decimal price)
    {
        if (price < 0)
        {
            throw new DomainValidationException("Price must not be negative.");
        }

        if (price != decimal.Truncate(price))
        {
            throw new DomainValidationException("Price is in VND and must not have a fractional part.");
        }

        return price;
    }

    private static int ValidateStock(int stock)
    {
        if (stock < 0)
        {
            throw new DomainValidationException("Stock must not be negative.");
        }

        return stock;
    }

    private static string? NormalizeImageUrl(string? imageUrl) =>
        string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim();
}
