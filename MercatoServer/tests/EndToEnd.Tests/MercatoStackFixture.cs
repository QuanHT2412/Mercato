using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace EndToEnd.Tests;

// Boots the REAL system from the AppHost — Postgres, Redis, RabbitMQ, the three services
// and the gateway — and hands out an HttpClient pointed at the gateway. Nothing is faked:
// this is the only place where Ordering's gRPC call really reaches Catalog through
// service discovery, and where the gateway's routing and edge auth are exercised together.
public sealed class MercatoStackFixture : IAsyncLifetime
{
    // The admin Identity seeds at startup (email is Identity's default; the password is
    // handed to the AppHost below). It only ever exists in this run's throwaway database.
    public const string AdminEmail = "admin@mercato.local";
    public const string AdminPassword = "end-to-end-admin-pw";

    private DistributedApplication? _app;

    public HttpClient Gateway { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Mercato_AppHost>(
            [$"--Parameters:admin-password={AdminPassword}"]);

        // The AppHost gives its containers named data volumes so a developer's data
        // survives restarts. A test run must not read or write that data, so the volumes
        // are dropped here: every run starts from empty, disposable containers.
        foreach (var resource in appHost.Resources)
        {
            foreach (var mount in resource.Annotations.OfType<ContainerMountAnnotation>().ToList())
            {
                resource.Annotations.Remove(mount);
            }
        }

        _app = await appHost.BuildAsync();
        await _app.StartAsync();

        using var readyCts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("gateway", readyCts.Token);

        Gateway = new HttpClient(new HttpClientHandler
        {
            // Aspire's dev HTTPS endpoints use the ASP.NET Core dev certificate.
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        })
        {
            BaseAddress = _app.GetEndpoint("gateway", "https"),
        };
    }

    public async ValueTask DisposeAsync()
    {
        Gateway?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
