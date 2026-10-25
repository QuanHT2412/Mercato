using Mercato.Ordering.Domain.Carts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mercato.Ordering.Infrastructure.Persistence.Configurations;

internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("carts");

        // One cart per user: the user id IS the key. A bare UUID — users live in
        // identity_db, no FK across services (ADR 0001).
        builder.HasKey(c => c.UserId);
        builder.Property(c => c.UserId).HasColumnName("user_id").ValueGeneratedNever();
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Ignore(c => c.IsEmpty);

        // Required relationship: an item taken out of the collection is an orphan and EF
        // deletes it, which is exactly what Cart.RemoveItem / Cart.Clear mean.
        builder.HasMany(c => c.Items)
            .WithOne()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("cart_items", table =>
            // Safety net behind Cart.SetItem (CLAUDE.md section 6: quantity per line 1–10).
            table.HasCheckConstraint("ck_cart_items_quantity", "quantity BETWEEN 1 AND 10"));

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(i => i.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(i => i.VariantId).HasColumnName("variant_id").IsRequired();
        builder.Property(i => i.Quantity).HasColumnName("quantity").IsRequired();
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // A variant appears at most once per cart; also covers lookups by user.
        builder.HasIndex(i => new { i.UserId, i.VariantId }).IsUnique();
    }
}
