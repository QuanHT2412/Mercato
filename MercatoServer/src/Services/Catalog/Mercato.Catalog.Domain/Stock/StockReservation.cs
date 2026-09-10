namespace Mercato.Catalog.Domain.Stock;

// A hold on some units of a variant for one order, alive until ExpiresAt. OrderId is a
// bare Guid: the order lives in ordering_db and there is no FK across services (ADR 0001).
public sealed class StockReservation
{
    public Guid Id { get; private set; }
    public Guid VariantId { get; private set; }
    public Guid OrderId { get; private set; }
    public int Quantity { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // EF Core materialization only — real instances are created via ProductVariant.Reserve.
    private StockReservation()
    {
    }

    private StockReservation(Guid id, Guid variantId, Guid orderId, int quantity, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        Id = id;
        VariantId = variantId;
        OrderId = orderId;
        Quantity = quantity;
        ExpiresAt = expiresAt;
        CreatedAt = now;
    }

    internal static StockReservation Create(Guid variantId, Guid orderId, int quantity, DateTimeOffset expiresAt, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), variantId, orderId, quantity, expiresAt, now);

    // An expired reservation stops counting against availability on its own, even if
    // nobody got around to deleting the row yet.
    public bool IsLive(DateTimeOffset now) => now < ExpiresAt;
}
