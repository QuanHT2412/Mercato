using Mercato.Catalog.Domain.Exceptions;
using Mercato.Catalog.Domain.Products;

namespace Catalog.Tests.Domain;

public class StockReservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Deadline = Now.AddMinutes(15);

    private static ProductVariant CreateVariant(int stock)
    {
        var product = Product.Create("Áo thun", null, null, Now);
        return product.AddVariant("AO-THUN-M", "Size M", 199_000m, stock, null, Now);
    }

    [Fact]
    public void Reserve_WithinAvailability_CreatesAReservation_AndLeavesStockUntouched()
    {
        var variant = CreateVariant(stock: 5);
        var orderId = Guid.CreateVersion7();

        var reservation = variant.Reserve(orderId, quantity: 3, reservedQuantity: 0, Deadline, Now);

        Assert.Equal(variant.Id, reservation.VariantId);
        Assert.Equal(orderId, reservation.OrderId);
        Assert.Equal(3, reservation.Quantity);
        Assert.Equal(Deadline, reservation.ExpiresAt);
        Assert.Equal(5, variant.Stock);
    }

    [Fact]
    public void Reserve_CountsWhatOtherOrdersAlreadyHold()
    {
        var variant = CreateVariant(stock: 5);

        // 4 of the 5 units are already held by other orders in their payment window.
        var exception = Assert.Throws<InsufficientStockException>(() =>
            variant.Reserve(Guid.CreateVersion7(), quantity: 2, reservedQuantity: 4, Deadline, Now));

        Assert.Equal(variant.Id, exception.VariantId);
        Assert.Equal(2, exception.Requested);
        Assert.Equal(1, exception.Available);
    }

    [Fact]
    public void Reserve_ExactlyTheRemainingQuantity_Succeeds()
    {
        var variant = CreateVariant(stock: 5);

        var reservation = variant.Reserve(Guid.CreateVersion7(), quantity: 1, reservedQuantity: 4, Deadline, Now);

        Assert.Equal(1, reservation.Quantity);
    }

    [Fact]
    public void Reserve_OnAnInactiveVariant_ReportsNothingAvailable()
    {
        var variant = CreateVariant(stock: 5);
        variant.Update(variant.Name, variant.Price, null, isActive: false, Now);

        var exception = Assert.Throws<InsufficientStockException>(() =>
            variant.Reserve(Guid.CreateVersion7(), quantity: 1, reservedQuantity: 0, Deadline, Now));

        Assert.Equal(0, exception.Available);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Reserve_WithANonPositiveQuantity_Throws(int quantity)
    {
        var variant = CreateVariant(stock: 5);

        Assert.Throws<DomainValidationException>(() =>
            variant.Reserve(Guid.CreateVersion7(), quantity, reservedQuantity: 0, Deadline, Now));
    }

    [Fact]
    public void CommitReservation_TakesTheUnitsOffTheShelf()
    {
        var variant = CreateVariant(stock: 5);
        var reservation = variant.Reserve(Guid.CreateVersion7(), quantity: 3, reservedQuantity: 0, Deadline, Now);

        variant.CommitReservation(reservation, Now.AddMinutes(2));

        Assert.Equal(2, variant.Stock);
    }

    [Fact]
    public void CommitReservation_ThatWouldDriveStockNegative_Throws_AndLeavesStockAlone()
    {
        var variant = CreateVariant(stock: 5);
        var reservation = variant.Reserve(Guid.CreateVersion7(), quantity: 4, reservedQuantity: 0, Deadline, Now);

        // The hold expired, and in the meantime the shelf was corrected down to 2 units.
        variant.SetStock(2, reservedQuantity: 0, Now.AddMinutes(20));

        Assert.Throws<InsufficientStockException>(() => variant.CommitReservation(reservation, Now.AddMinutes(21)));
        Assert.Equal(2, variant.Stock);
    }

    [Fact]
    public void SetStock_BelowWhatIsReserved_Throws()
    {
        var variant = CreateVariant(stock: 5);

        Assert.Throws<DomainValidationException>(() => variant.SetStock(2, reservedQuantity: 3, Now));
        Assert.Equal(5, variant.Stock);
    }

    [Fact]
    public void IsLive_TrueUntilTheDeadline_FalseAtTheExactInstant()
    {
        var variant = CreateVariant(stock: 5);
        var reservation = variant.Reserve(Guid.CreateVersion7(), quantity: 1, reservedQuantity: 0, Deadline, Now);

        Assert.True(reservation.IsLive(Deadline.AddSeconds(-1)));
        Assert.False(reservation.IsLive(Deadline));
    }
}
