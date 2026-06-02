using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.Property(x => x.UserCode)
            .HasMaxLength(9);

        builder.HasIndex(x => x.UserCode)
            .IsUnique()
            .HasFilter("\"UserCode\" IS NOT NULL");

        builder.Property(x => x.FullName)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.DateOfBirth)
            .HasColumnType("date");

        builder.Property(x => x.PhoneNumber)
            .HasMaxLength(20);

        builder.Property(x => x.NormalizedPhoneNumber)
            .HasMaxLength(20);

        builder.Property(x => x.PhoneVerifiedAt);

        builder.HasIndex(x => x.NormalizedPhoneNumber)
            .IsUnique()
            .HasFilter("\"NormalizedPhoneNumber\" IS NOT NULL");

        builder.Property(x => x.Email)
            .HasMaxLength(255);

        builder.Property(x => x.NormalizedEmail)
            .HasMaxLength(255);

        builder.HasIndex(x => x.NormalizedEmail)
            .IsUnique()
            .HasFilter("\"NormalizedEmail\" IS NOT NULL");

        builder.Property(x => x.PasswordHash)
            .HasMaxLength(500);

        builder.Property(x => x.AvatarUrl)
            .HasMaxLength(2048);

        builder.Property(x => x.AvatarPublicId)
            .HasMaxLength(255);

        builder.Property(x => x.AvatarSource)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.HasOne(x => x.Role)
            .WithMany(x => x.Users)
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
