using Mercato.Identity.Application.Abstractions;
using Mercato.Identity.Domain.Users;
using Mercato.Identity.Infrastructure.Auth;
using Mercato.Identity.Infrastructure.Persistence;
using Mercato.Identity.Infrastructure.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mercato.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddIdentityInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<IdentityDbContext>("identity-db");

        builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        builder.Services.AddSingleton<IAccessTokenGenerator, JwtAccessTokenGenerator>();
        builder.Services.AddSingleton<IRefreshTokenGenerator, RefreshTokenGenerator>();

        builder.Services.Configure<AdminSeedOptions>(builder.Configuration.GetSection(AdminSeedOptions.SectionName));
        builder.Services.AddScoped<AdminSeeder>();

        return builder;
    }

    // Applies pending migrations, then seeds the admin account if one is configured.
    public static async Task InitializeIdentityDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<AdminSeeder>().SeedAsync(cancellationToken);
    }
}
