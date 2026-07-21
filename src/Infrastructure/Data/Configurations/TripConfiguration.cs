using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.ToTable("trips");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("trip_id");

        builder.Property(x => x.RouteId).HasColumnName("route_id").IsRequired();
        builder.Property(x => x.BoatId).HasColumnName("boat_id");
        builder.Property(x => x.TripCode).HasColumnName("trip_code").HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.TripCode).IsUnique();
        builder.Property(x => x.TripType)
            .HasColumnName("trip_type")
            .HasMaxLength(30)
            .HasDefaultValue(TripTypes.Regular)
            .IsRequired();
        builder.Property(x => x.SourceBookingId).HasColumnName("source_booking_id");
        builder.HasIndex(x => x.SourceBookingId);

        builder.Property(x => x.OperatingDate).HasColumnName("operating_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.ServicePeriod).HasColumnName("service_period").HasMaxLength(30);
        builder.Property(x => x.DepartureTime).HasColumnName("departure_time").IsRequired();
        builder.Property(x => x.ArrivalTime).HasColumnName("arrival_time").IsRequired();
        builder.Property(x => x.DelayMinutes)
            .HasColumnName("delay_minutes")
            .HasDefaultValue(0);
        builder.Property(x => x.DelayReason).HasColumnName("delay_reason").HasMaxLength(500);
        builder.Property(x => x.AdjustedDepartureTime).HasColumnName("adjusted_departure_time");
        builder.Property(x => x.AdjustedArrivalTime).HasColumnName("adjusted_arrival_time");
        builder.Property(x => x.CapacitySnapshot).HasColumnName("capacity").IsRequired();
        builder.Property(x => x.TripStatus)
            .HasColumnName("status")
            .HasConversion(
                value => ToDatabaseTripStatus(value),
                value => FromDatabaseTripStatus(value))
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.StatusNote).HasColumnName("status_note").HasMaxLength(500);

        builder.HasIndex(x => new { x.RouteId, x.OperatingDate });
        builder.HasIndex(x => new { x.BoatId, x.OperatingDate, x.TripStatus })
            .HasDatabaseName("ix_trips_boat_operating_date_status");
        builder.HasIndex(x => new { x.TripStatus, x.OperatingDate })
            .HasDatabaseName("ix_trips_status_operating_date");

        builder.HasOne(x => x.Route).WithMany(r => r.Trips).HasForeignKey(x => x.RouteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Boat).WithMany().HasForeignKey(x => x.BoatId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Booking>().WithMany().HasForeignKey(x => x.SourceBookingId).OnDelete(DeleteBehavior.SetNull);

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);
    }

    private static string ToDatabaseTripStatus(TripStatus status) =>
        status switch
        {
            TripStatus.InProgress => "Departed",
            TripStatus.Completed => "Arrived",
            _ => status.ToString()
        };

    private static TripStatus FromDatabaseTripStatus(string status) =>
        status switch
        {
            "Departed" => TripStatus.InProgress,
            "Arrived" => TripStatus.Completed,
            _ => Enum.Parse<TripStatus>(status, true)
        };
}
