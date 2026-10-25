using Mercato.Ordering.Application.Abstractions;
using Mercato.Ordering.Application.Exceptions;
using Mercato.Ordering.Domain.Carts;
using Mercato.Ordering.Domain.Orders;
using Mercato.Ordering.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Mercato.Ordering.Infrastructure.Persistence;

internal sealed class CartRepository(OrderingDbContext dbContext) : ICartRepository
{
    public Task<Cart?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Carts.Include(c => c.Items).SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken);

    public void Add(Cart cart) => dbContext.Carts.Add(cart);
}

internal sealed class OrderRepository(OrderingDbContext dbContext) : IOrderRepository
{
    private const int MaxListedOrders = 100;

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Orders.Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Order>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(MaxListedOrders)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListOverdueIdsAsync(DateTimeOffset now, int take, CancellationToken cancellationToken) =>
        // Shaped to hit idx_orders_expiry_scan (partial on status = 'AwaitingPayment').
        await dbContext.Orders
            .Where(o => o.Status == OrderStatus.AwaitingPayment && o.PaymentDeadline <= now)
            .OrderBy(o => o.PaymentDeadline)
            .Take(take)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

    public void Add(Order order) => dbContext.Orders.Add(order);

    public void AddPayment(PaymentTransaction transaction) => dbContext.PaymentTransactions.Add(transaction);
}

// The repositories above share one scoped DbContext, so a single SaveChangesAsync here
// commits everything they staged in one database transaction.
internal sealed class UnitOfWork(OrderingDbContext dbContext) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // An order's xmin moved since it was read.
            throw new ConcurrencyConflictException();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two requests inserted the same thing at once (the same user's first cart,
            // the same variant into one cart). Same remedy for the caller: reload, retry.
            throw new ConcurrencyConflictException();
        }
    }
}
