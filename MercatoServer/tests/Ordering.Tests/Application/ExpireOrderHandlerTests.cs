using Mercato.Ordering.Application.Abstractions;
using Mercato.Ordering.Application.Exceptions;
using Mercato.Ordering.Application.Orders;
using Mercato.Ordering.Domain.Orders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ordering.Tests.Application;

public class ExpireOrderHandlerTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IStockGateway _stock = Substitute.For<IStockGateway>();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero));
    private readonly ExpireOrderHandler _handler;

    public ExpireOrderHandlerTests()
    {
        _handler = new ExpireOrderHandler(_orders, _unitOfWork, _stock, _timeProvider, NullLogger<ExpireOrderHandler>.Instance);
    }

    private Order GivenOrderAwaitingPayment()
    {
        var now = _timeProvider.GetUtcNow();
        var order = Order.Place(
            Order.NewId(),
            Guid.CreateVersion7(),
            ShippingDetails.Create("Nguyen Van A", "0901234567", "12 Lê Lợi", null),
            [new OrderLine(Guid.CreateVersion7(), "Áo thun", "Trắng / M", 199_000m, 1)],
            now);
        order.AwaitPayment(now);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        return order;
    }

    [Fact]
    public async Task OverdueOrder_IsExpired_AndItsStockIsReleased()
    {
        var order = GivenOrderAwaitingPayment();
        _timeProvider.Advance(Order.PaymentWindow);

        var expired = await _handler.HandleAsync(order.Id, CancellationToken.None);

        Assert.True(expired);
        Assert.Equal(OrderStatus.Expired, order.Status);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _stock.Received(1).ReleaseAsync(order.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderStillInsideItsPaymentWindow_IsLeftAlone()
    {
        var order = GivenOrderAwaitingPayment();
        _timeProvider.Advance(Order.PaymentWindow - TimeSpan.FromSeconds(1));

        var expired = await _handler.HandleAsync(order.Id, CancellationToken.None);

        Assert.False(expired);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _stock.DidNotReceive().ReleaseAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrderPaidBeforeTheJobReachedIt_IsLeftAlone()
    {
        var order = GivenOrderAwaitingPayment();
        order.Confirm(_timeProvider.GetUtcNow());
        _timeProvider.Advance(Order.PaymentWindow);

        var expired = await _handler.HandleAsync(order.Id, CancellationToken.None);

        Assert.False(expired);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        await _stock.DidNotReceive().ReleaseAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenAPaymentCommitsAtTheSameInstant_TheJobBacksOff_AndDoesNotReleaseThePaidStock()
    {
        var order = GivenOrderAwaitingPayment();
        _timeProvider.Advance(Order.PaymentWindow);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new ConcurrencyConflictException());

        var expired = await _handler.HandleAsync(order.Id, CancellationToken.None);

        Assert.False(expired);
        await _stock.DidNotReceive().ReleaseAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownOrder_IsANoOp()
    {
        Assert.False(await _handler.HandleAsync(Guid.CreateVersion7(), CancellationToken.None));
    }
}
