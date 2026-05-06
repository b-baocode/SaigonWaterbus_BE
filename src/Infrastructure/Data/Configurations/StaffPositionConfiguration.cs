using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class StaffPositionConfiguration : IEntityTypeConfiguration<StaffPosition>
{
    public void Configure(EntityTypeBuilder<StaffPosition> builder)
    {
        builder.ToTable("staff_positions");

        builder.Property(x => x.Code)
            .HasMaxLength(10)
            .IsRequired();

        builder.HasIndex(x => x.Code).IsUnique();

        builder.Property(x => x.SystemName)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(x => x.SystemName).IsUnique();

        builder.Property(x => x.DisplayName)
            .HasMaxLength(100)
            .IsRequired();
    }
}
