using System.Net.Http.Headers;
using System.Security.Cryptography;
using Mercato.Ordering.Application.Abstractions;
using Mercato.Ordering.Infrastructure.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace Ordering.IntegrationTests;

// The real Ordering.Api in-process on a real Postgres (migrations, the xmin concurrency
// token, cascade/orphan behaviour and Hangfire's storage all need one). Two things are
// swapped: Catalog (see FakeStockGateway) and the clock, so a test can step past the
// 15-minute payment window without waiting 15 minutes.
public sealed class OrderingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();
    private readonly ECDsa _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public FakeStockGateway Stock { get; } = new();

    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStockGateway>();
            services.AddSingleton<IStockGateway>(Stock);

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__ordering-db", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__PublicKey", _signingKey.ExportSubjectPublicKeyInfoPem());

        // Build the host (migrations, Hangfire schema, recurring job) once, up front.
        using var _ = CreateClient();
    }

    public override async ValueTask DisposeAsync()
    {
        // Host first: Hangfire's server must stop before its database disappears.
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        _signingKey.Dispose();
    }

    // The tests play Identity and sign access tokens for whichever user they need.
    public HttpClient CreateClientAs(Guid userId)
    {
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "mercato-identity",
            Audience = "mercato",
            Claims = new Dictionary<string, object>
            {
                ["sub"] = userId.ToString(),
                ["name"] = "Test Customer",
                ["role"] = "Customer",
            },
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(_signingKey), SecurityAlgorithms.EcdsaSha256),
        });

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    // Runs the same job Hangfire runs every minute, right now, on the test's clock.
    public async Task RunExpiryJobAsync(CancellationToken cancellationToken)
    {
        await using var scope = Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ExpireOverdueOrdersJob>().RunAsync(cancellationToken);
    }
}
