using Mercato.Ordering.Domain.Carts;
using Mercato.Ordering.Domain.Orders;
using Mercato.Ordering.Domain.Payments;

namespace Mercato.Ordering.Application.Abstractions;

public interface ICartRepository
{
    // Tracked, with its items. Null when the user has never put anything in a cart.
    Task<Cart?> GetAsync(Guid userId, CancellationToken cancellationToken);

    void Add(Cart cart);
}

public interface IOrderRepository
{
    // Tracked, with its items.
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    // Untracked, newest first.
    Task<IReadOnlyList<Order>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);

    // Orders still AwaitingPayment whose deadline has passed — the expiry job's work list.
    Task<IReadOnlyList<Guid>> ListOverdueIdsAsync(DateTimeOffset now, int take, CancellationToken cancellationToken);

    void Add(Order order);

    void AddPayment(PaymentTransaction transaction);
}

// Repositories only stage changes; this commits them. Checkout relies on it to write the
// new order and the emptied cart in ONE database transaction.
public interface IUnitOfWork
{
    // Throws ConcurrencyConflictException when a row changed underneath us.
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
