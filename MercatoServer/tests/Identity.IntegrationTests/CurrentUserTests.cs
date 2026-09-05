using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Mercato.Identity.Api.Contracts;

namespace Identity.IntegrationTests;

public class CurrentUserTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithATamperedToken_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        var register = new RegisterRequest("tamper@example.com", "tamperuser", "P@ssw0rd123", "Tamper User");
        await client.PostAsJsonAsync("/register", register, ct);
        var token = await LoginAsync(client, register.Email, register.Password, ct);

        // Flip the last character of the signature: same header and payload, bad signature.
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        var response = await GetMeAsync(client, tampered, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_AsARegisteredCustomer_ReturnsTheProfileWithTheCustomerRole()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        var register = new RegisterRequest("me@example.com", "meuser", "P@ssw0rd123", "Me User");
        await client.PostAsJsonAsync("/register", register, ct);
        var token = await LoginAsync(client, register.Email, register.Password, ct);

        var response = await GetMeAsync(client, token, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(ct);
        Assert.NotNull(me);
        Assert.Equal(register.Email, me.Email);
        Assert.Equal("Customer", me.Role);
    }

    [Fact]
    public async Task Me_AsTheSeededAdmin_ReturnsTheAdminRole()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        var token = await LoginAsync(client, IdentityApiFactory.AdminEmail, IdentityApiFactory.AdminPassword, ct);

        var response = await GetMeAsync(client, token, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(ct);
        Assert.NotNull(me);
        Assert.Equal("Admin", me.Role);
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/login", new LoginRequest(email, password), ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var login = await response.Content.ReadFromJsonAsync<AccessTokenResponse>(ct);
        Assert.NotNull(login);

        return login.AccessToken;
    }

    private static async Task<HttpResponseMessage> GetMeAsync(HttpClient client, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await client.SendAsync(request, ct);
    }
}
