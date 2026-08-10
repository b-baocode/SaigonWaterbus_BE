using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class UserPushTokenConfiguration : IEntityTypeConfiguration<UserPushToken>
{
    public void Configure(EntityTypeBuilder<UserPushToken> builder)
    {
        builder.ToTable("user_push_tokens");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("user_push_token_id");

        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.ExpoPushToken)
            .HasColumnName("expo_push_token")
            .HasMaxLength(255)
            .IsRequired();
        builder.Property(x => x.Platform)
            .HasColumnName("platform")
            .HasMaxLength(10)
            .HasConversion(
                v => v.ToString().ToLowerInvariant(),
                v => (Domain.Enums.PushPlatform)Enum.Parse(typeof(Domain.Enums.PushPlatform), v, true))
            .IsRequired();
        builder.Property(x => x.DeviceId).HasColumnName("device_id").HasMaxLength(255);
        builder.Property(x => x.AppVersion).HasColumnName("app_version").HasMaxLength(50);
        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .IsRequired()
            .HasDefaultValue(true);
        builder.Property(x => x.DisabledAt).HasColumnName("disabled_at");
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("NOW()");
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(x => x.ExpoPushToken).IsUnique()
            .HasDatabaseName("ux_user_push_tokens_token");
        builder.HasIndex(x => new { x.UserId, x.IsActive })
            .HasDatabaseName("ix_user_push_tokens_user_active");

        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
