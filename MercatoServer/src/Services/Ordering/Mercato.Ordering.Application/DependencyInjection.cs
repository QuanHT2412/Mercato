using Mercato.Ordering.Application.Carts;
using Mercato.Ordering.Application.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace Mercato.Ordering.Application;

public static class DependencyInjection
{
    // Plain handler classes resolved straight from DI — no mediator (CLAUDE.md section 5).
    public static IServiceCollection AddOrderingApplication(this IServiceCollection services)
    {
        services.AddScoped<GetCartHandler>();
        services.AddScoped<SetCartItemHandler>();
        services.AddScoped<RemoveCartItemHandler>();
        services.AddScoped<ClearCartHandler>();

        services.AddScoped<CheckoutHandler>();
        services.AddScoped<PayOrderHandler>();
        services.AddScoped<CancelOrderHandler>();
        services.AddScoped<GetOrderHandler>();
        services.AddScoped<ListOrdersHandler>();

        return services;
    }
}
