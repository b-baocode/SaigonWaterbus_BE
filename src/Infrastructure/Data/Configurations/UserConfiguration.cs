using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("user_id");

        builder.Property(x => x.UserCode)
            .HasColumnName("user_code")
            .HasMaxLength(30);

        builder.HasIndex(x => x.UserCode)
            .IsUnique()
            .HasFilter("\"user_code\" IS NOT NULL");

        builder.Property(x => x.FullName)
            .HasColumnName("full_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.DateOfBirth)
            .HasColumnName("date_of_birth");

        builder.Property(x => x.Gender)
            .HasColumnName("gender")
            .HasMaxLength(30);

        builder.Property(x => x.Nationality)
            .HasColumnName("nationality")
            .HasMaxLength(100);

        builder.Property(x => x.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(30);

        builder.HasIndex(x => x.PhoneNumber)
            .IsUnique()
            .HasFilter("\"phone_number\" IS NOT NULL");

        builder.Property(x => x.NormalizedPhoneNumber)
            .HasColumnName("normalized_phone_number")
            .HasMaxLength(30);

        builder.HasIndex(x => x.NormalizedPhoneNumber)
            .IsUnique()
            .HasFilter("\"normalized_phone_number\" IS NOT NULL");

        builder.Property(x => x.PhoneVerifiedAt)
            .HasColumnName("phone_verified_at");

        builder.Property(x => x.Email)
            .HasColumnName("email")
            .HasMaxLength(255);

        builder.HasIndex(x => x.Email)
            .IsUnique()
            .HasFilter("\"email\" IS NOT NULL");

        builder.Property(x => x.NormalizedEmail)
            .HasColumnName("normalized_email")
            .HasMaxLength(255);

        builder.HasIndex(x => x.NormalizedEmail)
            .IsUnique()
            .HasFilter("\"normalized_email\" IS NOT NULL");

        builder.Property(x => x.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(500);

        builder.Property(x => x.RoleId)
            .HasColumnName("role_id");

        builder.Property(x => x.StaffType)
            .HasColumnName("staff_type")
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(x => x.AvatarUrl)
            .HasColumnName("avatar_url")
            .HasMaxLength(1000);

        builder.Property(x => x.AvatarPublicId)
            .HasColumnName("avatar_public_id")
            .HasMaxLength(500);

        builder.Property(x => x.AvatarSource)
            .HasColumnName("avatar_source")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.AvatarUpdatedAt)
            .HasColumnName("avatar_updated_at");

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.LastLoginAt)
            .HasColumnName("last_login_at");

        builder.Property(x => x.FailedLoginAttemptCount)
            .HasColumnName("failed_login_attempt_count")
            .IsRequired();

        builder.Property(x => x.FailedLoginWindowStartedAt)
            .HasColumnName("failed_login_window_started_at");

        builder.Property(x => x.PointBalance)
            .HasColumnName("point_balance")
            .IsRequired();


        builder.Property(x => x.Created)
            .HasColumnName("created_at");

        builder.Property<DateTimeOffset?>("UpdatedAt")
            .HasColumnName("updated_at");

        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasOne(x => x.Role)
            .WithMany(x => x.Users)
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
