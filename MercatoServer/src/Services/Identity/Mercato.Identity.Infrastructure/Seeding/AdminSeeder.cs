using Mercato.Identity.Application.Abstractions;
using Mercato.Identity.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mercato.Identity.Infrastructure.Seeding;

public sealed class AdminSeedOptions
{
    public const string SectionName = "Seed";

    public string AdminEmail { get; init; } = string.Empty;

    public string AdminUsername { get; init; } = "admin";

    public string AdminName { get; init; } = "Store Admin";

    // Never in appsettings: comes from the AppHost's optional 'admin-password' user
    // secret. Empty means "do not seed an admin" — the system still runs, there is just
    // nobody who can manage the catalog.
    public string AdminPassword { get; init; } = string.Empty;
}

// There is no "create admin" endpoint on purpose (that would be an endpoint that hands out
// the highest privilege); the first admin is seeded at startup instead.
internal sealed class AdminSeeder(
    IUserRepository userRepository,
    IPasswordHasher<User> passwordHasher,
    IOptions<AdminSeedOptions> options,
    ILogger<AdminSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var seed = options.Value;

        if (string.IsNullOrWhiteSpace(seed.AdminEmail) || string.IsNullOrWhiteSpace(seed.AdminPassword))
        {
            logger.LogInformation("Admin seeding skipped: Seed:AdminEmail / Seed:AdminPassword not configured.");
            return;
        }

        if (await userRepository.ExistsByEmailAsync(seed.AdminEmail, cancellationToken))
        {
            return;
        }

        var passwordHash = passwordHasher.HashPassword(null!, seed.AdminPassword);
        userRepository.Add(User.CreateAdmin(seed.AdminEmail, seed.AdminUsername, passwordHash, seed.AdminName));
        await userRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded admin account {AdminEmail}.", seed.AdminEmail);
    }
}
