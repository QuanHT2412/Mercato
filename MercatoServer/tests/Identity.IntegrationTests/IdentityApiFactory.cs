using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Identity.IntegrationTests;

public sealed class IdentityApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        // WebApplicationFactory hosts Program.cs in-process, so setting process
        // environment variables before the host builds is the most reliable way to
        // feed it a connection string/keys — this is also how Aspire really does it
        // (env vars from AppHost), so the test mirrors production wiring.
        var (privateKeyPem, _) = GenerateEcKeyPair();
        Environment.SetEnvironmentVariable("ConnectionStrings__identity-db", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__PrivateKey", privateKeyPem);
        Environment.SetEnvironmentVariable("Jwt__Issuer", "mercato-identity-tests");
        Environment.SetEnvironmentVariable("Jwt__Audience", "mercato-tests");

        // Force host creation (and the one-time EF migration it runs) here, while
        // nothing else is running yet. Test methods in the same class run in parallel
        // by default and each calls CreateClient(); without this, two of them can race
        // to build the (lazily-created, cached) host and both try to migrate the same
        // database at once — "relation already exists".
        using var _ = CreateClient();
    }

    public override async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    private static (string PrivateKeyPem, string PublicKeyPem) GenerateEcKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportECPrivateKeyPem(), ecdsa.ExportSubjectPublicKeyInfoPem());
    }
}
