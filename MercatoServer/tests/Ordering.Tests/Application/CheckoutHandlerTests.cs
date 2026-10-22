using Mercato.Ordering.Application.Abstractions;
using Mercato.Ordering.Application.Exceptions;
using Mercato.Ordering.Application.Orders;
using Mercato.Ordering.Domain.Carts;
using Mercato.Ordering.Domain.Exceptions;
using Mercato.Ordering.Domain.Orders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ordering.Tests.Application;

public class CheckoutHandlerTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid ShirtId = Guid.CreateVersion7();
    private static readonly Guid JeansId = Guid.CreateVersion7();

    private readonly ICartRepository _carts = Substitute.For<ICartRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IStockGateway _stock = Substitute.For<IStockGateway>();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero));
    private readonly CheckoutHandler _handler;

    public CheckoutHandlerTests()
    {
        _handler = new CheckoutHandler(_carts, _orders, _unitOfWork, _stock, _timeProvider, NullLogger<CheckoutHandler>.Instance);
    }

    private static CheckoutCommand Command(string address = "12 Lê Lợi, Quận 1, TP.HCM") =>
        new(UserId, "Nguyen Van A", "0901234567", address, null);

    private Cart GivenCart()
    {
        var cart = Cart.CreateFor(UserId, _timeProvider.GetUtcNow());
        cart.SetItem(ShirtId, 2, _timeProvider.GetUtcNow());
        cart.SetItem(JeansId, 1, _timeProvider.GetUtcNow());
        _carts.GetAsync(UserId, Arg.Any<CancellationToken>()).Returns(cart);

        return cart;
    }

    private void GivenStockIsReserved() =>
        _stock.ReserveAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<StockLine>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new StockReservationResult(
                true,
                [
                    new ReservedLine(ShirtId, "Áo thun", "Trắng / M", 199_000m, 2),
                    new ReservedLine(JeansId, "Quần jean", "Xanh / 30", 450_000m, 1),
                ],
                StockFailure.None,
                null,
                0));

    [Fact]
    public async Task HappyPath_ReservesStock_ThenCreatesTheOrderAwaitingPayment_AndEmptiesTheCart()
    {
        var cart = GivenCart();
        GivenStockIsReserved();
        Order? saved = null;
        _orders.Add(Arg.Do<Order>(o => saved = o));

        var result = await _handler.HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("AwaitingPayment", result.Status);
        Assert.Equal(848_000m, result.TotalAmount);
        Assert.Equal(_timeProvider.GetUtcNow() + Order.PaymentWindow, result.PaymentDeadline);
        Assert.Equal(2, result.Items.Count);

        Assert.NotNull(saved);
        Assert.Equal(result.Id, saved.Id);
        Assert.True(cart.IsEmpty);

        // Order and emptied cart are committed together, once.
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // The hold is taken for the SAME id the order ends up with, for the cart's
        // lines, and outlives the payment window.
        await _stock.Received(1).ReserveAsync(
            result.Id,
            Arg.Is<IReadOnlyList<StockLine>>(l => l.Count == 2 && l.Contains(new StockLine(ShirtId, 2)) && l.Contains(new StockLine(JeansId, 1))),
            CheckoutHandler.StockHold,
            Arg.Any<CancellationToken>());
        Assert.True(CheckoutHandler.StockHold > Order.PaymentWindow);
        await _stock.DidNotReceive().ReleaseAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmptyCart_Throws_WithoutCallingCatalog()
    {
        _carts.GetAsync(UserId, Arg.Any<CancellationToken>()).Returns(Cart.CreateFor(UserId, _timeProvider.GetUtcNow()));

        await Assert.ThrowsAsync<EmptyCartException>(() => _handler.HandleAsync(Command(), CancellationToken.None));

        await _stock.DidNotReceive().ReserveAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<StockLine>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidShippingDetails_AreRejectedBeforeAnyStockIsReserved()
    {
        GivenCart();

        await Assert.ThrowsAsync<DomainValidationException>(() => _handler.HandleAsync(Command(address: "  "), CancellationToken.None));

        await _stock.DidNotReceive().ReserveAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<StockLine>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OutOfStock_Throws_CreatesNoOrder_AndKeepsTheCart()
    {
        var cart = GivenCart();
        _stock.ReserveAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<StockLine>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new StockReservationResult(false, [], StockFailure.InsufficientStock, JeansId, 0));

        var exception = await Assert.ThrowsAsync<OutOfStockException>(() => _handler.HandleAsync(Command(), CancellationToken.None));

        Assert.Equal(JeansId, exception.VariantId);
        Assert.Equal(2, cart.Items.Count);
        _orders.DidNotReceive().Add(Arg.Any<Order>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());

        // Nothing was held (reserve is all-or-nothing), so there is nothing to compensate.
        await _stock.DidNotReceive().ReleaseAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenSavingTheOrderFails_TheStockHoldIsReleased()
    {
        GivenCart();
        GivenStockIsReserved();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("database is down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(Command(), CancellationToken.None));

        // Compensation targets exactly the order id the hold was taken for.
        var reservedFor = (Guid)_stock.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IStockGateway.ReserveAsync)).GetArguments()[0]!;
        await _stock.Received(1).ReleaseAsync(reservedFor, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenEvenTheCompensationFails_TheOriginalErrorStillSurfaces()
    {
        GivenCart();
        GivenStockIsReserved();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("database is down"));
        _stock.ReleaseAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("catalog is down too"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.HandleAsync(Command(), CancellationToken.None));

        Assert.Equal("database is down", exception.Message);
    }
}
