using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class VesselImageConfiguration : IEntityTypeConfiguration<VesselImage>
{
    public void Configure(EntityTypeBuilder<VesselImage> builder)
    {
        builder.ToTable("vessel_images");

        builder.Property(x => x.Url)
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(x => x.PublicId)
            .HasMaxLength(255);

        builder.Property(x => x.DisplayOrder)
            .IsRequired();

        builder.Property(x => x.IsPrimary)
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasOne(x => x.Vessel)
            .WithMany(x => x.Images)
            .HasForeignKey(x => x.VesselId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.VesselId, x.DisplayOrder });

        builder.HasIndex(x => new { x.VesselId, x.IsPrimary })
            .IsUnique()
            .HasFilter("\"IsPrimary\" = TRUE");
    }
}
