using Mercato.Ordering.Domain.Orders;

namespace Mercato.Ordering.Domain.Exceptions;

// The request breaks a rule about the data itself (quantity out of range, empty address).
public sealed class DomainValidationException : Exception
{
    public DomainValidationException(string message) : base(message)
    {
    }
}

// The request is well-formed but not allowed in the order's current state (paying a
// cancelled order, cancelling a confirmed one). Kept apart from validation errors because
// the caller reacts differently: this one is a conflict with state, not bad input.
public sealed class InvalidOrderTransitionException : Exception
{
    public InvalidOrderTransitionException(Guid orderId, OrderStatus status, string action)
        : base($"Order '{orderId}' is {status} and cannot be {action}.")
    {
        OrderId = orderId;
        Status = status;
    }

    public Guid OrderId { get; }

    public OrderStatus Status { get; }
}
