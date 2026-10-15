using Mercato.Ordering.Domain.Exceptions;

namespace Mercato.Ordering.Domain.Payments;

public enum PaymentStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
}

// One attempt to pay one order. Written BEFORE the provider is called (as Pending) and
// completed afterwards, so that an attempt which crashes midway still leaves a trace to
// reconcile against the provider.
public sealed class PaymentTransaction
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public string MethodCode { get; private set; }
    public PaymentStatus Status { get; private set; }
    public decimal Amount { get; private set; }
    public string? ProviderTransactionId { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // EF Core materialization only — real instances are created via Start.
    private PaymentTransaction()
    {
        MethodCode = null!;
    }

    private PaymentTransaction(Guid orderId, string methodCode, decimal amount, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        OrderId = orderId;
        MethodCode = methodCode;
        Amount = amount;
        Status = PaymentStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static PaymentTransaction Start(Guid orderId, string methodCode, decimal amount, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(methodCode))
        {
            throw new DomainValidationException("Payment method must not be empty.");
        }

        if (amount <= 0)
        {
            throw new DomainValidationException("Payment amount must be positive.");
        }

        return new PaymentTransaction(orderId, methodCode.Trim(), amount, now);
    }

    public void MarkSucceeded(string providerTransactionId, DateTimeOffset now)
    {
        EnsurePending();

        Status = PaymentStatus.Succeeded;
        ProviderTransactionId = providerTransactionId;
        PaidAt = now;
        UpdatedAt = now;
    }

    public void MarkFailed(string? providerTransactionId, string reason, DateTimeOffset now)
    {
        EnsurePending();

        Status = PaymentStatus.Failed;
        ProviderTransactionId = providerTransactionId;
        FailureReason = reason;
        UpdatedAt = now;
    }

    private void EnsurePending()
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new DomainValidationException($"Payment transaction '{Id}' is already {Status}.");
        }
    }
}
