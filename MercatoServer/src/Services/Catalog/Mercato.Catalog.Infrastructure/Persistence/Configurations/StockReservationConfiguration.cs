using Mercato.Catalog.Domain.Products;
using Mercato.Catalog.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mercato.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("stock_reservations", table =>
            table.HasCheckConstraint("ck_stock_reservations_quantity", "quantity > 0"));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(r => r.VariantId).HasColumnName("variant_id").IsRequired();
        // Bare UUID: orders live in ordering_db, no FK across services (ADR 0001).
        builder.Property(r => r.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(r => r.Quantity).HasColumnName("quantity").IsRequired();
        builder.Property(r => r.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();

        // One hold per (order, variant): the database-level backstop for reserve idempotency.
        builder.HasIndex(r => new { r.OrderId, r.VariantId }).IsUnique();
        builder.HasIndex(r => r.VariantId).HasDatabaseName("idx_reservations_variant");
        builder.HasIndex(r => r.ExpiresAt).HasDatabaseName("idx_reservations_expiry");

        builder.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(r => r.VariantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
