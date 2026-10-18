using Mercato.Ordering.Domain.Exceptions;
using Mercato.Ordering.Domain.Orders;

namespace Ordering.Tests.Domain;

public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly ShippingDetails Shipping = new("Nguyen Van A", "0901234567", "12 Lê Lợi, Quận 1, TP.HCM", null);

    private static readonly OrderLine[] Lines =
    [
        new(Guid.CreateVersion7(), "Áo thun", "Trắng / M", 199_000m, 2),
        new(Guid.CreateVersion7(), "Quần jean", "Xanh / 30", 450_000m, 1),
    ];

    private static Order PlaceOrder() => Order.Place(Order.NewId(), Guid.CreateVersion7(), Shipping, Lines, Now);

    private static Order PlaceOrderAwaitingPayment()
    {
        var order = PlaceOrder();
        order.AwaitPayment(Now);
        return order;
    }

    [Fact]
    public void Place_StartsPending_SnapshotsTheLines_AndComputesTheTotal()
    {
        var order = PlaceOrder();

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Null(order.PaymentDeadline);
        Assert.Equal(2, order.Items.Count);
        Assert.Equal(848_000m, order.TotalAmount);

        var first = order.Items.First();
        Assert.Equal("Áo thun", first.ProductName);
        Assert.Equal("Trắng / M", first.VariantName);
        Assert.Equal(199_000m, first.UnitPrice);
        Assert.Equal(398_000m, first.Subtotal);
        Assert.Equal(order.Id, first.OrderId);
    }

    [Fact]
    public void Place_WithoutItems_Throws()
    {
        Assert.Throws<DomainValidationException>(() =>
            Order.Place(Order.NewId(), Guid.CreateVersion7(), Shipping, [], Now));
    }

    [Theory]
    [InlineData("", "0901234567", "12 Lê Lợi")]
    [InlineData("Nguyen Van A", " ", "12 Lê Lợi")]
    [InlineData("Nguyen Van A", "0901234567", "")]
    public void Place_WithIncompleteShippingDetails_Throws(string receiver, string phone, string address)
    {
        Assert.Throws<DomainValidationException>(() =>
            Order.Place(Order.NewId(), Guid.CreateVersion7(), new ShippingDetails(receiver, phone, address, null), Lines, Now));
    }

    [Fact]
    public void AwaitPayment_StartsTheFifteenMinuteWindow()
    {
        var order = PlaceOrder();

        order.AwaitPayment(Now);

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(Now.AddMinutes(15), order.PaymentDeadline);
    }

    [Fact]
    public void Confirm_InsideThePaymentWindow_ConfirmsTheOrder()
    {
        var order = PlaceOrderAwaitingPayment();

        order.Confirm(Now.AddMinutes(14).AddSeconds(59));

        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void Confirm_AtOrAfterTheDeadline_Throws_EvenIfTheExpiryJobHasNotRunYet()
    {
        var order = PlaceOrderAwaitingPayment();

        Assert.Throws<InvalidOrderTransitionException>(() => order.Confirm(Now.AddMinutes(15)));
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void Confirm_BeforeTheOrderIsAwaitingPayment_Throws()
    {
        var order = PlaceOrder();

        Assert.Throws<InvalidOrderTransitionException>(() => order.Confirm(Now));
    }

    [Fact]
    public void Cancel_WhileAwaitingPayment_CancelsTheOrder()
    {
        var order = PlaceOrderAwaitingPayment();

        order.Cancel(Now.AddMinutes(1));

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Cancel_AConfirmedOrder_Throws()
    {
        var order = PlaceOrderAwaitingPayment();
        order.Confirm(Now.AddMinutes(1));

        Assert.Throws<InvalidOrderTransitionException>(() => order.Cancel(Now.AddMinutes(2)));
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void Expire_BeforeTheDeadline_Throws_AndAtTheDeadline_ExpiresTheOrder()
    {
        var order = PlaceOrderAwaitingPayment();

        Assert.Throws<InvalidOrderTransitionException>(() => order.Expire(Now.AddMinutes(14)));

        order.Expire(Now.AddMinutes(15));
        Assert.Equal(OrderStatus.Expired, order.Status);
    }

    [Fact]
    public void Expire_AConfirmedOrder_Throws()
    {
        var order = PlaceOrderAwaitingPayment();
        order.Confirm(Now.AddMinutes(1));

        Assert.Throws<InvalidOrderTransitionException>(() => order.Expire(Now.AddMinutes(20)));
    }

    // "A cancelled/expired order cannot be reactivated" — CLAUDE.md section 6.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACancelledOrExpiredOrder_CannotBeReactivated(bool cancelled)
    {
        var order = PlaceOrderAwaitingPayment();

        if (cancelled)
        {
            order.Cancel(Now.AddMinutes(1));
        }
        else
        {
            order.Expire(Now.AddMinutes(15));
        }

        Assert.Throws<InvalidOrderTransitionException>(() => order.AwaitPayment(Now.AddMinutes(16)));
        Assert.Throws<InvalidOrderTransitionException>(() => order.Confirm(Now.AddMinutes(16)));
        Assert.Throws<InvalidOrderTransitionException>(() => order.Cancel(Now.AddMinutes(16)));
        Assert.Throws<InvalidOrderTransitionException>(() => order.Expire(Now.AddMinutes(16)));
    }
}
