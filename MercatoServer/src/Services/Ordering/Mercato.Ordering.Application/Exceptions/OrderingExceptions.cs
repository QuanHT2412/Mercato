namespace Mercato.Ordering.Application.Exceptions;

// Also thrown when the order exists but belongs to someone else: from the caller's side
// that is indistinguishable from "no such order", so order ids cannot be probed.
public sealed class OrderNotFoundException : Exception
{
    public OrderNotFoundException(Guid orderId) : base($"Order '{orderId}' was not found.")
    {
    }
}

public sealed class EmptyCartException : Exception
{
    public EmptyCartException() : base("The cart is empty.")
    {
    }
}

// Checkout could not hold the stock. Nothing was reserved (reserve is all-or-nothing in
// Catalog), no order was created and the cart is left as it was.
public sealed class OutOfStockException : Exception
{
    public OutOfStockException(Guid? variantId, int availableQuantity, bool variantMissing)
        : base(variantMissing
            ? $"Item '{variantId}' is no longer available."
            : $"Not enough stock for item '{variantId}': {availableQuantity} left.")
    {
        VariantId = variantId;
        AvailableQuantity = availableQuantity;
    }

    public Guid? VariantId { get; }

    public int AvailableQuantity { get; }
}

// Someone else changed the same row between our read and our write (the order was paid
// while it was being cancelled, expired while it was being paid, ...). The caller should
// reload and decide again.
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException() : base("The resource was modified concurrently. Reload and try again.")
    {
    }
}
