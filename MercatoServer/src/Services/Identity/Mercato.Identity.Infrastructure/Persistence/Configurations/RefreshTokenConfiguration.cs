using Mercato.Identity.Domain.Tokens;
using Mercato.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mercato.Identity.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(t => t.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(t => t.TokenHash).HasColumnName("token_hash").IsRequired();
        builder.Property(t => t.FamilyId).HasColumnName("family_id").IsRequired();
        builder.Property(t => t.ReplacedBy).HasColumnName("replaced_by");
        builder.Property(t => t.DeviceInfo).HasColumnName("device_info");
        builder.Property(t => t.IpAddress).HasColumnName("ip_address").HasColumnType("inet");
        builder.Property(t => t.IsRevoked).HasColumnName("is_revoked").IsRequired();
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.RotatedAt).HasColumnName("rotated_at");
        builder.Property(t => t.FamilyExpiresAt).HasColumnName("family_expires_at").IsRequired();

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.FamilyId).HasDatabaseName("idx_refresh_tokens_family");
        builder.HasIndex(t => new { t.UserId, t.ExpiresAt })
            .HasDatabaseName("idx_refresh_tokens_active")
            .HasFilter("is_revoked = false");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
