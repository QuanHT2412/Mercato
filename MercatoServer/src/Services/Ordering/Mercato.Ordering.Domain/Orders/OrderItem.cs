namespace Mercato.Ordering.Domain.Orders;

// What the customer is buying, as Catalog described it at the moment of checkout.
public sealed record OrderLine(Guid VariantId, string ProductName, string VariantName, decimal UnitPrice, int Quantity);

// A snapshot, deliberately duplicated from Catalog: an order must keep showing what was
// bought and at what price even after the product is renamed, repriced or deactivated
// (CLAUDE.md section 6, ADR 0001).
public sealed class OrderItem
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid VariantId { get; private set; }
    public string ProductName { get; private set; }
    public string VariantName { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Subtotal { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // EF Core materialization only — real instances are created via Order.Place.
    private OrderItem()
    {
        ProductName = null!;
        VariantName = null!;
    }

    internal OrderItem(Guid orderId, OrderLine line, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        OrderId = orderId;
        VariantId = line.VariantId;
        ProductName = line.ProductName;
        VariantName = line.VariantName;
        Quantity = line.Quantity;
        UnitPrice = line.UnitPrice;
        Subtotal = line.UnitPrice * line.Quantity;
        CreatedAt = now;
    }
}
