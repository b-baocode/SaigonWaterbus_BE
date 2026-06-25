using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> builder)
    {
        builder.ToTable("seats");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("seat_id");

        builder.Property(x => x.VesselId)
            .HasColumnName("vessel_id");

        builder.Property(x => x.Code)
            .HasColumnName("seat_code")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.SeatTypeCode)
            .HasColumnName("seat_type_code")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.SeatTypeName)
            .HasColumnName("seat_type")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Deck)
            .HasColumnName("deck_number");

        builder.Property(x => x.Row)
            .HasColumnName("seat_row")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(x => x.Column)
            .HasColumnName("seat_column");

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active");

        builder.Ignore(x => x.Created);
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

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
