namespace Mercato.Catalog.Domain.Exceptions;

// Not a validation error: the request was well-formed, the shelf just does not hold
// enough. Carries the numbers so the caller can tell the customer what is left.
public sealed class InsufficientStockException : Exception
{
    public InsufficientStockException(Guid variantId, int requested, int available)
        : base($"Variant '{variantId}' has {available} available, {requested} requested.")
    {
        VariantId = variantId;
        Requested = requested;
        Available = available;
    }

    public Guid VariantId { get; }

    public int Requested { get; }

    public int Available { get; }
}
