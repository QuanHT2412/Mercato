using Mercato.Ordering.Domain.Orders;

namespace Mercato.Ordering.Application.Orders;

public sealed record OrderItemResult(
    Guid VariantId,
    string ProductName,
    string VariantName,
    int Quantity,
    decimal UnitPrice,
    decimal Subtotal);

// Status travels as its name ("AwaitingPayment"), not a number: the client switches on it
// to show the order's progress, and a name survives reordering the enum.
public sealed record OrderResult(
    Guid Id,
    string Status,
    decimal TotalAmount,
    string Receiver,
    string Phone,
    string Address,
    string? Note,
    DateTimeOffset? PaymentDeadline,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OrderItemResult> Items)
{
    public static OrderResult From(Order order) => new(
        order.Id,
        order.Status.ToString(),
        order.TotalAmount,
        order.Receiver,
        order.Phone,
        order.Address,
        order.Note,
        order.PaymentDeadline,
        order.CreatedAt,
        order.UpdatedAt,
        [.. order.Items
            .OrderBy(i => i.ProductName, StringComparer.Ordinal)
            .ThenBy(i => i.VariantName, StringComparer.Ordinal)
            .Select(i => new OrderItemResult(i.VariantId, i.ProductName, i.VariantName, i.Quantity, i.UnitPrice, i.Subtotal))]);
}

public sealed record OrderSummary(
    Guid Id,
    string Status,
    decimal TotalAmount,
    int ItemCount,
    DateTimeOffset? PaymentDeadline,
    DateTimeOffset CreatedAt)
{
    public static OrderSummary From(Order order) => new(
        order.Id,
        order.Status.ToString(),
        order.TotalAmount,
        order.Items.Sum(i => i.Quantity),
        order.PaymentDeadline,
        order.CreatedAt);
}
