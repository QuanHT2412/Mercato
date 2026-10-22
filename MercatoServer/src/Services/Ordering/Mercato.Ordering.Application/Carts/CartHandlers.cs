using Mercato.Ordering.Application.Abstractions;
using Mercato.Ordering.Domain.Carts;

namespace Mercato.Ordering.Application.Carts;

public sealed record CartItemResult(Guid VariantId, int Quantity);

// Variant ids and quantities only. Names and prices live in Catalog: the client shows
// them from there, and the authoritative ones are fixed at checkout.
public sealed record CartResult(IReadOnlyList<CartItemResult> Items, int TotalQuantity)
{
    public static readonly CartResult Empty = new([], 0);

    public static CartResult From(Cart cart)
    {
        var items = cart.Items
            .OrderBy(i => i.CreatedAt)
            .ThenBy(i => i.VariantId)
            .Select(i => new CartItemResult(i.VariantId, i.Quantity))
            .ToList();

        return new CartResult(items, items.Sum(i => i.Quantity));
    }
}

public sealed class GetCartHandler(ICartRepository cartRepository)
{
    public async Task<CartResult> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cart = await cartRepository.GetAsync(userId, cancellationToken);

        return cart is null ? CartResult.Empty : CartResult.From(cart);
    }
}

public sealed record SetCartItemCommand(Guid UserId, Guid VariantId, int Quantity);

public sealed class SetCartItemHandler(ICartRepository cartRepository, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    public async Task<CartResult> HandleAsync(SetCartItemCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var cart = await cartRepository.GetAsync(command.UserId, cancellationToken);

        // The cart row is created lazily, the first time something goes into it.
        if (cart is null)
        {
            cart = Cart.CreateFor(command.UserId, now);
            cartRepository.Add(cart);
        }

        cart.SetItem(command.VariantId, command.Quantity, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return CartResult.From(cart);
    }
}

public sealed class RemoveCartItemHandler(ICartRepository cartRepository, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    // Removing something that is not in the cart is not an error — the end state the
    // caller asked for ("this item is not in my cart") already holds.
    public async Task<CartResult> HandleAsync(Guid userId, Guid variantId, CancellationToken cancellationToken)
    {
        var cart = await cartRepository.GetAsync(userId, cancellationToken);

        if (cart is null)
        {
            return CartResult.Empty;
        }

        if (cart.RemoveItem(variantId, timeProvider.GetUtcNow()))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return CartResult.From(cart);
    }
}

public sealed class ClearCartHandler(ICartRepository cartRepository, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    public async Task HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cart = await cartRepository.GetAsync(userId, cancellationToken);

        if (cart is null || cart.IsEmpty)
        {
            return;
        }

        cart.Clear(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
