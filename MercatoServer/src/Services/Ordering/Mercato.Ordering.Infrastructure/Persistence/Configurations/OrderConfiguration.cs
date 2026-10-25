using Mercato.Ordering.Domain.Orders;
using Mercato.Ordering.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mercato.Ordering.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", table =>
            table.HasCheckConstraint("ck_orders_total_amount", "total_amount >= 0"));

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(o => o.UserId).HasColumnName("user_id").IsRequired();
        // Stored by name: readable in the database and safe against enum reordering.
        builder.Property(o => o.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.TotalAmount).HasColumnName("total_amount").HasColumnType("numeric(14,0)").IsRequired();
        builder.Property(o => o.Receiver).HasColumnName("receiver").HasMaxLength(100).IsRequired();
        builder.Property(o => o.Phone).HasColumnName("phone").HasMaxLength(20).IsRequired();
        builder.Property(o => o.Address).HasColumnName("address").IsRequired();
        builder.Property(o => o.Note).HasColumnName("note");
        builder.Property(o => o.PaymentDeadline).HasColumnName("payment_deadline");
        builder.Property(o => o.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // xmin as an optimistic concurrency token: "pay", "cancel" and the expiry job can
        // all reach the same AwaitingPayment order at once. Each reads it, decides, and
        // writes — only the first write matches the xmin it read, the others fail instead
        // of silently overwriting (e.g. Expired on top of a just-Confirmed order).
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasIndex(o => o.UserId).HasDatabaseName("idx_orders_user_id");
        builder.HasIndex(o => o.CreatedAt).IsDescending().HasDatabaseName("idx_orders_created_at");

        // Partial index for the expiry scan: it only ever looks at orders awaiting
        // payment, a tiny slice of the table, ordered by deadline.
        builder.HasIndex(o => o.PaymentDeadline)
            .HasDatabaseName("idx_orders_expiry_scan")
            .HasFilter("status = 'AwaitingPayment'");

        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items", table =>
        {
            table.HasCheckConstraint("ck_order_items_quantity", "quantity > 0");
            table.HasCheckConstraint("ck_order_items_unit_price", "unit_price >= 0");
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(i => i.OrderId).HasColumnName("order_id").IsRequired();
        // Bare UUID of a Catalog variant; name and price are snapshots, not lookups.
        builder.Property(i => i.VariantId).HasColumnName("variant_id").IsRequired();
        builder.Property(i => i.ProductName).HasColumnName("product_name").HasMaxLength(255).IsRequired();
        builder.Property(i => i.VariantName).HasColumnName("variant_name").HasMaxLength(255).IsRequired();
        builder.Property(i => i.Quantity).HasColumnName("quantity").IsRequired();
        builder.Property(i => i.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(14,0)").IsRequired();
        builder.Property(i => i.Subtotal).HasColumnName("subtotal").HasColumnType("numeric(14,0)").IsRequired();
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(i => i.OrderId).HasDatabaseName("idx_order_items_order_id");
    }
}

internal sealed class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.ToTable("payment_transactions", table =>
            table.HasCheckConstraint("ck_payment_transactions_amount", "amount > 0"));

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(t => t.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(t => t.MethodCode).HasColumnName("method_code").HasMaxLength(50).IsRequired();
        builder.Property(t => t.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Amount).HasColumnName("amount").HasColumnType("numeric(14,0)").IsRequired();
        builder.Property(t => t.ProviderTransactionId).HasColumnName("provider_tx_id");
        builder.Property(t => t.FailureReason).HasColumnName("failure_reason");
        builder.Property(t => t.PaidAt).HasColumnName("paid_at");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(t => t.OrderId).HasDatabaseName("idx_payment_tx_order_id");

        // Restrict: an order with payment history must not be deletable out from under it.
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(t => t.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
