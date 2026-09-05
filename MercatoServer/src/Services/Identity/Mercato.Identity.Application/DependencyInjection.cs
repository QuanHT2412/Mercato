using Mercato.Identity.Application.Tokens;
using Mercato.Identity.Application.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Mercato.Identity.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services)
    {
        services.AddScoped<RegisterUserHandler>();
        services.AddScoped<LoginUserHandler>();
        services.AddScoped<RefreshAccessTokenHandler>();
        services.AddScoped<LogoutHandler>();
        services.AddScoped<GetCurrentUserHandler>();

        return services;
    }
}
