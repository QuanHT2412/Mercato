using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace EndToEnd.Tests;

public class ShoppingJourneyTests(MercatoStackFixture stack) : IClassFixture<MercatoStackFixture>
{
    private const string Password = "P@ssw0rd123";

    [Fact]
    public async Task Admin_ListsAProduct_ACustomerBuysIt_AndTheStockReallyDrops()
    {
        var ct = TestContext.Current.CancellationToken;
        var unique = Guid.NewGuid().ToString("N")[..10];

        // --- Admin puts a product on sale: 5 units at 199,000 VND ---------------------
        var adminToken = await LoginAsync(MercatoStackFixture.AdminEmail, MercatoStackFixture.AdminPassword, ct);

        var createProduct = await SendAsync(HttpMethod.Post, "/api/catalog/products", adminToken, new
        {
            name = $"Áo khoác gió {unique}",
            description = "Chống nước nhẹ",
            variants = new[] { new { sku = $"E2E-{unique}", name = "Đen / L", price = 199000, stock = 5 } },
        }, ct);
        Assert.Equal(HttpStatusCode.Created, createProduct.StatusCode);
        var product = await createProduct.Content.ReadFromJsonAsync<JsonElement>(ct);
        var productId = product.GetProperty("id").GetGuid();
        var variantId = product.GetProperty("variants")[0].GetProperty("id").GetGuid();

        // --- Anyone can find it; a customer cannot create products --------------------
        var search = await stack.Gateway.GetFromJsonAsync<JsonElement>($"/api/catalog/products?search={unique}", ct);
        Assert.Equal(productId, search.GetProperty("items")[0].GetProperty("id").GetGuid());

        var customerToken = await RegisterAndLoginAsync($"buyer-{unique}", ct);
        var forbidden = await SendAsync(HttpMethod.Post, "/api/catalog/products", customerToken, new { name = "nope", variants = Array.Empty<object>() }, ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // --- The gateway turns unauthenticated ordering traffic away at the edge -------
        var anonymousCart = await stack.Gateway.GetAsync("/api/ordering/cart", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousCart.StatusCode);

        // --- Cart -> checkout: Ordering reserves the stock in Catalog over gRPC --------
        var addToCart = await SendAsync(HttpMethod.Put, $"/api/ordering/cart/items/{variantId}", customerToken, new { quantity = 2 }, ct);
        Assert.Equal(HttpStatusCode.OK, addToCart.StatusCode);

        var checkout = await SendAsync(HttpMethod.Post, "/api/ordering/orders/checkout", customerToken, new
        {
            receiver = "Nguyen Van A",
            phone = "0901234567",
            address = "12 Lê Lợi, Quận 1, TP.HCM",
        }, ct);
        Assert.Equal(HttpStatusCode.Created, checkout.StatusCode);
        var order = await checkout.Content.ReadFromJsonAsync<JsonElement>(ct);
        var orderId = order.GetProperty("id").GetGuid();
        Assert.Equal("AwaitingPayment", order.GetProperty("status").GetString());
        // Name and price were snapshotted from Catalog, the total computed by the backend.
        Assert.Equal(398000m, order.GetProperty("totalAmount").GetDecimal());
        Assert.Equal($"Áo khoác gió {unique}", order.GetProperty("items")[0].GetProperty("productName").GetString());

        // --- The hold is real: 2 of 5 are taken, a second buyer cannot get 4 -----------
        var rivalToken = await RegisterAndLoginAsync($"rival-{unique}", ct);
        await SendAsync(HttpMethod.Put, $"/api/ordering/cart/items/{variantId}", rivalToken, new { quantity = 4 }, ct);
        var rivalCheckout = await SendAsync(HttpMethod.Post, "/api/ordering/orders/checkout", rivalToken, new
        {
            receiver = "Tran Thi B",
            phone = "0907654321",
            address = "34 Nguyễn Huệ, Quận 1, TP.HCM",
        }, ct);
        Assert.Equal(HttpStatusCode.Conflict, rivalCheckout.StatusCode);
        var conflict = await rivalCheckout.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(3, conflict.GetProperty("availableQuantity").GetInt32());

        // Holding is not selling: on-hand stock is still 5.
        Assert.Equal(5, await GetStockAsync(productId, adminToken, ct));

        // --- Pay: the order is confirmed and the hold becomes a real decrement ---------
        var pay = await SendAsync(HttpMethod.Post, $"/api/ordering/orders/{orderId}/pay", customerToken, new { paymentToken = (string?)null }, ct);
        Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
        Assert.Equal("Confirmed", (await pay.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("status").GetString());

        Assert.Equal(3, await GetStockAsync(productId, adminToken, ct));

        var myOrders = await SendAsync(HttpMethod.Get, "/api/ordering/orders", customerToken, null, ct);
        var listed = await myOrders.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(orderId, listed[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task TheInternalGrpcStockService_IsNotReachableThroughTheGateway()
    {
        var ct = TestContext.Current.CancellationToken;

        // If the gateway forwarded this, anyone on the internet could release other
        // customers' stock holds. Its catalog route only knows /products and /categories.
        var response = await stack.Gateway.PostAsync(
            "/api/catalog/mercato.catalog.v1.StockService/ReleaseReservation", content: null, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<string> RegisterAndLoginAsync(string username, CancellationToken ct)
    {
        var email = $"{username}@example.com";
        var register = await stack.Gateway.PostAsJsonAsync(
            "/api/identity/register", new { email, username, password = Password, name = "E2E Customer" }, ct);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        return await LoginAsync(email, Password, ct);
    }

    private async Task<string> LoginAsync(string email, string password, CancellationToken ct)
    {
        var response = await stack.Gateway.PostAsJsonAsync("/api/identity/login", new { email, password }, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("accessToken").GetString()!;
    }

    // As the admin, so the answer comes from Postgres rather than from the Redis cache.
    private async Task<int> GetStockAsync(Guid productId, string adminToken, CancellationToken ct)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/catalog/products/{productId}", adminToken, null, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("variants")[0].GetProperty("stock").GetInt32();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string accessToken, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await stack.Gateway.SendAsync(request, ct);
    }
}
