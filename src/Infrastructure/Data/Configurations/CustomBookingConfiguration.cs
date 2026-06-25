using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;


public sealed class CustomBookingConfiguration : IEntityTypeConfiguration<CustomBooking>
{
    public void Configure(EntityTypeBuilder<CustomBooking> builder)
    {
        builder.Property(x => x.VesselId).HasColumnName("vessel_id");
        builder.Property(x => x.FromStationId).HasColumnName("custom_from_station_id");
        builder.Property(x => x.ToStationId).HasColumnName("custom_to_station_id");
        builder.Property(x => x.DepartureDate).HasColumnName("departure_date");
        builder.Property(x => x.StartTime).HasColumnName("start_time");
        builder.Property(x => x.RentalUnit)
            .HasColumnName("rental_unit")
            .HasConversion<string>()
            .HasMaxLength(10);
        builder.Property(x => x.DurationValue).HasColumnName("duration_value");
        builder.Property(x => x.PassengerCount).HasColumnName("passenger_count");
        builder.Property(x => x.AdultCount).HasColumnName("adult_count");
        builder.Property(x => x.ChildCount).HasColumnName("child_count");
        builder.Property(x => x.PreferredNumberOfDecks).HasColumnName("preferred_number_of_decks");
        builder.Property(x => x.PreferredSeatSetupType)
            .HasColumnName("preferred_seat_setup_type")
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.VesselRequirements).HasColumnName("vessel_requirements").HasMaxLength(1000);
        builder.Property(x => x.SpecialRequests).HasColumnName("special_requests").HasMaxLength(1000);
        builder.Property(x => x.HoldExpiresAt).HasColumnName("hold_expires_at");

        builder.HasIndex(x => new { x.VesselId, x.DepartureDate })
            .HasDatabaseName("ux_bookings_vessel_date_active")
            .IsUnique()
            .HasFilter("booking_type = 'CustomBooking' AND status IN ('Quoted', 'Confirmed')");

        builder.HasOne(x => x.Vessel).WithMany().HasForeignKey(x => x.VesselId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.FromStation).WithMany().HasForeignKey(x => x.FromStationId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.ToStation).WithMany().HasForeignKey(x => x.ToStationId).OnDelete(DeleteBehavior.SetNull);
    }
}
