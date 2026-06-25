using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class CustomBookingItineraryStopConfiguration : IEntityTypeConfiguration<CustomBookingItineraryStop>
{
    public void Configure(EntityTypeBuilder<CustomBookingItineraryStop> builder)
    {
        builder.ToTable("itinerary_stops");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("itinerary_stop_id");

        builder.Property(x => x.CustomBookingId).HasColumnName("custom_booking_id").IsRequired();
        builder.Property(x => x.StationId).HasColumnName("station_id").IsRequired();
        builder.Property(x => x.StopOrder).HasColumnName("stop_order").IsRequired();
        builder.Property(x => x.StayDurationMinutes).HasColumnName("stay_duration_minutes").IsRequired();
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.CustomBookingId, x.StopOrder }).IsUnique();
        builder.HasIndex(x => x.StationId);

        builder.HasOne(x => x.CustomBooking)
            .WithMany(x => x.ItineraryStops)
            .HasForeignKey(x => x.CustomBookingId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne(x => x.Station)
            .WithMany()
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
