using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class VesselDeckLayoutConfiguration : IEntityTypeConfiguration<VesselDeckLayout>
{
    public void Configure(EntityTypeBuilder<VesselDeckLayout> builder)
    {
        builder.ToTable("vessel_deck_layouts");

        builder.HasIndex(x => new { x.VesselId, x.DeckNumber })
            .IsUnique();

        builder.HasOne(x => x.Vessel)
            .WithMany(x => x.DeckLayouts)
            .HasForeignKey(x => x.VesselId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
