using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> builder)
    {
        builder.ToTable("seats");

        builder.Property(x => x.Code)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Row)
            .HasMaxLength(5)
            .IsRequired();

        builder.HasIndex(x => new { x.VesselId, x.Code })
            .IsUnique();

        builder.HasIndex(x => new { x.VesselId, x.Deck, x.Row, x.Column })
            .IsUnique();

        builder.HasOne(x => x.Vessel)
            .WithMany(x => x.Seats)
            .HasForeignKey(x => x.VesselId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
