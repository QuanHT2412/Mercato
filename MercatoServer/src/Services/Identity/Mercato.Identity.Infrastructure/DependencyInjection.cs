using Mercato.Identity.Application.Abstractions;
using Mercato.Identity.Domain.Users;
using Mercato.Identity.Infrastructure.Auth;
using Mercato.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mercato.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddIdentityInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<IdentityDbContext>("identity-db");

        builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        builder.Services.AddSingleton<IAccessTokenGenerator, JwtAccessTokenGenerator>();

        return builder;
    }
}
