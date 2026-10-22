namespace Mercato.Ordering.Application.Abstractions;

// AttemptId is our PaymentTransaction id and doubles as the idempotency key a real
// provider would be given, so a retried call cannot charge the customer twice.
public sealed record PaymentRequest(Guid OrderId, Guid AttemptId, decimal Amount, string? PaymentToken);

// A declined payment is a normal outcome (Succeeded = false), not an exception.
// Exceptions are for "the provider could not be reached / answered nonsense".
public sealed record PaymentResult(bool Succeeded, string? ProviderTransactionId, string? FailureReason);

// The seam where a real gateway (VNPay) would plug in. v1 ships only a mock
// implementation — no real payment integration (CLAUDE.md 3.3).
public interface IPaymentProvider
{
    string MethodCode { get; }

    Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken cancellationToken);
}
