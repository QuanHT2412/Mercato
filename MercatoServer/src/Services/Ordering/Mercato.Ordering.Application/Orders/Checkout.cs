using Mercato.Ordering.Application.Abstractions;
using Mercato.Ordering.Application.Exceptions;
using Mercato.Ordering.Domain.Orders;
using Microsoft.Extensions.Logging;

namespace Mercato.Ordering.Application.Orders;

public sealed record CheckoutCommand(Guid UserId, string? Receiver, string? Phone, string? Address, string? Note);

// The checkout flow. Two services are involved and there is no distributed transaction
// between them (ADR 0001), so the steps are ordered such that every failure either
// happens before anything was changed, or is undone by a compensating call:
//
//   1. validate locally (cart not empty, shipping details)   -> fails: nothing happened
//   2. reserve stock in Catalog (gRPC, all-or-nothing)        -> fails: nothing was held
//   3. create the order + empty the cart, one DB transaction  -> fails: RELEASE the hold
//
// After step 3 the order is AwaitingPayment and owns the hold until it is paid,
// cancelled or expired.
public sealed class CheckoutHandler(
    ICartRepository cartRepository,
    IOrderRepository orderRepository,
    IUnitOfWork unitOfWork,
    IStockGateway stockGateway,
    TimeProvider timeProvider,
    ILogger<CheckoutHandler> logger)
{
    // Slightly longer than the payment window: the hold is taken a moment before the
    // window starts, and the expiry job only runs once a minute. Without the margin a
    // payment in the window's last seconds could find its hold already lapsed.
    public static readonly TimeSpan StockHold = Order.PaymentWindow + TimeSpan.FromMinutes(2);

    public async Task<OrderResult> HandleAsync(CheckoutCommand command, CancellationToken cancellationToken)
    {
        var cart = await cartRepository.GetAsync(command.UserId, cancellationToken);

        if (cart is null || cart.IsEmpty)
        {
            throw new EmptyCartException();
        }

        // Before the remote call on purpose: a typo in the address must not cost a
        // reservation and a compensation.
        var shipping = ShippingDetails.Create(command.Receiver, command.Phone, command.Address, command.Note);

        var orderId = Order.NewId();
        var lines = cart.Items.Select(i => new StockLine(i.VariantId, i.Quantity)).ToList();

        var reservation = await stockGateway.ReserveAsync(orderId, lines, StockHold, cancellationToken);

        if (!reservation.Reserved)
        {
            throw new OutOfStockException(
                reservation.FailedVariantId,
                reservation.AvailableQuantity,
                variantMissing: reservation.Failure == StockFailure.VariantNotFound);
        }

        try
        {
            var now = timeProvider.GetUtcNow();

            // Prices and names come from what Catalog just reserved — never from the
            // client and never from the cart. This is the price snapshot.
            var order = Order.Place(
                orderId,
                command.UserId,
                shipping,
                [.. reservation.Lines.Select(l => new OrderLine(l.VariantId, l.ProductName, l.VariantName, l.UnitPrice, l.Quantity))],
                now);

            order.AwaitPayment(now);
            orderRepository.Add(order);
            cart.Clear(now);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return OrderResult.From(order);
        }
        catch (Exception exception)
        {
            // Compensation: stock is held in Catalog for an order that does not exist.
            logger.LogWarning(exception, "Checkout failed after stock was reserved for order {OrderId}; releasing the hold.", orderId);
            await ReleaseHoldAsync(orderId);

            throw;
        }
    }

    private async Task ReleaseHoldAsync(Guid orderId)
    {
        try
        {
            // Not the request's token: if the request was cancelled, that is exactly
            // when the compensation still has to go out.
            await stockGateway.ReleaseAsync(orderId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Even the compensation failed. Not fatal: the hold carries its own expiry
            // and stops counting against availability when it lapses.
            logger.LogError(exception, "Could not release the stock hold for order {OrderId}; it will lapse on its own.", orderId);
        }
    }
}
