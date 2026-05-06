using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class UserPositionConfiguration : IEntityTypeConfiguration<UserPosition>
{
    public void Configure(EntityTypeBuilder<UserPosition> builder)
    {
        builder.ToTable("user_positions");

        builder.Property(x => x.AssignedAt).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();

        builder.HasIndex(x => new { x.UserId, x.PositionId })
            .HasFilter("\"IsActive\" = true AND \"StationId\" IS NULL")
            .IsUnique();

        builder.HasIndex(x => new { x.UserId, x.PositionId, x.StationId })
            .HasFilter("\"IsActive\" = true AND \"StationId\" IS NOT NULL")
            .IsUnique();

        builder.HasIndex(x => x.StationId)
            .HasFilter("\"StationId\" IS NOT NULL");

        builder.HasOne(x => x.User)
            .WithMany(x => x.UserPositions)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Position)
            .WithMany(x => x.UserPositions)
            .HasForeignKey(x => x.PositionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AssignedByUser)
            .WithMany(x => x.AssignedUserPositions)
            .HasForeignKey(x => x.AssignedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
