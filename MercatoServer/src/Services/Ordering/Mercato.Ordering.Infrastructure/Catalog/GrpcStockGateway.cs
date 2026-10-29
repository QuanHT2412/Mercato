using Grpc.Core;
using Mercato.Ordering.Application.Abstractions;
using Mercato.Ordering.Application.Exceptions;
using Proto = Mercato.Catalog.Grpc;

namespace Mercato.Ordering.Infrastructure.Catalog;

// The one synchronous call between services (CLAUDE.md section 5): Ordering -> Catalog
// over gRPC. Everything gRPC-specific stops here; the Application layer only sees
// IStockGateway and its own records.
internal sealed class GrpcStockGateway(Proto.StockService.StockServiceClient client) : IStockGateway
{
    public async Task<StockReservationResult> ReserveAsync(
        Guid orderId, IReadOnlyList<StockLine> lines, TimeSpan holdFor, CancellationToken cancellationToken)
    {
        var request = new Proto.ReserveStockRequest
        {
            OrderId = orderId.ToString(),
            HoldSeconds = (int)holdFor.TotalSeconds,
        };
        request.Lines.AddRange(lines.Select(l => new Proto.StockLine { VariantId = l.VariantId.ToString(), Quantity = l.Quantity }));

        var reply = await CallAsync(() => client.CheckAndReserveStockAsync(request, cancellationToken: cancellationToken).ResponseAsync);

        return new StockReservationResult(
            reply.Reserved,
            [.. reply.Lines.Select(l => new ReservedLine(Guid.Parse(l.VariantId), l.ProductName, l.VariantName, l.UnitPrice, l.Quantity))],
            reply.FailureReason switch
            {
                Proto.ReserveFailureReason.VariantNotFound => StockFailure.VariantNotFound,
                Proto.ReserveFailureReason.InsufficientStock => StockFailure.InsufficientStock,
                // A failure we do not recognise (a newer Catalog) is still a failure.
                _ => reply.Reserved ? StockFailure.None : StockFailure.InsufficientStock,
            },
            Guid.TryParse(reply.FailedVariantId, out var failedVariantId) ? failedVariantId : null,
            reply.AvailableQuantity);
    }

    public Task ConfirmAsync(Guid orderId, CancellationToken cancellationToken) =>
        CallAsync(() => client.ConfirmReservationAsync(
            new Proto.ReservationRequest { OrderId = orderId.ToString() }, cancellationToken: cancellationToken).ResponseAsync);

    public Task ReleaseAsync(Guid orderId, CancellationToken cancellationToken) =>
        CallAsync(() => client.ReleaseReservationAsync(
            new Proto.ReservationRequest { OrderId = orderId.ToString() }, cancellationToken: cancellationToken).ResponseAsync);

    private static async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (RpcException exception) when (exception.StatusCode is not StatusCode.Cancelled)
        {
            // Translate the transport error into one of ours so nothing above this class
            // needs to know the call was gRPC.
            throw new StockServiceException($"Catalog stock call failed: {exception.Status.StatusCode} — {exception.Status.Detail}", exception);
        }
    }
}
