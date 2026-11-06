using Mercato.Ordering.Application.Abstractions;
using Mercato.Ordering.Application.Exceptions;
using Microsoft.Extensions.Logging;

namespace Mercato.Ordering.Application.Orders;

// Expires ONE overdue order. The background job finds the candidates and calls this once
// per order; keeping the decision here (not in the job) means the rule is unit-testable
// and does not depend on Hangfire.
public sealed class ExpireOrderHandler(
    IOrderRepository orderRepository,
    IUnitOfWork unitOfWork,
    IStockGateway stockGateway,
    TimeProvider timeProvider,
    ILogger<ExpireOrderHandler> logger)
{
    // True when this call expired the order. False when there was nothing to do — the
    // order was paid or cancelled between being listed and being processed.
    public async Task<bool> HandleAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(orderId, cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (order is null || !order.IsPaymentOverdue(now))
        {
            return false;
        }

        order.Expire(now);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // The customer's payment/cancel committed in the same instant. Theirs wins —
            // the concurrency token is what stops "Expired" overwriting "Confirmed".
            return false;
        }

        // Compensation for the checkout reservation, same as on cancel.
        try
        {
            await stockGateway.ReleaseAsync(order.Id, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Order {OrderId} expired but its stock hold could not be released; it will lapse on its own.", order.Id);
        }

        return true;
    }
}
