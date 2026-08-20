using System.Net;
using System.Net.Http.Json;
using Mercato.Identity.Api.Contracts;

namespace Identity.IntegrationTests;

public class RefreshThroughGatewayTests(GatewayTestFixture fixture) : IClassFixture<GatewayTestFixture>
{
    [Fact]
    public async Task Login_Refresh_Logout_ThroughGateway_HonoursTheCookiePathContract()
    {
        var client = fixture.Client;
        var ct = TestContext.Current.CancellationToken;

        // Unique per run: the AppHost's Postgres uses WithDataVolume() (deliberately,
        // for real dev usage — see AppHost.cs), so this test's data persists across
        // runs against the same dev database. A fixed email would 409 on the second run.
        var unique = Guid.NewGuid().ToString("N")[..12];
        var register = new RegisterRequest($"gateway-{unique}@example.com", $"gateway{unique}", "P@ssw0rd123", "Gateway User");
        var registerResponse = await client.PostAsJsonAsync("/api/identity/register", register, ct);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        // The client's CookieContainer captures Set-Cookie here transparently — no
        // header handling in this test — exactly like a browser would.
        var loginResponse = await client.PostAsJsonAsync(
            "/api/identity/login", new LoginRequest(register.Email, register.Password), ct);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // /refresh sits directly under Path=/api/identity/refresh, so this would pass
        // even with the narrower (buggy) path scoping considered during planning —
        // logout below is the case that actually pins the fix.
        var refreshResponse = await client.PostAsync("/api/identity/refresh", content: null, ct);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        // Logout only works if the cookie Path is wide enough to also cover
        // /api/identity/logout. A Path scoped to /api/identity/refresh alone (the bug
        // caught during planning) means the browser never attaches the cookie here —
        // logout would still answer 204 (it's idempotent-by-design), silently NOT
        // revoking anything, which the next assertion catches.
        var logoutResponse = await client.PostAsync("/api/identity/logout", content: null, ct);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        // Proves logout actually received and revoked the family: if the cookie never
        // reached /logout, this refresh would still succeed (200) using the
        // still-technically-valid token, and the Path bug would go undetected.
        var refreshAfterLogout = await client.PostAsync("/api/identity/refresh", content: null, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);
    }
}
