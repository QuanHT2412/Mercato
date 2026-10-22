using Mercato.Ordering.Application.Abstractions;
using Mercato.Ordering.Application.Exceptions;
using Mercato.Ordering.Application.Orders;
using Mercato.Ordering.Domain.Exceptions;
using Mercato.Ordering.Domain.Orders;
using Mercato.Ordering.Domain.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ordering.Tests.Application;

public class PayAndCancelHandlerTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IPaymentProvider _payments = Substitute.For<IPaymentProvider>();
    private readonly IStockGateway _stock = Substitute.For<IStockGateway>();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero));
    private readonly List<PaymentTransaction> _attempts = [];
    private readonly PayOrderHandler _pay;
    private readonly CancelOrderHandler _cancel;

    public PayAndCancelHandlerTests()
    {
        _payments.MethodCode.Returns("mock_vnpay");
        _orders.AddPayment(Arg.Do<PaymentTransaction>(_attempts.Add));

        _pay = new PayOrderHandler(_orders, _unitOfWork, _payments, _stock, _timeProvider, NullLogger<PayOrderHandler>.Instance);
        _cancel = new CancelOrderHandler(_orders, _unitOfWork, _stock, _timeProvider, NullLogger<CancelOrderHandler>.Instance);
    }

    private Order GivenOrderAwaitingPayment(Guid? userId = null)
    {
        var now = _timeProvider.GetUtcNow();
        var order = Order.Place(
            Order.NewId(),
            userId ?? UserId,
            ShippingDetails.Create("Nguyen Van A", "0901234567", "12 Lê Lợi", null),
            [new OrderLine(Guid.CreateVersion7(), "Áo thun", "Trắng / M", 199_000m, 2)],
            now);
        order.AwaitPayment(now);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        return order;
    }

    private void GivenTheProviderAnswers(PaymentResult result) =>
        _payments.ChargeAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>()).Returns(result);

    [Fact]
    public async Task Pay_Approved_ConfirmsTheOrder_RecordsTheAttempt_AndCommitsTheStockHold()
    {
        var order = GivenOrderAwaitingPayment();
        GivenTheProviderAnswers(new PaymentResult(true, "MOCK-1", null));

        var result = await _pay.HandleAsync(new PayOrderCommand(UserId, order.Id, null), CancellationToken.None);

        Assert.True(result.Paid);
        Assert.Equal("Confirmed", result.Order.Status);

        var attempt = Assert.Single(_attempts);
        Assert.Equal(PaymentStatus.Succeeded, attempt.Status);
        Assert.Equal(398_000m, attempt.Amount);
        Assert.Equal("MOCK-1", attempt.ProviderTransactionId);

        // The provider is charged the order's own total, keyed by the attempt id.
        await _payments.Received(1).ChargeAsync(
            Arg.Is<PaymentRequest>(r => r.OrderId == order.Id && r.Amount == 398_000m && r.AttemptId == attempt.Id),
            Arg.Any<CancellationToken>());
        await _stock.Received(1).ConfirmAsync(order.Id, Arg.Any<CancellationToken>());

        // Saved twice: the Pending attempt before the charge, the outcome after it.
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_Declined_LeavesTheOrderAwaitingPayment_SoTheCustomerCanRetry()
    {
        var order = GivenOrderAwaitingPayment();
        GivenTheProviderAnswers(new PaymentResult(false, "MOCK-2", "Card declined"));

        var result = await _pay.HandleAsync(new PayOrderCommand(UserId, order.Id, "tok_decline"), CancellationToken.None);

        Assert.False(result.Paid);
        Assert.Equal("Card declined", result.FailureReason);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(PaymentStatus.Failed, Assert.Single(_attempts).Status);
        await _stock.DidNotReceive().ConfirmAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        // Second try on the same order goes through as a new attempt.
        GivenTheProviderAnswers(new PaymentResult(true, "MOCK-3", null));
        var retry = await _pay.HandleAsync(new PayOrderCommand(UserId, order.Id, null), CancellationToken.None);

        Assert.True(retry.Paid);
        Assert.Equal(2, _attempts.Count);
    }

    [Fact]
    public async Task Pay_SomeoneElsesOrder_IsNotFound_AndNothingIsCharged()
    {
        var order = GivenOrderAwaitingPayment(userId: Guid.CreateVersion7());

        await Assert.ThrowsAsync<OrderNotFoundException>(() =>
            _pay.HandleAsync(new PayOrderCommand(UserId, order.Id, null), CancellationToken.None));

        await _payments.DidNotReceive().ChargeAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_AfterTheDeadline_IsRefusedBeforeAnyMoneyMoves()
    {
        var order = GivenOrderAwaitingPayment();
        _timeProvider.Advance(Order.PaymentWindow);

        await Assert.ThrowsAsync<InvalidOrderTransitionException>(() =>
            _pay.HandleAsync(new PayOrderCommand(UserId, order.Id, null), CancellationToken.None));

        await _payments.DidNotReceive().ChargeAsync(Arg.Any<PaymentRequest>(), Arg.Any<CancellationToken>());
        Assert.Empty(_attempts);
    }

    [Fact]
    public async Task Pay_WhenCatalogCannotCommitTheHold_TheOrderIsStillConfirmed()
    {
        var order = GivenOrderAwaitingPayment();
        GivenTheProviderAnswers(new PaymentResult(true, "MOCK-4", null));
        _stock.ConfirmAsync(order.Id, Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("catalog is down"));

        var result = await _pay.HandleAsync(new PayOrderCommand(UserId, order.Id, null), CancellationToken.None);

        // The customer paid; a Catalog hiccup must not turn that into an error for them.
        Assert.True(result.Paid);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public async Task Cancel_CancelsTheOrder_AndReleasesItsStock()
    {
        var order = GivenOrderAwaitingPayment();

        var result = await _cancel.HandleAsync(UserId, order.Id, CancellationToken.None);

        Assert.Equal("Cancelled", result.Status);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _stock.Received(1).ReleaseAsync(order.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_WhenTheReleaseFails_TheOrderIsStillCancelled()
    {
        var order = GivenOrderAwaitingPayment();
        _stock.ReleaseAsync(order.Id, Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("catalog is down"));

        var result = await _cancel.HandleAsync(UserId, order.Id, CancellationToken.None);

        Assert.Equal("Cancelled", result.Status);
    }

    [Fact]
    public async Task Cancel_APaidOrder_IsRefused_AndItsStockIsNotReleased()
    {
        var order = GivenOrderAwaitingPayment();
        order.Confirm(_timeProvider.GetUtcNow());

        await Assert.ThrowsAsync<InvalidOrderTransitionException>(() => _cancel.HandleAsync(UserId, order.Id, CancellationToken.None));

        await _stock.DidNotReceive().ReleaseAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
