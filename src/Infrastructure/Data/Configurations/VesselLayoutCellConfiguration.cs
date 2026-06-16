using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class VesselLayoutCellConfiguration : IEntityTypeConfiguration<VesselLayoutCell>
{
    public void Configure(EntityTypeBuilder<VesselLayoutCell> builder)
    {
        builder.ToTable("vessel_layout_cells");

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Row)
            .HasMaxLength(5)
            .IsRequired();

        builder.HasIndex(x => new { x.VesselId, x.Deck, x.Row, x.Column })
            .IsUnique();

        builder.HasOne(x => x.Vessel)
            .WithMany(x => x.LayoutCells)
            .HasForeignKey(x => x.VesselId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
