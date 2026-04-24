using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable("otp_challenges");

        builder.Property(x => x.Purpose)
            .HasConversion<int>();

        builder.Property(x => x.Email)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.CodeHash)
            .HasMaxLength(500)
            .IsRequired();

        builder.HasIndex(x => new { x.UserId, x.Purpose, x.Created });

        builder.HasOne(x => x.User)
            .WithMany(x => x.OtpChallenges)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
