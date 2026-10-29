using Mercato.Ordering.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Mercato.Ordering.Infrastructure.Payments;

// Stand-in for a real gateway (designed as if it were VNPay — CLAUDE.md 3.3). No network
// call, no real money. It approves everything except the well-known test token below,
// the same idea as a payment provider's sandbox test cards, so both outcomes can be
// demonstrated from Swagger and exercised in tests.
internal sealed class MockPaymentProvider(ILogger<MockPaymentProvider> logger) : IPaymentProvider
{
    public const string DeclineToken = "tok_decline";

    public string MethodCode => "mock_vnpay";

    public Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        // Derived from the attempt id, so charging the same attempt twice yields the
        // same provider reference — what an idempotency key buys you with a real provider.
        var providerTransactionId = $"MOCK-{request.AttemptId:N}";

        if (string.Equals(request.PaymentToken, DeclineToken, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Mock payment DECLINED for order {OrderId} ({Amount} VND).", request.OrderId, request.Amount);

            return Task.FromResult(new PaymentResult(false, providerTransactionId, "Payment was declined by the bank (mock)."));
        }

        logger.LogInformation("Mock payment APPROVED for order {OrderId} ({Amount} VND).", request.OrderId, request.Amount);

        return Task.FromResult(new PaymentResult(true, providerTransactionId, null));
    }
}
