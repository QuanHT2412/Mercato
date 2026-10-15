using Mercato.Ordering.Domain.Exceptions;

namespace Mercato.Ordering.Domain.Carts;

// Aggregate root: one cart per user, keyed by the user id. The cart only knows variant
// ids and quantities — names, prices and stock belong to Catalog and are looked up (and
// snapshotted) at checkout, never stored here.
public sealed class Cart
{
    public const int MaxLines = 20;
    public const int MinQuantity = 1;
    public const int MaxQuantity = 10;

    private readonly List<CartItem> _items = [];

    public Guid UserId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<CartItem> Items => _items;

    public bool IsEmpty => _items.Count == 0;

    // EF Core materialization only — real instances are created via CreateFor.
    private Cart()
    {
    }

    private Cart(Guid userId, DateTimeOffset now)
    {
        UserId = userId;
        UpdatedAt = now;
    }

    public static Cart CreateFor(Guid userId, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainValidationException("A cart must belong to a user.");
        }

        return new Cart(userId, now);
    }

    // "Set", not "add": the quantity given is the quantity the line ends up with. That
    // makes the operation idempotent — a retried request cannot double the quantity.
    public CartItem SetItem(Guid variantId, int quantity, DateTimeOffset now)
    {
        if (variantId == Guid.Empty)
        {
            throw new DomainValidationException("Variant id must not be empty.");
        }

        if (quantity is < MinQuantity or > MaxQuantity)
        {
            throw new DomainValidationException($"Quantity per line must be between {MinQuantity} and {MaxQuantity}.");
        }

        var item = _items.FirstOrDefault(i => i.VariantId == variantId);

        if (item is not null)
        {
            item.ChangeQuantity(quantity, now);
        }
        else
        {
            if (_items.Count >= MaxLines)
            {
                throw new DomainValidationException($"A cart can hold at most {MaxLines} different items.");
            }

            item = new CartItem(UserId, variantId, quantity, now);
            _items.Add(item);
        }

        UpdatedAt = now;

        return item;
    }

    public bool RemoveItem(Guid variantId, DateTimeOffset now)
    {
        var removed = _items.RemoveAll(i => i.VariantId == variantId) > 0;

        if (removed)
        {
            UpdatedAt = now;
        }

        return removed;
    }

    public void Clear(DateTimeOffset now)
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        UpdatedAt = now;
    }
}
