using Mercato.Catalog.Application.Categories;
using Mercato.Catalog.Application.Products;
using Mercato.Catalog.Application.Stock;
using Microsoft.Extensions.DependencyInjection;

namespace Mercato.Catalog.Application;

public static class DependencyInjection
{
    // Plain handler classes resolved straight from DI — no mediator (CLAUDE.md section 5).
    public static IServiceCollection AddCatalogApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateCategoryHandler>();
        services.AddScoped<ListCategoriesHandler>();
        services.AddScoped<DeleteCategoryHandler>();

        services.AddScoped<CreateProductHandler>();
        services.AddScoped<UpdateProductHandler>();
        services.AddScoped<DeactivateProductHandler>();
        services.AddScoped<AddVariantHandler>();
        services.AddScoped<UpdateVariantHandler>();
        services.AddScoped<GetProductHandler>();
        services.AddScoped<SearchProductsHandler>();

        services.AddScoped<ReserveStockHandler>();
        services.AddScoped<ReleaseStockHandler>();
        services.AddScoped<ConfirmStockHandler>();

        return services;
    }
}
