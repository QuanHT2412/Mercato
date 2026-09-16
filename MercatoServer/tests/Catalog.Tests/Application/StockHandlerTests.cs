using Mercato.Catalog.Application.Abstractions;
using Mercato.Catalog.Application.Stock;
using Mercato.Catalog.Domain.Exceptions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Catalog.Tests.Application;

public class StockHandlerTests
{
    private static readonly TimeSpan Hold = TimeSpan.FromMinutes(15);

    private readonly FakeStockStore _store = new();
    private readonly ICatalogCache _cache = Substitute.For<ICatalogCache>();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 9, 16, 9, 0, 0, TimeSpan.Zero));
    private readonly ReserveStockHandler _reserve;
    private readonly ReleaseStockHandler _release;
    private readonly ConfirmStockHandler _confirm;

    public StockHandlerTests()
    {
        _reserve = new ReserveStockHandler(_store, _timeProvider);
        _release = new ReleaseStockHandler(_store);
        _confirm = new ConfirmStockHandler(_store, _cache, _timeProvider);
    }

    private Task<ReserveStockResult> ReserveAsync(Guid orderId, params ReserveStockLine[] lines) =>
        _reserve.HandleAsync(new ReserveStockCommand(orderId, lines, Hold), CancellationToken.None);

    [Fact]
    public async Task Reserve_ReturnsASnapshotOfNamesAndPrice_AndHoldsUntilTheDeadline()
    {
        var variant = _store.AddVariant("Áo thun", "Size M", 199_000m, stock: 5);
        var orderId = Guid.CreateVersion7();

        var result = await ReserveAsync(orderId, new ReserveStockLine(variant.Id, 2));

        Assert.True(result.Reserved);
        var line = Assert.Single(result.Lines);
        Assert.Equal(new ReservedLine(variant.Id, "Áo thun", "Size M", 199_000m, 2), line);

        var reservation = Assert.Single(_store.Reservations);
        Assert.Equal(orderId, reservation.OrderId);
        Assert.Equal(_timeProvider.GetUtcNow() + Hold, reservation.ExpiresAt);
        Assert.Equal(5, variant.Stock);
    }

    [Fact]
    public async Task Reserve_SeesUnitsHeldByAnotherOrder_AndReportsWhatIsLeft()
    {
        var variant = _store.AddVariant("Áo thun", "Size M", 199_000m, stock: 5);
        await ReserveAsync(Guid.CreateVersion7(), new ReserveStockLine(variant.Id, 4));

        var result = await ReserveAsync(Guid.CreateVersion7(), new ReserveStockLine(variant.Id, 2));

        Assert.False(result.Reserved);
        Assert.Equal(ReserveStockFailure.InsufficientStock, result.Failure);
        Assert.Equal(variant.Id, result.FailedVariantId);
        Assert.Equal(1, result.AvailableQuantity);
        Assert.Single(_store.Reservations);
    }

    [Fact]
    public async Task Reserve_AfterAnotherOrdersHoldExpired_GetsTheUnitsBack()
    {
        var variant = _store.AddVariant("Áo thun", "Size M", 199_000m, stock: 5);
        await ReserveAsync(Guid.CreateVersion7(), new ReserveStockLine(variant.Id, 5));

        _timeProvider.Advance(Hold);
        var result = await ReserveAsync(Guid.CreateVersion7(), new ReserveStockLine(variant.Id, 5));

        Assert.True(result.Reserved);
    }

    [Fact]
    public async Task Reserve_IsAllOrNothing_WhenOneLineCannotBeHeld()
    {
        var plenty = _store.AddVariant("Áo thun", "Size M", 199_000m, stock: 10);
        var scarce = _store.AddVariant("Quần jean", "Size 30", 450_000m, stock: 1);

        var result = await ReserveAsync(
            Guid.CreateVersion7(),
            new ReserveStockLine(plenty.Id, 2),
            new ReserveStockLine(scarce.Id, 3));

        Assert.False(result.Reserved);
        Assert.Equal(scarce.Id, result.FailedVariantId);
        Assert.Empty(_store.Reservations);
    }

    [Fact]
    public async Task Reserve_WithAnUnknownVariant_FailsWithoutHoldingAnything()
    {
        var known = _store.AddVariant("Áo thun", "Size M", 199_000m, stock: 10);
        var unknownId = Guid.CreateVersion7();

        var result = await ReserveAsync(
            Guid.CreateVersion7(),
            new ReserveStockLine(known.Id, 1),
            new ReserveStockLine(unknownId, 1));

        Assert.Equal(ReserveStockFailure.VariantNotFound, result.Failure);
        Assert.Equal(unknownId, result.FailedVariantId);
        Assert.Empty(_store.Reservations);
    }

    [Fact]
    public async Task Reserve_WhenTheProductWasDeactivated_Fails()
    {
        var variant = _store.AddVariant("Áo thun", "Size M", 199_000m, stock: 10, productIsActive: false);

        var result = await ReserveAsync(Guid.CreateVersion7(), new ReserveStockLine(variant.Id, 1));

        Assert.Equal(ReserveStockFailure.InsufficientStock, result.Failure);
        Assert.Equal(0, result.AvailableQuantity);
    }

    [Fact]
    public async Task Reserve_RepeatedForTheSameOrder_DoesNotHoldTwice()
    {
        var variant = _store.AddVariant("Áo thun", "Size M", 199_000m, stock: 5);
        var orderId = Guid.CreateVersion7();
        await ReserveAsync(orderId, new ReserveStockLine(variant.Id, 3));

        // Retry of the same call — with 3 already held, a second hold of 3 would not even fit.
        var retry = await ReserveAsync(orderId, new ReserveStockLine(variant.Id, 3));

        Assert.True(retry.Reserved);
        Assert.Equal(3, Assert.Single(retry.Lines).Quantity);
        Assert.Single(_store.Reservations);
    }

    [Fact]
    public async Task Reserve_MergesDuplicateLinesForTheSameVariant()
    {
        var variant = _store.AddVariant("Áo thun", "Size M", 199_000m, stock: 5);

        var result = await ReserveAsync(
            Guid.CreateVersion7(),
            new ReserveStockLine(variant.Id, 2),
            new ReserveStockLine(variant.Id, 2));

        Assert.True(result.Reserved);
        Assert.Equal(4, Assert.Single(_store.Reservations).Quantity);
    }

    [Fact]
    public async Task Reserve_WithNoLinesOrANonPositiveQuantity_Throws()
    {
        var variant = _store.AddVariant("Áo thun", "Size M", 199_000m, stock: 5);

        await Assert.ThrowsAsync<DomainValidationException>(() => ReserveAsync(Guid.CreateVersion7()));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            ReserveAsync(Guid.CreateVersion7(), new ReserveStockLine(variant.Id, 0)));
    }

    [Fact]
    public async Task Release_RemovesTheHold_AndIsIdempotent()
    {
        var variant = _store.AddVariant("Áo thun", "Size M", 199_000m, stock: 5);
        var orderId = Guid.CreateVersion7();
        await ReserveAsync(orderId, new ReserveStockLine(variant.Id, 5));

        Assert.True(await _release.HandleAsync(orderId, CancellationToken.None));
        Assert.False(await _release.HandleAsync(orderId, CancellationToken.None));

        Assert.Empty(_store.Reservations);
        Assert.Equal(5, variant.Stock);
        Assert.True((await ReserveAsync(Guid.CreateVersion7(), new ReserveStockLine(variant.Id, 5))).Reserved);
    }

    [Fact]
    public async Task Confirm_DecrementsStockOnce_EvenWhenCalledTwice()
    {
        var variant = _store.AddVariant("Áo thun", "Size M", 199_000m, stock: 5);
        var orderId = Guid.CreateVersion7();
        await ReserveAsync(orderId, new ReserveStockLine(variant.Id, 3));

        Assert.True(await _confirm.HandleAsync(orderId, CancellationToken.None));
        Assert.False(await _confirm.HandleAsync(orderId, CancellationToken.None));

        Assert.Equal(2, variant.Stock);
        Assert.Empty(_store.Reservations);
        await _cache.Received(1).RemoveAsync(CatalogCacheKeys.Product(variant.ProductId), Arg.Any<CancellationToken>());
    }
}
