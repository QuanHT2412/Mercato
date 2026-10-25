using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mercato.Ordering.Infrastructure.Persistence;

// Used only by `dotnet ef migrations` — supplies a throwaway connection string so the
// design-time tool can build the model without going through Aspire service discovery.
public sealed class OrderingDbContextFactory : IDesignTimeDbContextFactory<OrderingDbContext>
{
    public OrderingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OrderingDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=ordering_db;Username=postgres;Password=design-time-only");

        return new OrderingDbContext(optionsBuilder.Options);
    }
}
