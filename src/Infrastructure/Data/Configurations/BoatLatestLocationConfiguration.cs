using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class BoatLatestLocationConfiguration : IEntityTypeConfiguration<BoatLatestLocation>
{
    public void Configure(EntityTypeBuilder<BoatLatestLocation> builder)
    {
        builder.ToTable("boat_latest_locations");
        builder.HasKey(x => x.BoatId);

        builder.Property(x => x.BoatId).HasColumnName("boat_id");
        builder.Property(x => x.GpsDeviceId).HasColumnName("gps_device_id").IsRequired();
        builder.Property(x => x.RouteId).HasColumnName("route_id");
        builder.Property(x => x.TripId).HasColumnName("trip_id");
        builder.Property(x => x.CurrentStationId).HasColumnName("current_station_id");
        builder.Property(x => x.NextStationId).HasColumnName("next_station_id");
        builder.Property(x => x.Latitude).HasColumnName("latitude").HasColumnType("numeric(10,7)").IsRequired();
        builder.Property(x => x.Longitude).HasColumnName("longitude").HasColumnType("numeric(10,7)").IsRequired();
        builder.Property(x => x.RemainingDistanceKmToNextStation)
            .HasColumnName("remaining_distance_km_to_next_station")
            .HasColumnType("numeric(10,3)");
        builder.Property(x => x.RemainingMinutesToNextStation).HasColumnName("remaining_minutes_to_next_station");
        builder.Property(x => x.SpeedKmh).HasColumnName("speed_kmh").HasColumnType("numeric(6,2)");
        builder.Property(x => x.Heading).HasColumnName("heading");
        builder.Property(x => x.AccuracyMeters).HasColumnName("accuracy_meters").HasColumnType("numeric(8,2)");
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.ReceivedAt).HasColumnName("received_at").IsRequired();
        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(30);
        builder.Property(x => x.LiveAuthorityUntil).HasColumnName("live_authority_until");
        builder.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(30);
        builder.Property(x => x.BatteryPercent).HasColumnName("battery_percent");
        builder.Property(x => x.SignalStrength).HasColumnName("signal_strength");
        builder.Property(x => x.GpsFixQuality).HasColumnName("gps_fix_quality").HasMaxLength(30);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.GpsDeviceId);
        builder.HasIndex(x => x.RouteId);
        builder.HasIndex(x => x.TripId);
        builder.HasIndex(x => x.CurrentStationId);
        builder.HasIndex(x => x.NextStationId);
        builder.HasIndex(x => x.RecordedAt);
        builder.HasIndex(x => new { x.RouteId, x.Status });

        builder.HasOne(x => x.Boat)
            .WithOne()
            .HasForeignKey<BoatLatestLocation>(x => x.BoatId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne(x => x.GpsDevice)
            .WithMany()
            .HasForeignKey(x => x.GpsDeviceId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.Route)
            .WithMany()
            .HasForeignKey(x => x.RouteId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Trip)
            .WithMany()
            .HasForeignKey(x => x.TripId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.CurrentStation)
            .WithMany()
            .HasForeignKey(x => x.CurrentStationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.NextStation)
            .WithMany()
            .HasForeignKey(x => x.NextStationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
