using Mercato.Catalog.Application.Abstractions;
using Mercato.Catalog.Domain.Exceptions;
using Mercato.Catalog.Domain.Stock;

namespace Mercato.Catalog.Application.Stock;

public sealed record ReserveStockLine(Guid VariantId, int Quantity);

public sealed record ReserveStockCommand(Guid OrderId, IReadOnlyList<ReserveStockLine> Lines, TimeSpan HoldFor);

public enum ReserveStockFailure
{
    None = 0,
    VariantNotFound = 1,
    InsufficientStock = 2,
}

// What Ordering snapshots onto the order: names and the unit price AT THIS MOMENT.
public sealed record ReservedLine(Guid VariantId, string ProductName, string VariantName, decimal UnitPrice, int Quantity);

// "Out of stock" is an expected business outcome of a checkout, not an exceptional one,
// so it is a result value rather than an exception.
public sealed record ReserveStockResult(
    bool Reserved,
    IReadOnlyList<ReservedLine> Lines,
    ReserveStockFailure Failure,
    Guid? FailedVariantId,
    int AvailableQuantity)
{
    public static ReserveStockResult Success(IReadOnlyList<ReservedLine> lines) =>
        new(true, lines, ReserveStockFailure.None, null, 0);

    public static ReserveStockResult Failed(ReserveStockFailure failure, Guid variantId, int availableQuantity) =>
        new(false, [], failure, variantId, availableQuantity);
}

public sealed class ReserveStockHandler(IStockStore stockStore, TimeProvider timeProvider)
{
    public static readonly TimeSpan MinHold = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxHold = TimeSpan.FromHours(1);

    public Task<ReserveStockResult> HandleAsync(ReserveStockCommand command, CancellationToken cancellationToken)
    {
        if (command.OrderId == Guid.Empty)
        {
            throw new DomainValidationException("Order id must not be empty.");
        }

        if (command.Lines is null || command.Lines.Count == 0)
        {
            throw new DomainValidationException("At least one line is required to reserve stock.");
        }

        if (command.Lines.Any(l => l.Quantity <= 0))
        {
            throw new DomainValidationException("Reserved quantity must be positive.");
        }

        // The same variant twice in one request is one demand on that variant.
        var wanted = command.Lines
            .GroupBy(l => l.VariantId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var holdFor = TimeSpan.FromTicks(Math.Clamp(command.HoldFor.Ticks, MinHold.Ticks, MaxHold.Ticks));

        // All-or-nothing: either every line of the order is held or none is.
        return stockStore.ExecuteInTransactionAsync(
            ct => ReserveAsync(command.OrderId, wanted, holdFor, ct),
            cancellationToken);
    }

    private async Task<ReserveStockResult> ReserveAsync(
        Guid orderId, Dictionary<Guid, int> wanted, TimeSpan holdFor, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // Idempotent: a retried call for an order that already holds stock (the caller
        // timed out and tried again) answers with the existing hold, it does not hold twice.
        var existing = await stockStore.GetReservationsAsync(orderId, cancellationToken);

        if (existing.Count > 0)
        {
            wanted = existing.ToDictionary(r => r.VariantId, r => r.Quantity);
        }

        var locked = await stockStore.LockVariantsAsync(wanted.Keys, cancellationToken);
        var byId = locked.ToDictionary(l => l.Variant.Id);

        var lines = new List<ReservedLine>(wanted.Count);

        foreach (var (variantId, quantity) in wanted)
        {
            if (!byId.TryGetValue(variantId, out var item))
            {
                return ReserveStockResult.Failed(ReserveStockFailure.VariantNotFound, variantId, 0);
            }

            lines.Add(new ReservedLine(variantId, item.ProductName, item.Variant.Name, item.Variant.Price, quantity));
        }

        if (existing.Count > 0)
        {
            return ReserveStockResult.Success(lines);
        }

        var reserved = await stockStore.GetReservedQuantitiesAsync(wanted.Keys, now, cancellationToken);
        var reservations = new List<StockReservation>(wanted.Count);

        foreach (var (variantId, quantity) in wanted)
        {
            var item = byId[variantId];
            var alreadyReserved = reserved.GetValueOrDefault(variantId);

            // A deactivated product takes all its variants off sale.
            if (!item.ProductIsActive)
            {
                return ReserveStockResult.Failed(ReserveStockFailure.InsufficientStock, variantId, 0);
            }

            try
            {
                reservations.Add(item.Variant.Reserve(orderId, quantity, alreadyReserved, now + holdFor, now));
            }
            catch (InsufficientStockException exception)
            {
                return ReserveStockResult.Failed(ReserveStockFailure.InsufficientStock, variantId, exception.Available);
            }
        }

        stockStore.AddReservations(reservations);
        await stockStore.SaveChangesAsync(cancellationToken);

        return ReserveStockResult.Success(lines);
    }
}
