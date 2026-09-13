using Mercato.Catalog.Domain.Exceptions;
using Mercato.Catalog.Domain.Products;

namespace Catalog.Tests.Domain;

public class ProductTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_SetsExpectedDefaults()
    {
        var categoryId = Guid.CreateVersion7();

        var product = Product.Create("  Áo thun cotton ", "  100% cotton  ", categoryId, Now);

        Assert.NotEqual(Guid.Empty, product.Id);
        Assert.Equal("Áo thun cotton", product.Name);
        Assert.Equal("100% cotton", product.Description);
        Assert.Equal(categoryId, product.CategoryId);
        Assert.True(product.IsActive);
        Assert.Empty(product.Variants);
        Assert.Equal(Now, product.CreatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyName_Throws(string name)
    {
        Assert.Throws<DomainValidationException>(() => Product.Create(name, null, null, Now));
    }

    [Fact]
    public void Create_WithBlankDescription_StoresNull()
    {
        var product = Product.Create("Áo thun", "   ", null, Now);

        Assert.Null(product.Description);
    }

    [Fact]
    public void AddVariant_NormalizesTheSku_AndAttachesItToTheProduct()
    {
        var product = Product.Create("Áo thun", null, null, Now);

        var variant = product.AddVariant(" ao-thun-m ", "Size M", 199_000m, 12, null, Now);

        Assert.Same(variant, Assert.Single(product.Variants));
        Assert.Equal(product.Id, variant.ProductId);
        Assert.Equal("AO-THUN-M", variant.Sku);
        Assert.Equal(199_000m, variant.Price);
        Assert.Equal(12, variant.Stock);
        Assert.True(variant.IsActive);
    }

    [Fact]
    public void AddVariant_WithASkuAlreadyOnTheProduct_Throws()
    {
        var product = Product.Create("Áo thun", null, null, Now);
        product.AddVariant("AO-THUN-M", "Size M", 199_000m, 12, null, Now);

        Assert.Throws<DomainValidationException>(() =>
            product.AddVariant("ao-thun-m", "Size M again", 199_000m, 1, null, Now));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(199_000.5)]
    public void AddVariant_WithANegativeOrFractionalVndPrice_Throws(double price)
    {
        var product = Product.Create("Áo thun", null, null, Now);

        Assert.Throws<DomainValidationException>(() =>
            product.AddVariant("AO-THUN-M", "Size M", (decimal)price, 1, null, Now));
    }

    [Fact]
    public void AddVariant_WithNegativeStock_Throws()
    {
        var product = Product.Create("Áo thun", null, null, Now);

        Assert.Throws<DomainValidationException>(() =>
            product.AddVariant("AO-THUN-M", "Size M", 199_000m, -1, null, Now));
    }

    [Fact]
    public void Deactivate_ThenActivate_TogglesIsActive_AndTouchesUpdatedAt()
    {
        var product = Product.Create("Áo thun", null, null, Now);
        var later = Now.AddHours(1);

        product.Deactivate(later);
        Assert.False(product.IsActive);
        Assert.Equal(later, product.UpdatedAt);

        product.Activate(later.AddHours(1));
        Assert.True(product.IsActive);
    }

    [Fact]
    public void UpdateDetails_ReplacesNameDescriptionAndCategory()
    {
        var product = Product.Create("Áo thun", "cũ", null, Now);
        var categoryId = Guid.CreateVersion7();

        product.UpdateDetails("Áo thun oversize", "mới", categoryId, Now.AddMinutes(5));

        Assert.Equal("Áo thun oversize", product.Name);
        Assert.Equal("mới", product.Description);
        Assert.Equal(categoryId, product.CategoryId);
        Assert.Equal(Now.AddMinutes(5), product.UpdatedAt);
    }
}
