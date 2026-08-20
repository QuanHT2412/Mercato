using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mercato.Identity.Infrastructure.Persistence;

// Used only by `dotnet ef migrations` — supplies a throwaway connection string so the
// design-time tool can build the model without going through Aspire service discovery.
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=identity_db;Username=postgres;Password=design-time-only");

        return new IdentityDbContext(optionsBuilder.Options);
    }
}
