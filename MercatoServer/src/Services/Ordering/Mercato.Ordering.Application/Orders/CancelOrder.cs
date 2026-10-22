using Mercato.Ordering.Application.Abstractions;
using Mercato.Ordering.Application.Exceptions;
using Microsoft.Extensions.Logging;

namespace Mercato.Ordering.Application.Orders;

public sealed class CancelOrderHandler(
    IOrderRepository orderRepository,
    IUnitOfWork unitOfWork,
    IStockGateway stockGateway,
    TimeProvider timeProvider,
    ILogger<CancelOrderHandler> logger)
{
    public async Task<OrderResult> HandleAsync(Guid userId, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(orderId, cancellationToken);

        if (order is null || order.UserId != userId)
        {
            throw new OrderNotFoundException(orderId);
        }

        order.Cancel(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Compensation for the reservation made at checkout. The order is already
        // Cancelled whatever happens here; a failed release only means the units stay
        // held until the hold's own expiry.
        try
        {
            await stockGateway.ReleaseAsync(order.Id, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Order {OrderId} was cancelled but its stock hold could not be released; it will lapse on its own.", order.Id);
        }

        return OrderResult.From(order);
    }
}
