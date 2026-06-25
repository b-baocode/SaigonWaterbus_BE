using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable("otp_challenges");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("otp_challenge_id");

        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.Purpose).HasColumnName("purpose").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Channel).HasColumnName("channel").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Email).HasColumnName("destination").HasMaxLength(255).IsRequired();
        builder.Property(x => x.PendingPhoneNumber).HasColumnName("pending_value").HasMaxLength(255);
        builder.Property(x => x.CodeHash).HasColumnName("code_hash").HasMaxLength(500).IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.ResendAvailableAt).HasColumnName("resend_available_at").IsRequired();
        builder.Property(x => x.ConsumedAt).HasColumnName("consumed_at");
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(x => x.MaxAttempts).HasColumnName("max_attempts").IsRequired();
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.UserId, x.Purpose, x.ConsumedAt });
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasOne(x => x.User).WithMany(x => x.OtpChallenges).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
