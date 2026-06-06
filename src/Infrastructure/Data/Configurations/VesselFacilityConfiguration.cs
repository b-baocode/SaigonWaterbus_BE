using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class VesselFacilityConfiguration : IEntityTypeConfiguration<VesselFacility>
{
    public void Configure(EntityTypeBuilder<VesselFacility> builder)
    {
        builder.ToTable("vessel_facilities");

        builder.Property(x => x.Type)
            .IsRequired();

        builder.Property(x => x.Row)
            .HasMaxLength(5)
            .IsRequired();

        builder.HasIndex(x => new { x.VesselId, x.Deck, x.Row, x.Column });

        builder.HasOne(x => x.Vessel)
            .WithMany(x => x.Facilities)
            .HasForeignKey(x => x.VesselId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
