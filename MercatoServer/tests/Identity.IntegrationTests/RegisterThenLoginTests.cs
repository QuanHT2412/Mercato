using System.Net;
using System.Net.Http.Json;
using Mercato.Identity.Api.Contracts;

namespace Identity.IntegrationTests;

public class RegisterThenLoginTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    [Fact]
    public async Task Register_ThenLogin_ReturnsAccessToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        var request = new RegisterRequest("saga@example.com", "sagacustomer", "P@ssw0rd123", "Saga Customer");

        var registerResponse = await client.PostAsJsonAsync("/register", request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var registered = await registerResponse.Content.ReadFromJsonAsync<RegisterResponse>(cancellationToken);
        Assert.NotNull(registered);
        Assert.Equal(request.Email, registered.Email);

        var loginResponse = await client.PostAsJsonAsync(
            "/login",
            new LoginRequest(request.Email, request.Password),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken);
        Assert.NotNull(login);
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        Assert.True(login.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsConflictProblemDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        var request = new RegisterRequest("duplicate@example.com", "duplicateuser", "P@ssw0rd123", "Duplicate User");

        var first = await client.PostAsJsonAsync("/register", request, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            "/register",
            request with { Username = "differentusername" },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        var request = new RegisterRequest("wrongpass@example.com", "wrongpassuser", "P@ssw0rd123", "Wrong Pass User");

        await client.PostAsJsonAsync("/register", request, cancellationToken);

        var loginResponse = await client.PostAsJsonAsync(
            "/login",
            new LoginRequest(request.Email, "not-the-right-password"),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }
}
