using Hangfire;
using Hangfire.PostgreSql;
using Mercato.Catalog.Grpc;
using Mercato.Ordering.Application.Abstractions;
using Mercato.Ordering.Infrastructure.Catalog;
using Mercato.Ordering.Infrastructure.Jobs;
using Mercato.Ordering.Infrastructure.Payments;
using Mercato.Ordering.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Mercato.Ordering.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddOrderingInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<OrderingDbContext>("ordering-db");

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddScoped<ICartRepository, CartRepository>();
        builder.Services.AddScoped<IOrderRepository, OrderRepository>();
        builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

        // "catalog-api" is not a hostname: the AppHost reference + service discovery
        // (ServiceDefaults turns it on for every HttpClient, the gRPC client included)
        // resolve it to wherever Catalog is actually listening. https because gRPC needs
        // HTTP/2, which Kestrel only negotiates over TLS.
        builder.Services.AddGrpcClient<StockService.StockServiceClient>(options =>
            options.Address = new Uri(builder.Configuration["Catalog:GrpcAddress"] ?? "https://catalog-api"));
        builder.Services.AddScoped<IStockGateway, GrpcStockGateway>();

        builder.Services.AddSingleton<IPaymentProvider, MockPaymentProvider>();

        AddBackgroundJobs(builder);

        return builder;
    }

    // Hangfire (Ordering only — CLAUDE.md section 5). Its job state lives in a `hangfire`
    // schema inside ordering_db, which Hangfire creates itself on first use: the jobs
    // belong to this service, so they live in this service's database.
    private static void AddBackgroundJobs(IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("ordering-db")
            ?? throw new InvalidOperationException("Connection string 'ordering-db' is not configured.");

        builder.Services.AddHangfire(configuration => configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));

        builder.Services.AddHangfireServer();
        builder.Services.AddTransient<ExpireOverdueOrdersJob>();
    }

    // Registers the recurring jobs. AddOrUpdate is idempotent, so calling it on every
    // startup (and from every instance) is safe.
    public static void ScheduleOrderingJobs(this IServiceProvider services) =>
        services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<ExpireOverdueOrdersJob>(
            ExpireOverdueOrdersJob.JobId,
            // Hangfire swaps this placeholder token for a real one when the job runs.
            job => job.RunAsync(CancellationToken.None),
            Cron.Minutely());

    public static async Task InitializeOrderingDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
