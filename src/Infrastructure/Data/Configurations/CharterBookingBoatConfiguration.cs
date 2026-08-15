using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class CharterBookingBoatConfiguration : IEntityTypeConfiguration<CharterBookingBoat>
{
    public void Configure(EntityTypeBuilder<CharterBookingBoat> builder)
    {
        builder.ToTable("charter_booking_boats");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("charter_booking_boat_id");
        builder.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired();
        builder.Property(x => x.BoatId).HasColumnName("boat_id").IsRequired();
        builder.Property(x => x.TripId).HasColumnName("trip_id");
        builder.Property(x => x.BoatOrder).HasColumnName("boat_order").IsRequired();
        builder.Property(x => x.SeatSetupType)
            .HasColumnName("seat_setup_type")
            .HasConversion(
                v => v.ToString(),
                v => BoatConfigurationFallbacks.ParseSeatSetupType(v))
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.UnitPrice)
            .HasColumnName("unit_price")
            .HasColumnType("numeric(12,2)")
            .IsRequired();
        builder.Property(x => x.ChargeableDurationValue)
            .HasColumnName("chargeable_duration_value")
            .HasColumnType("numeric(12,3)")
            .IsRequired();
        builder.Property(x => x.SubtotalAmount)
            .HasColumnName("subtotal_amount")
            .HasColumnType("numeric(12,2)")
            .IsRequired();
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.BookingId, x.BoatOrder }).IsUnique();
        builder.HasIndex(x => new { x.BookingId, x.BoatId }).IsUnique();
        builder.HasIndex(x => x.BoatId);

        builder.HasOne(x => x.Booking)
            .WithMany(x => x.CharterBoats)
            .HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Boat)
            .WithMany()
            .HasForeignKey(x => x.BoatId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Trip)
            .WithMany()
            .HasForeignKey(x => x.TripId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => x.TripId);
    }
}
