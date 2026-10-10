using System.Net.Http.Headers;
using System.Security.Cryptography;
using Grpc.Net.Client;
using Mercato.Catalog.Grpc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Catalog.IntegrationTests;

// The real Catalog.Api in-process, against a real Postgres (FTS, row locks and migrations
// are the things worth testing here, and none of them exist in an in-memory fake) and a
// real Redis.
public sealed class CatalogApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8.2").Build();
    private readonly ECDsa _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        // Environment variables, exactly how the AppHost hands configuration to the
        // service — see IdentityApiFactory for why not ConfigureAppConfiguration.
        Environment.SetEnvironmentVariable("ConnectionStrings__catalog-db", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__redis", _redis.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__PublicKey", _signingKey.ExportSubjectPublicKeyInfoPem());

        // Build the host (and run the migrations) once, before any test runs.
        using var _ = CreateClient();
    }

    public override async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
        _signingKey.Dispose();
        await base.DisposeAsync();
    }

    // The tests play Identity: they sign access tokens with the private half of the key
    // whose public half the service was configured with.
    public HttpClient CreateClientAs(string role)
    {
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "mercato-identity",
            Audience = "mercato",
            Claims = new Dictionary<string, object>
            {
                ["sub"] = Guid.CreateVersion7().ToString(),
                ["name"] = $"Test {role}",
                ["role"] = role,
            },
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(_signingKey), SecurityAlgorithms.EcdsaSha256),
        });

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    public StockService.StockServiceClient CreateStockClient()
    {
        // TestServer speaks to the app in-memory; Grpc.Net.Client insists on an HTTP/2
        // response, so the handler stamps the response with the request's version.
        var channel = GrpcChannel.ForAddress(Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = new ResponseVersionHandler { InnerHandler = Server.CreateHandler() },
        });

        return new StockService.StockServiceClient(channel);
    }

    private sealed class ResponseVersionHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            response.Version = request.Version;

            return response;
        }
    }
}
