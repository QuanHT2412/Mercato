using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mercato.Catalog.Infrastructure.Persistence;

// Used only by `dotnet ef migrations` — supplies a throwaway connection string so the
// design-time tool can build the model without going through Aspire service discovery.
public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CatalogDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=catalog_db;Username=postgres;Password=design-time-only");

        return new CatalogDbContext(optionsBuilder.Options);
    }
}
