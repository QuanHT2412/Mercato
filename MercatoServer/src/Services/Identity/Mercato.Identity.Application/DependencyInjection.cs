using Mercato.Identity.Application.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Mercato.Identity.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services)
    {
        services.AddScoped<RegisterUserHandler>();
        services.AddScoped<LoginUserHandler>();

        return services;
    }
}
