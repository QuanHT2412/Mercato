using Mercato.Ordering.Domain.Carts;
using Mercato.Ordering.Domain.Exceptions;

namespace Ordering.Tests.Domain;

public class CartTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public void CreateFor_StartsEmpty()
    {
        var cart = Cart.CreateFor(UserId, Now);

        Assert.Equal(UserId, cart.UserId);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public void CreateFor_WithoutAUser_Throws()
    {
        Assert.Throws<DomainValidationException>(() => Cart.CreateFor(Guid.Empty, Now));
    }

    [Fact]
    public void SetItem_AddsANewLine()
    {
        var cart = Cart.CreateFor(UserId, Now);
        var variantId = Guid.CreateVersion7();

        cart.SetItem(variantId, 2, Now);

        var item = Assert.Single(cart.Items);
        Assert.Equal(variantId, item.VariantId);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(UserId, item.UserId);
    }

    [Fact]
    public void SetItem_ForAVariantAlreadyInTheCart_ReplacesTheQuantity_InsteadOfAddingALine()
    {
        var cart = Cart.CreateFor(UserId, Now);
        var variantId = Guid.CreateVersion7();
        cart.SetItem(variantId, 2, Now);

        // Same request sent twice (a retry) must not end up as 2 lines or quantity 6.
        cart.SetItem(variantId, 3, Now.AddMinutes(1));
        cart.SetItem(variantId, 3, Now.AddMinutes(1));

        Assert.Equal(3, Assert.Single(cart.Items).Quantity);
        Assert.Equal(Now.AddMinutes(1), cart.UpdatedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void SetItem_WithAQuantityOutsideOneToTen_Throws(int quantity)
    {
        var cart = Cart.CreateFor(UserId, Now);

        Assert.Throws<DomainValidationException>(() => cart.SetItem(Guid.CreateVersion7(), quantity, Now));
        Assert.True(cart.IsEmpty);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void SetItem_AtTheQuantityBounds_IsAccepted(int quantity)
    {
        var cart = Cart.CreateFor(UserId, Now);

        cart.SetItem(Guid.CreateVersion7(), quantity, Now);

        Assert.Equal(quantity, Assert.Single(cart.Items).Quantity);
    }

    [Fact]
    public void SetItem_ATwentyFirstDifferentItem_Throws_ButChangingAnExistingLineStillWorks()
    {
        var cart = Cart.CreateFor(UserId, Now);
        var variantIds = Enumerable.Range(0, Cart.MaxLines).Select(_ => Guid.CreateVersion7()).ToList();

        foreach (var variantId in variantIds)
        {
            cart.SetItem(variantId, 1, Now);
        }

        Assert.Throws<DomainValidationException>(() => cart.SetItem(Guid.CreateVersion7(), 1, Now));
        Assert.Equal(Cart.MaxLines, cart.Items.Count);

        // The limit is on distinct lines, not on edits.
        cart.SetItem(variantIds[0], 5, Now);
        Assert.Equal(5, cart.Items.Single(i => i.VariantId == variantIds[0]).Quantity);
    }

    [Fact]
    public void RemoveItem_RemovesTheLine_AndReportsWhetherAnythingChanged()
    {
        var cart = Cart.CreateFor(UserId, Now);
        var variantId = Guid.CreateVersion7();
        cart.SetItem(variantId, 2, Now);

        Assert.True(cart.RemoveItem(variantId, Now));
        Assert.False(cart.RemoveItem(variantId, Now));
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public void Clear_EmptiesTheCart()
    {
        var cart = Cart.CreateFor(UserId, Now);
        cart.SetItem(Guid.CreateVersion7(), 2, Now);
        cart.SetItem(Guid.CreateVersion7(), 1, Now);

        cart.Clear(Now.AddMinutes(1));

        Assert.True(cart.IsEmpty);
        Assert.Equal(Now.AddMinutes(1), cart.UpdatedAt);
    }
}
