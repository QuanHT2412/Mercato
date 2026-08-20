using System.Net;
using System.Net.Http.Json;
using Mercato.Identity.Api.Contracts;

namespace Identity.IntegrationTests;

public class RefreshTokenRotationTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    [Fact]
    public async Task Login_Refresh_Refresh_ReuseOld_Logout_RefreshAfterLogout()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        var register = new RegisterRequest("rotation@example.com", "rotationuser", "P@ssw0rd123", "Rotation User");
        await client.PostAsJsonAsync("/register", register, ct);

        var loginResponse = await client.PostAsJsonAsync("/login", new LoginRequest(register.Email, register.Password), ct);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var firstCookie = ExtractCookieValue(loginResponse);

        // First refresh rotates first -> second.
        var refresh1 = await PostWithCookieAsync(client, "/refresh", firstCookie, ct);
        Assert.Equal(HttpStatusCode.OK, refresh1.StatusCode);
        var secondCookie = ExtractCookieValue(refresh1);
        Assert.NotEqual(firstCookie, secondCookie);

        // Second refresh, using the NEW token, rotates second -> third — a real chain,
        // not just "the same token refreshed twice".
        var refresh2 = await PostWithCookieAsync(client, "/refresh", secondCookie, ct);
        Assert.Equal(HttpStatusCode.OK, refresh2.StatusCode);
        var thirdCookie = ExtractCookieValue(refresh2);
        Assert.NotEqual(secondCookie, thirdCookie);

        // Reusing the FIRST (already-rotated) token: flat 401 is the slice-2 behavior —
        // slice 3 replaces this with grace-window/reuse-detection.
        var reuseFirst = await PostWithCookieAsync(client, "/refresh", firstCookie, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, reuseFirst.StatusCode);

        var logout = await PostWithCookieAsync(client, "/logout", thirdCookie, ct);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var afterLogout = await PostWithCookieAsync(client, "/refresh", thirdCookie, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task Logout_WithoutCookie_IsStillNoContent()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        var response = await client.PostAsync("/logout", content: null, ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithoutCookie_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        var response = await client.PostAsync("/refresh", content: null, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostWithCookieAsync(
        HttpClient client, string path, string cookieValue, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Cookie", $"refresh_token={cookieValue}");

        return await client.SendAsync(request, cancellationToken);
    }

    private static string ExtractCookieValue(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var values));
        var setCookie = values.FirstOrDefault(v => v.StartsWith("refresh_token=", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(setCookie);

        var firstSegment = setCookie.Split(';')[0];
        return firstSegment[(firstSegment.IndexOf('=') + 1)..];
    }
}
