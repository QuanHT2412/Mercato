using Mercato.Catalog.Grpc;
using Mercato.Ordering.Application.Abstractions;
using Mercato.Ordering.Infrastructure.Catalog;
using Mercato.Ordering.Infrastructure.Payments;
using Mercato.Ordering.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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

        return builder;
    }

    public static async Task InitializeOrderingDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
