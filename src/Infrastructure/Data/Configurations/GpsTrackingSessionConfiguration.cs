using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class GpsTrackingSessionConfiguration : IEntityTypeConfiguration<GpsTrackingSession>
{
    public void Configure(EntityTypeBuilder<GpsTrackingSession> builder)
    {
        builder.ToTable("gps_tracking_sessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("gps_tracking_session_id");

        builder.Property(x => x.GpsDeviceId).HasColumnName("gps_device_id").IsRequired();
        builder.Property(x => x.BoatId).HasColumnName("boat_id").IsRequired();
        builder.Property(x => x.RouteId).HasColumnName("route_id");
        builder.Property(x => x.RouteCode).HasColumnName("route_code").HasMaxLength(50);
        builder.Property(x => x.RouteName).HasColumnName("route_name").HasMaxLength(150);
        builder.Property(x => x.PlannedLengthMeters).HasColumnName("planned_length_meters").HasColumnType("numeric(10,2)");
        builder.Property(x => x.TripId).HasColumnName("trip_id");
        builder.Property(x => x.StartStationId).HasColumnName("start_station_id");
        builder.Property(x => x.EndStationId).HasColumnName("end_station_id");
        builder.Property(x => x.Mode).HasColumnName("mode").HasMaxLength(30).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
        builder.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.StoppedAt).HasColumnName("stopped_at");

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.GpsDeviceId, x.Status });
        builder.HasIndex(x => new { x.BoatId, x.StartedAt });
        builder.HasIndex(x => x.RouteId);
        builder.HasIndex(x => x.RouteCode);
        builder.HasIndex(x => x.TripId);

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

        builder.HasOne(x => x.StartStation)
            .WithMany()
            .HasForeignKey(x => x.StartStationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.EndStation)
            .WithMany()
            .HasForeignKey(x => x.EndStationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
