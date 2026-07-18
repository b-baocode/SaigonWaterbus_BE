using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class TripStopConfiguration : IEntityTypeConfiguration<TripStop>
{
    public void Configure(EntityTypeBuilder<TripStop> builder)
    {
        builder.ToTable("trip_stops");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("trip_stop_id");
        builder.Property(x => x.TripId).HasColumnName("trip_id").IsRequired();
        builder.Property(x => x.StationId).HasColumnName("station_id").IsRequired();
        builder.Property(x => x.StopOrder).HasColumnName("stop_order").IsRequired();
        builder.Property(x => x.StayDurationMinutes)
            .HasColumnName("stay_duration_minutes")
            .HasDefaultValue(0)
            .IsRequired();
        builder.Property(x => x.PlannedArrivalTime).HasColumnName("planned_arrival_time");
        builder.Property(x => x.PlannedDepartureTime).HasColumnName("planned_departure_time");
        builder.Property(x => x.ActualArrivalTime).HasColumnName("actual_arrival_time");
        builder.Property(x => x.ActualDepartureTime).HasColumnName("actual_departure_time");
        builder.Property(x => x.StopStatus)
            .HasColumnName("stop_status")
            .HasMaxLength(30)
            .HasDefaultValue("Scheduled")
            .IsRequired();
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.TripId, x.StopOrder }).IsUnique();
        builder.HasIndex(x => x.StationId);

        builder.HasOne(x => x.Trip)
            .WithMany(x => x.TripStops)
            .HasForeignKey(x => x.TripId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Station)
            .WithMany()
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
