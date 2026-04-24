using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class ExternalLoginConfiguration : IEntityTypeConfiguration<ExternalLogin>
{
    public void Configure(EntityTypeBuilder<ExternalLogin> builder)
    {
        builder.ToTable("external_logins");

        builder.Property(x => x.UserId).IsRequired();

        builder.Property(x => x.Provider)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ProviderUserId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.Email)
            .HasMaxLength(255);

        builder.Property(x => x.DisplayName)
            .HasMaxLength(255);

        builder.Property(x => x.ProfilePictureUrl)
            .HasMaxLength(500);

        builder.HasIndex(x => new { x.Provider, x.ProviderUserId })
            .IsUnique();

        builder.HasOne(x => x.User)
            .WithMany(x => x.ExternalLogins)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
