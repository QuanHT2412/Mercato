namespace Mercato.Ordering.Application.Abstractions;

public sealed record StockLine(Guid VariantId, int Quantity);

// Catalog's description of a held line at the moment of checkout — the source of the
// price/name snapshot on the order.
public sealed record ReservedLine(Guid VariantId, string ProductName, string VariantName, decimal UnitPrice, int Quantity);

public enum StockFailure
{
    None = 0,
    VariantNotFound = 1,
    InsufficientStock = 2,
}

public sealed record StockReservationResult(
    bool Reserved,
    IReadOnlyList<ReservedLine> Lines,
    StockFailure Failure,
    Guid? FailedVariantId,
    int AvailableQuantity);

// Ordering's view of Catalog's stock (the gRPC call sits behind this port). All three
// operations are idempotent on the Catalog side, keyed by order id, so retrying any of
// them is always safe.
public interface IStockGateway
{
    // All-or-nothing: either every line is held for `holdFor`, or nothing is.
    Task<StockReservationResult> ReserveAsync(
        Guid orderId, IReadOnlyList<StockLine> lines, TimeSpan holdFor, CancellationToken cancellationToken);

    // The order was paid: the held units leave the shelf for good.
    Task ConfirmAsync(Guid orderId, CancellationToken cancellationToken);

    // The order was cancelled/expired (or never got created): give the units back.
    Task ReleaseAsync(Guid orderId, CancellationToken cancellationToken);
}
