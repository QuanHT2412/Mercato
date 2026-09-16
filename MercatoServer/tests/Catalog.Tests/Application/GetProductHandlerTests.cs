using Mercato.Catalog.Application.Abstractions;
using Mercato.Catalog.Application.Products;
using NSubstitute;

namespace Catalog.Tests.Application;

public class GetProductHandlerTests
{
    private readonly IProductQueries _queries = Substitute.For<IProductQueries>();
    private readonly ICatalogCache _cache = Substitute.For<ICatalogCache>();
    private readonly GetProductHandler _handler;

    public GetProductHandlerTests()
    {
        _handler = new GetProductHandler(_queries, _cache);
    }

    private static ProductDetails CreateDetails(bool isActive = true) => new(
        Guid.CreateVersion7(),
        "Áo thun",
        null,
        null,
        isActive,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        [
            new ProductVariantDetails(Guid.CreateVersion7(), "AO-M", "Size M", 199_000m, 5, null, IsActive: true),
            new ProductVariantDetails(Guid.CreateVersion7(), "AO-L", "Size L", 199_000m, 0, null, IsActive: false),
        ]);

    [Fact]
    public async Task CacheHit_ReturnsTheCachedCopy_WithoutTouchingTheDatabase()
    {
        var cached = CreateDetails();
        _cache.GetAsync<ProductDetails>(CatalogCacheKeys.Product(cached.Id), Arg.Any<CancellationToken>()).Returns(cached);

        var result = await _handler.HandleAsync(cached.Id, includeInactive: false, CancellationToken.None);

        Assert.Same(cached, result);
        await _queries.DidNotReceive().GetDetailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CacheMiss_ReadsTheDatabase_ThenPopulatesTheCache_WithActiveVariantsOnly()
    {
        var product = CreateDetails();
        _queries.GetDetailsAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        var result = await _handler.HandleAsync(product.Id, includeInactive: false, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("AO-M", Assert.Single(result.Variants).Sku);
        await _cache.Received(1).SetAsync(
            CatalogCacheKeys.Product(product.Id), result, CatalogCacheKeys.ProductTimeToLive, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InactiveProduct_IsHiddenFromThePublic_AndNotCached()
    {
        var product = CreateDetails(isActive: false);
        _queries.GetDetailsAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        var result = await _handler.HandleAsync(product.Id, includeInactive: false, CancellationToken.None);

        Assert.Null(result);
        await _cache.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<ProductDetails>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Admin_BypassesTheCache_AndSeesInactiveProductsAndVariants()
    {
        var product = CreateDetails(isActive: false);
        _queries.GetDetailsAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        var result = await _handler.HandleAsync(product.Id, includeInactive: true, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Same(product, result);
        Assert.Equal(2, result.Variants.Count);
        await _cache.DidNotReceive().GetAsync<ProductDetails>(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
