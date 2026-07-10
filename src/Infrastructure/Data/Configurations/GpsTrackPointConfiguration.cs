using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class GpsTrackPointConfiguration : IEntityTypeConfiguration<GpsTrackPoint>
{
    public void Configure(EntityTypeBuilder<GpsTrackPoint> builder)
    {
        builder.ToTable("gps_track_points");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("gps_track_point_id");

        builder.Property(x => x.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(x => x.GpsDeviceId).HasColumnName("gps_device_id").IsRequired();
        builder.Property(x => x.BoatId).HasColumnName("boat_id").IsRequired();
        builder.Property(x => x.RouteId).HasColumnName("route_id");
        builder.Property(x => x.TripId).HasColumnName("trip_id");
        builder.Property(x => x.MessageId).HasColumnName("message_id").IsRequired();
        builder.Property(x => x.Latitude).HasColumnName("latitude").HasColumnType("numeric(10,7)").IsRequired();
        builder.Property(x => x.Longitude).HasColumnName("longitude").HasColumnType("numeric(10,7)").IsRequired();
        builder.Property(x => x.SpeedKmh).HasColumnName("speed_kmh").HasColumnType("numeric(6,2)");
        builder.Property(x => x.Heading).HasColumnName("heading");
        builder.Property(x => x.AccuracyMeters).HasColumnName("accuracy_meters").HasColumnType("numeric(8,2)");
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").IsRequired();
        builder.Property(x => x.ReceivedAt).HasColumnName("received_at").IsRequired();
        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
        builder.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(30);
        builder.Property(x => x.BatteryPercent).HasColumnName("battery_percent");
        builder.Property(x => x.SignalStrength).HasColumnName("signal_strength");
        builder.Property(x => x.GpsFixQuality).HasColumnName("gps_fix_quality").HasMaxLength(30);

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.SessionId, x.Sequence }).IsUnique();
        builder.HasIndex(x => new { x.GpsDeviceId, x.MessageId }).IsUnique();
        builder.HasIndex(x => new { x.SessionId, x.RecordedAt });
        builder.HasIndex(x => new { x.BoatId, x.RecordedAt });
        builder.HasIndex(x => x.RouteId);
        builder.HasIndex(x => x.TripId);

        builder.HasOne(x => x.Session)
            .WithMany(x => x.TrackPoints)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne(x => x.GpsDevice)
            .WithMany()
            .HasForeignKey(x => x.GpsDeviceId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.Boat)
            .WithMany()
            .HasForeignKey(x => x.BoatId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne(x => x.Route)
            .WithMany()
            .HasForeignKey(x => x.RouteId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Trip)
            .WithMany()
            .HasForeignKey(x => x.TripId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
