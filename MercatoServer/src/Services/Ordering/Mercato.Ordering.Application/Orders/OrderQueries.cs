using Mercato.Ordering.Application.Abstractions;

namespace Mercato.Ordering.Application.Orders;

public sealed class GetOrderHandler(IOrderRepository orderRepository)
{
    // Null for "no such order" AND for "someone else's order" — see OrderNotFoundException.
    public async Task<OrderResult?> HandleAsync(Guid userId, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(orderId, cancellationToken);

        return order is null || order.UserId != userId ? null : OrderResult.From(order);
    }
}

public sealed class ListOrdersHandler(IOrderRepository orderRepository)
{
    public async Task<IReadOnlyList<OrderSummary>> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var orders = await orderRepository.ListForUserAsync(userId, cancellationToken);

        return [.. orders.Select(OrderSummary.From)];
    }
}
