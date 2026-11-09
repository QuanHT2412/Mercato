using System.Net;
using System.Net.Http.Json;
using Mercato.Ordering.Api.Contracts;
using Mercato.Ordering.Application.Carts;

namespace Ordering.IntegrationTests;

public class CartTests(OrderingApiFactory factory) : IClassFixture<OrderingApiFactory>
{
    private static Task<HttpResponseMessage> PutItemAsync(HttpClient client, Guid variantId, int quantity, CancellationToken ct) =>
        client.PutAsJsonAsync($"/cart/items/{variantId}", new SetCartItemRequest(quantity), ct);

    [Fact]
    public async Task Cart_RequiresAnAccessToken()
    {
        var response = await factory.CreateClient().GetAsync("/cart", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SetItem_AddsThenReplacesTheQuantity_AndRemoveAndClearEmptyTheCart()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClientAs(Guid.CreateVersion7());
        var shirt = Guid.CreateVersion7();
        var jeans = Guid.CreateVersion7();

        Assert.Equal(HttpStatusCode.OK, (await PutItemAsync(client, shirt, 2, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PutItemAsync(client, jeans, 1, ct)).StatusCode);

        // Same variant again: the quantity is replaced, not added, and no second line appears.
        var replaced = await (await PutItemAsync(client, shirt, 5, ct)).Content.ReadFromJsonAsync<CartResult>(ct);
        Assert.NotNull(replaced);
        Assert.Equal(2, replaced.Items.Count);
        Assert.Equal(5, replaced.Items.Single(i => i.VariantId == shirt).Quantity);
        Assert.Equal(6, replaced.TotalQuantity);

        // Read back through a fresh request: it was really persisted.
        var stored = await client.GetFromJsonAsync<CartResult>("/cart", ct);
        Assert.Equal(6, stored!.TotalQuantity);

        var afterRemove = await (await client.DeleteAsync($"/cart/items/{shirt}", ct)).Content.ReadFromJsonAsync<CartResult>(ct);
        Assert.Equal(jeans, Assert.Single(afterRemove!.Items).VariantId);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/cart", ct)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<CartResult>("/cart", ct))!.Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task SetItem_WithAQuantityOutsideOneToTen_IsABadRequest(int quantity)
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClientAs(Guid.CreateVersion7());

        var response = await PutItemAsync(client, Guid.CreateVersion7(), quantity, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ATwentyFirstDifferentItem_IsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClientAs(Guid.CreateVersion7());

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await PutItemAsync(client, Guid.CreateVersion7(), 1, ct)).StatusCode);
        }

        var overflow = await PutItemAsync(client, Guid.CreateVersion7(), 1, ct);

        Assert.Equal(HttpStatusCode.BadRequest, overflow.StatusCode);
        Assert.Equal(20, (await client.GetFromJsonAsync<CartResult>("/cart", ct))!.Items.Count);
    }

    [Fact]
    public async Task EachUserSeesOnlyTheirOwnCart()
    {
        var ct = TestContext.Current.CancellationToken;
        var alice = factory.CreateClientAs(Guid.CreateVersion7());
        var bob = factory.CreateClientAs(Guid.CreateVersion7());

        await PutItemAsync(alice, Guid.CreateVersion7(), 3, ct);

        Assert.Empty((await bob.GetFromJsonAsync<CartResult>("/cart", ct))!.Items);
        Assert.Single((await alice.GetFromJsonAsync<CartResult>("/cart", ct))!.Items);
    }
}
