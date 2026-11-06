using Hangfire;
using Mercato.Ordering.Application.Abstractions;
using Mercato.Ordering.Application.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mercato.Ordering.Infrastructure.Jobs;

// Recurring Hangfire job (every minute): finds orders whose payment window has closed
// and expires them, which releases their reserved stock.
//
// A periodic SCAN rather than one delayed job scheduled per order at checkout: the scan
// is driven by the orders table itself, so there is no second piece of state ("the
// scheduled job") that can be lost or get out of sync with the order — if a run is
// missed, the next one simply picks the same orders up.
public sealed class ExpireOverdueOrdersJob(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ExpireOverdueOrdersJob> logger)
{
    public const string JobId = "expire-overdue-orders";

    private const int BatchSize = 100;

    // A slow run must not overlap the next tick and process the same orders twice.
    [DisableConcurrentExecution(timeoutInSeconds: 30)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> overdueIds;

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            overdueIds = await scope.ServiceProvider.GetRequiredService<IOrderRepository>()
                .ListOverdueIdsAsync(timeProvider.GetUtcNow(), BatchSize, cancellationToken);
        }

        var expired = 0;

        foreach (var orderId in overdueIds)
        {
            // One scope (= one DbContext) per order: an order that fails leaves nothing
            // half-tracked behind to break the ones after it.
            await using var scope = scopeFactory.CreateAsyncScope();

            try
            {
                if (await scope.ServiceProvider.GetRequiredService<ExpireOrderHandler>().HandleAsync(orderId, cancellationToken))
                {
                    expired++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Failed to expire order {OrderId}; it will be retried on the next run.", orderId);
            }
        }

        if (expired > 0)
        {
            logger.LogInformation("Expired {ExpiredCount} unpaid order(s).", expired);
        }
    }
}
