using Mercato.Ordering.Domain.Exceptions;
using Mercato.Ordering.Domain.Payments;

namespace Ordering.Tests.Domain;

public class PaymentTransactionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Start_CreatesAPendingAttempt()
    {
        var orderId = Guid.CreateVersion7();

        var transaction = PaymentTransaction.Start(orderId, "mock_vnpay", 848_000m, Now);

        Assert.Equal(orderId, transaction.OrderId);
        Assert.Equal(PaymentStatus.Pending, transaction.Status);
        Assert.Equal(848_000m, transaction.Amount);
        Assert.Null(transaction.PaidAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1000)]
    public void Start_WithANonPositiveAmount_Throws(int amount)
    {
        Assert.Throws<DomainValidationException>(() =>
            PaymentTransaction.Start(Guid.CreateVersion7(), "mock_vnpay", amount, Now));
    }

    [Fact]
    public void MarkSucceeded_RecordsTheProviderReferenceAndPaymentTime()
    {
        var transaction = PaymentTransaction.Start(Guid.CreateVersion7(), "mock_vnpay", 848_000m, Now);

        transaction.MarkSucceeded("MOCK-123", Now.AddSeconds(3));

        Assert.Equal(PaymentStatus.Succeeded, transaction.Status);
        Assert.Equal("MOCK-123", transaction.ProviderTransactionId);
        Assert.Equal(Now.AddSeconds(3), transaction.PaidAt);
    }

    [Fact]
    public void MarkFailed_KeepsTheReason_AndLeavesPaidAtEmpty()
    {
        var transaction = PaymentTransaction.Start(Guid.CreateVersion7(), "mock_vnpay", 848_000m, Now);

        transaction.MarkFailed("MOCK-456", "Card declined", Now.AddSeconds(3));

        Assert.Equal(PaymentStatus.Failed, transaction.Status);
        Assert.Equal("Card declined", transaction.FailureReason);
        Assert.Null(transaction.PaidAt);
    }

    [Fact]
    public void ACompletedAttempt_CannotBeCompletedAgain()
    {
        var transaction = PaymentTransaction.Start(Guid.CreateVersion7(), "mock_vnpay", 848_000m, Now);
        transaction.MarkFailed(null, "Card declined", Now);

        Assert.Throws<DomainValidationException>(() => transaction.MarkSucceeded("MOCK-789", Now));
    }
}
