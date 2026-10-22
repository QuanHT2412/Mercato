using Mercato.Ordering.Application.Abstractions;
using Mercato.Ordering.Application.Exceptions;
using Mercato.Ordering.Domain.Payments;
using Microsoft.Extensions.Logging;

namespace Mercato.Ordering.Application.Orders;

public sealed record PayOrderCommand(Guid UserId, Guid OrderId, string? PaymentToken);

// Paid = false is a declined payment: the order is untouched (still AwaitingPayment) and
// the customer may try again until the deadline.
public sealed record PayOrderResult(bool Paid, string? FailureReason, OrderResult Order);

public sealed class PayOrderHandler(
    IOrderRepository orderRepository,
    IUnitOfWork unitOfWork,
    IPaymentProvider paymentProvider,
    IStockGateway stockGateway,
    TimeProvider timeProvider,
    ILogger<PayOrderHandler> logger)
{
    public async Task<PayOrderResult> HandleAsync(PayOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(command.OrderId, cancellationToken);

        if (order is null || order.UserId != command.UserId)
        {
            throw new OrderNotFoundException(command.OrderId);
        }

        // Refuse BEFORE any money moves: wrong state, or the payment window has closed.
        order.EnsurePayable(timeProvider.GetUtcNow());

        // The attempt is on record (Pending) before the provider is called, so a crash
        // between "charged" and "saved" leaves something to reconcile.
        var attempt = PaymentTransaction.Start(order.Id, paymentProvider.MethodCode, order.TotalAmount, timeProvider.GetUtcNow());
        orderRepository.AddPayment(attempt);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var charge = await paymentProvider.ChargeAsync(
            new PaymentRequest(order.Id, attempt.Id, order.TotalAmount, command.PaymentToken),
            cancellationToken);

        var now = timeProvider.GetUtcNow();

        if (!charge.Succeeded)
        {
            var reason = charge.FailureReason ?? "Payment was declined.";
            attempt.MarkFailed(charge.ProviderTransactionId, reason, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new PayOrderResult(false, reason, OrderResult.From(order));
        }

        attempt.MarkSucceeded(charge.ProviderTransactionId ?? attempt.Id.ToString(), now);

        try
        {
            order.Confirm(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // Known gap in v1: the charge went through but the order could not be
            // confirmed (it expired/was cancelled during the charge, or the save failed).
            // Refunds are out of scope, so this is logged for manual follow-up rather
            // than compensated automatically.
            logger.LogCritical(
                exception,
                "Payment {PaymentAttemptId} ({ProviderTransactionId}) was captured but order {OrderId} could not be confirmed.",
                attempt.Id, charge.ProviderTransactionId, order.Id);

            throw;
        }

        // Tell Catalog to turn the hold into a real stock decrement. Order state and
        // stock live in two databases, so this cannot be atomic with the save above: if
        // it fails the order is Confirmed while the units are still only held. The call
        // is idempotent and safe to retry; making it reliable is what the outbox +
        // OrderConfirmed event are for (M5).
        try
        {
            await stockGateway.ConfirmAsync(order.Id, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Order {OrderId} is confirmed but its stock hold could not be committed in Catalog.", order.Id);
        }

        return new PayOrderResult(true, null, OrderResult.From(order));
    }
}
