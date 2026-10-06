using Grpc.Core;
using Mercato.Catalog.Application.Stock;
using Mercato.Catalog.Domain.Exceptions;
using Mercato.Catalog.Grpc;

namespace Mercato.Catalog.Api.Grpc;

// Thin transport adapter, same role as the REST endpoint classes: translate the wire
// message to a command, call the handler, translate the result back. No business logic.
public sealed class StockGrpcService(
    ReserveStockHandler reserveHandler,
    ConfirmStockHandler confirmHandler,
    ReleaseStockHandler releaseHandler) : StockService.StockServiceBase
{
    public override async Task<ReserveStockReply> CheckAndReserveStock(ReserveStockRequest request, ServerCallContext context)
    {
        var command = new ReserveStockCommand(
            ParseId(request.OrderId, "order_id"),
            [.. request.Lines.Select(l => new ReserveStockLine(ParseId(l.VariantId, "variant_id"), l.Quantity))],
            TimeSpan.FromSeconds(request.HoldSeconds));

        ReserveStockResult result;

        try
        {
            result = await reserveHandler.HandleAsync(command, context.CancellationToken);
        }
        catch (DomainValidationException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }

        var reply = new ReserveStockReply
        {
            Reserved = result.Reserved,
            FailureReason = result.Failure switch
            {
                ReserveStockFailure.VariantNotFound => ReserveFailureReason.VariantNotFound,
                ReserveStockFailure.InsufficientStock => ReserveFailureReason.InsufficientStock,
                _ => ReserveFailureReason.Unspecified,
            },
            FailedVariantId = result.FailedVariantId?.ToString() ?? string.Empty,
            AvailableQuantity = result.AvailableQuantity,
        };

        reply.Lines.AddRange(result.Lines.Select(l => new ReservedStockLine
        {
            VariantId = l.VariantId.ToString(),
            ProductName = l.ProductName,
            VariantName = l.VariantName,
            UnitPrice = decimal.ToInt64(l.UnitPrice),
            Quantity = l.Quantity,
        }));

        return reply;
    }

    public override async Task<ReservationReply> ConfirmReservation(ReservationRequest request, ServerCallContext context)
    {
        try
        {
            var found = await confirmHandler.HandleAsync(ParseId(request.OrderId, "order_id"), context.CancellationToken);

            return new ReservationReply { Found = found };
        }
        catch (InsufficientStockException exception)
        {
            // The hold expired and its units were sold to someone else before the
            // confirmation arrived. Stock is never driven negative; the caller decides
            // what to do with a paid order that can no longer be fulfilled.
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<ReservationReply> ReleaseReservation(ReservationRequest request, ServerCallContext context)
    {
        var found = await releaseHandler.HandleAsync(ParseId(request.OrderId, "order_id"), context.CancellationToken);

        return new ReservationReply { Found = found };
    }

    private static Guid ParseId(string value, string field) =>
        Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id
            : throw new RpcException(new Status(StatusCode.InvalidArgument, $"'{field}' must be a non-empty UUID."));
}
