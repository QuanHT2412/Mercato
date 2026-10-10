using System.Net;
using System.Net.Http.Json;
using Mercato.Catalog.Api.Contracts;
using Mercato.Catalog.Application.Categories;
using Mercato.Catalog.Application.Products;

namespace Catalog.IntegrationTests;

public class ProductCatalogTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    private static CreateProductRequest NewProduct(string name, string? description, string sku, Guid? categoryId = null) =>
        new(name, description, categoryId, [new VariantRequest(sku, "Mặc định", 199_000m, 10, null)]);

    private static async Task<ProductDetails> CreateAsync(HttpClient admin, CreateProductRequest request, CancellationToken ct)
    {
        var response = await admin.PostAsJsonAsync("/products", request, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ProductDetails>(ct))!;
    }

    private static async Task<PagedResult<ProductSummary>> SearchAsync(HttpClient client, string query, CancellationToken ct) =>
        (await client.GetFromJsonAsync<PagedResult<ProductSummary>>($"/products?{query}", ct))!;

    [Fact]
    public async Task Writes_AreRejectedForAnonymousCallers_AndForCustomers()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = NewProduct("Không được phép", null, "RBAC-1");

        var anonymous = await factory.CreateClient().PostAsJsonAsync("/products", request, ct);
        var customer = await factory.CreateClientAs("Customer").PostAsJsonAsync("/products", request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, customer.StatusCode);
    }

    [Fact]
    public async Task Admin_CreatesACategoryAndProduct_ThenAnyoneCanBrowseIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = factory.CreateClientAs("Admin");
        var anonymous = factory.CreateClient();

        var categoryResponse = await admin.PostAsJsonAsync("/categories", new CreateCategoryRequest("Thời trang nam", null), ct);
        Assert.Equal(HttpStatusCode.Created, categoryResponse.StatusCode);
        var category = (await categoryResponse.Content.ReadFromJsonAsync<CategoryResult>(ct))!;
        Assert.Equal("thoi-trang-nam", category.Slug);

        var created = await CreateAsync(admin, new CreateProductRequest(
            "Sơ mi linen tay dài",
            "Vải linen thoáng mát",
            category.Id,
            [
                new VariantRequest("so-mi-linen-m", "Trắng / M", 349_000m, 8, null),
                new VariantRequest("so-mi-linen-l", "Trắng / L", 359_000m, 0, null),
            ]), ct);

        var detail = await anonymous.GetFromJsonAsync<ProductDetails>($"/products/{created.Id}", ct);
        Assert.NotNull(detail);
        Assert.Equal("Sơ mi linen tay dài", detail.Name);
        Assert.Equal(["SO-MI-LINEN-L", "SO-MI-LINEN-M"], detail.Variants.Select(v => v.Sku));

        var byCategory = await SearchAsync(anonymous, $"categoryId={category.Id}", ct);
        var summary = Assert.Single(byCategory.Items);
        Assert.Equal(created.Id, summary.Id);
        Assert.Equal(349_000m, summary.PriceFrom);
        Assert.True(summary.InStock);

        var categories = await anonymous.GetFromJsonAsync<List<CategoryResult>>("/categories", ct);
        Assert.Contains(categories!, c => c.Id == category.Id);
    }

    [Fact]
    public async Task CreatingAProduct_WithASkuThatAlreadyExists_ReturnsConflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = factory.CreateClientAs("Admin");
        await CreateAsync(admin, NewProduct("Mũ lưỡi trai", null, "MU-DUP-1"), ct);

        var duplicate = await admin.PostAsJsonAsync("/products", NewProduct("Mũ bucket", null, "mu-dup-1"), ct);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("application/problem+json", duplicate.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreatingAProduct_WithoutVariants_ReturnsBadRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = factory.CreateClientAs("Admin");

        var response = await admin.PostAsJsonAsync("/products", new CreateProductRequest("Không có biến thể", null, null, []), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_MatchesWordPrefixesInNameAndDescription_AndRanksNameHitsFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = factory.CreateClientAs("Admin");
        var anonymous = factory.CreateClient();

        var inDescription = await CreateAsync(admin, NewProduct("Chuột không dây", "Tặng kèm lót chuột và bàn phím mini", "FTS-CHUOT"), ct);
        var inName = await CreateAsync(admin, NewProduct("Bàn phím cơ 87 phím", "Switch đỏ, hot-swap", "FTS-BANPHIM"), ct);
        await CreateAsync(admin, NewProduct("Tai nghe chụp tai", "Chống ồn chủ động", "FTS-TAINGHE"), ct);

        // Prefix of a word ("phí" -> "phím"), found in the name of one product and only
        // in the description of the other: both match, the name hit ranks first.
        var result = await SearchAsync(anonymous, "search=phí", ct);
        Assert.Equal([inName.Id, inDescription.Id], result.Items.Select(p => p.Id));
        Assert.Equal(2, result.TotalCount);

        // Every word must match (AND), in any order.
        var bothWords = await SearchAsync(anonymous, "search=cơ bàn", ct);
        Assert.Equal(inName.Id, Assert.Single(bothWords.Items).Id);

        // tsquery operator characters in user input are data, not syntax.
        var hostile = await anonymous.GetAsync("/products?search=" + Uri.EscapeDataString("phím & !(:*) | '"), ct);
        Assert.Equal(HttpStatusCode.OK, hostile.StatusCode);

        var nothing = await SearchAsync(anonymous, "search=tủ lạnh", ct);
        Assert.Empty(nothing.Items);
    }

    [Fact]
    public async Task UpdatingAProduct_InvalidatesItsCachedDetail_AndTheCachedListPages()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = factory.CreateClientAs("Admin");
        var anonymous = factory.CreateClient();
        var created = await CreateAsync(admin, NewProduct("Balo laptop chống nước", null, "CACHE-BALO"), ct);

        // Warm both caches as a public reader.
        Assert.Equal("Balo laptop chống nước", (await anonymous.GetFromJsonAsync<ProductDetails>($"/products/{created.Id}", ct))!.Name);
        Assert.Single((await SearchAsync(anonymous, "search=balo", ct)).Items);

        var update = await admin.PutAsJsonAsync(
            $"/products/{created.Id}", new UpdateProductRequest("Balo du lịch 40L", null, null, IsActive: true), ct);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        // No waiting for a TTL: the write invalidated both.
        Assert.Equal("Balo du lịch 40L", (await anonymous.GetFromJsonAsync<ProductDetails>($"/products/{created.Id}", ct))!.Name);
        Assert.Equal("Balo du lịch 40L", Assert.Single((await SearchAsync(anonymous, "search=balo", ct)).Items).Name);
    }

    [Fact]
    public async Task DeletingAProduct_HidesItFromThePublic_ButNotFromAdmins()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = factory.CreateClientAs("Admin");
        var anonymous = factory.CreateClient();
        var created = await CreateAsync(admin, NewProduct("Đèn bàn kẹp", null, "SOFT-DEN"), ct);
        await anonymous.GetAsync($"/products/{created.Id}", ct);

        var delete = await admin.DeleteAsync($"/products/{created.Id}", ct);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/products/{created.Id}", ct)).StatusCode);
        Assert.Empty((await SearchAsync(anonymous, "search=kẹp", ct)).Items);

        // includeInactive is honoured for admins and ignored for everyone else.
        Assert.Single((await SearchAsync(admin, "search=kẹp&includeInactive=true", ct)).Items);
        Assert.Empty((await SearchAsync(anonymous, "search=kẹp&includeInactive=true", ct)).Items);
        Assert.False((await admin.GetFromJsonAsync<ProductDetails>($"/products/{created.Id}", ct))!.IsActive);
    }
}
