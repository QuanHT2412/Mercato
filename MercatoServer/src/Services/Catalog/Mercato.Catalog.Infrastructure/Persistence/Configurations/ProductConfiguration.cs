using Mercato.Catalog.Domain.Categories;
using Mercato.Catalog.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mercato.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(p => p.CategoryId).HasColumnName("category_id");
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(p => p.Description).HasColumnName("description");
        builder.Property(p => p.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(p => p.CategoryId).HasDatabaseName("idx_products_category_id");
        builder.HasIndex(p => p.IsActive).HasDatabaseName("idx_products_active").HasFilter("is_active = true");

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // The aggregate exposes Variants as a read-only collection; EF writes through the
        // private _variants field so the domain keeps control over how variants are added.
        builder.HasMany(p => p.Variants)
            .WithOne()
            .HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("product_variants", table =>
        {
            // Last line of defence behind the domain rules (CLAUDE.md section 6).
            table.HasCheckConstraint("ck_product_variants_price", "price >= 0");
            table.HasCheckConstraint("ck_product_variants_stock", "stock >= 0");
        });

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(v => v.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(v => v.Sku).HasColumnName("sku").HasMaxLength(100).IsRequired();
        builder.Property(v => v.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        // VND has no minor unit: 14 digits, zero decimals.
        builder.Property(v => v.Price).HasColumnName("price").HasColumnType("numeric(14,0)").IsRequired();
        builder.Property(v => v.Stock).HasColumnName("stock").IsRequired();
        builder.Property(v => v.ImageUrl).HasColumnName("image_url");
        builder.Property(v => v.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(v => v.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(v => v.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(v => v.Sku).IsUnique();
        builder.HasIndex(v => v.ProductId).HasDatabaseName("idx_variants_product_id");
    }
}
