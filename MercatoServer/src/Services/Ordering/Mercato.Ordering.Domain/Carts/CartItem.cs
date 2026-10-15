namespace Mercato.Ordering.Domain.Carts;

public sealed class CartItem
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }

    // Bare id of a Catalog variant: no FK across services (ADR 0001). Whether it still
    // exists and is in stock is only decided at checkout.
    public Guid VariantId { get; private set; }
    public int Quantity { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // EF Core materialization only — real instances are created via Cart.SetItem.
    private CartItem()
    {
    }

    internal CartItem(Guid userId, Guid variantId, int quantity, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        VariantId = variantId;
        Quantity = quantity;
        CreatedAt = now;
        UpdatedAt = now;
    }

    internal void ChangeQuantity(int quantity, DateTimeOffset now)
    {
        Quantity = quantity;
        UpdatedAt = now;
    }
}
