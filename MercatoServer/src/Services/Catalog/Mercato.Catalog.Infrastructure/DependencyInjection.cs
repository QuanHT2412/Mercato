using Mercato.Catalog.Application.Abstractions;
using Mercato.Catalog.Infrastructure.Caching;
using Mercato.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Mercato.Catalog.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddCatalogInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<CatalogDbContext>("catalog-db");

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
        builder.Services.AddScoped<IProductRepository, ProductRepository>();
        builder.Services.AddScoped<IProductQueries, ProductQueries>();
        builder.Services.AddScoped<IStockStore, StockStore>();

        // IConnectionMultiplexer from the Aspire Redis component ("redis" resource in the
        // AppHost) — connection string, health check and tracing come with it.
        builder.AddRedisClient("redis");
        builder.Services.AddSingleton<ICatalogCache, RedisCatalogCache>();

        return builder;
    }

    public static async Task InitializeCatalogDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
