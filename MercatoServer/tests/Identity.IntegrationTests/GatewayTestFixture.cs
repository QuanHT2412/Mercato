extern alias AppHostAssembly;

using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace Identity.IntegrationTests;

// Spins up the REAL AppHost (Postgres/RabbitMQ/Redis + all 4 services + gateway) and
// hands out an HttpClient with a real CookieContainer, so tests exercise the actual
// gateway path-stripping + cookie Path contract end to end — the thing a
// WebApplicationFactory test against the service directly cannot verify (ADR 0002
// follow-up). Slow relative to the other integration tests (full stack boot); kept as
// its own fixture so it's only paid for by the tests that need it.
public sealed class GatewayTestFixture : IAsyncLifetime
{
    private DistributedApplication? _app;

    public HttpClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<AppHostAssembly::Projects.Mercato_AppHost>();

        _app = await appHost.BuildAsync();
        await _app.StartAsync();

        using var readyCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("gateway", readyCts.Token);

        var gatewayUri = _app.GetEndpoint("gateway", "https");

        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            // Aspire's dev HTTPS endpoints use the ASP.NET Core dev certificate, which
            // this HttpClientHandler (unlike the OS trust store used elsewhere) doesn't
            // automatically trust.
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };

        Client = new HttpClient(handler) { BaseAddress = gatewayUri };
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
