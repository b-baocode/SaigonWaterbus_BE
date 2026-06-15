using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class RouteSegmentConfiguration : IEntityTypeConfiguration<RouteSegment>
{
    public void Configure(EntityTypeBuilder<RouteSegment> builder)
    {
        builder.ToTable("route_segments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("route_segment_id");

        builder.Property(x => x.RouteId).HasColumnName("route_id").IsRequired();
        builder.Property(x => x.FromStationId).HasColumnName("from_station_id").IsRequired();
        builder.Property(x => x.ToStationId).HasColumnName("to_station_id").IsRequired();
        builder.Property(x => x.SegmentOrder).HasColumnName("segment_order").IsRequired();
        builder.Property(x => x.DistanceKm).HasColumnName("distance_km").HasColumnType("numeric(8,2)").IsRequired();
        builder.Property(x => x.EstimatedTravelMinutes).HasColumnName("estimated_travel_minutes").IsRequired();
        builder.Property(x => x.Geometry).HasColumnName("geometry").HasColumnType("geography(LineString,4326)");

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.RouteId, x.SegmentOrder }).IsUnique();
        builder.HasIndex(x => new { x.RouteId, x.FromStationId, x.ToStationId }).IsUnique();
        builder.HasIndex(x => x.Geometry).HasMethod("gist").HasDatabaseName("ix_route_segments_geometry");

        builder.HasOne(x => x.Route)
            .WithMany(x => x.RouteSegments)
            .HasForeignKey(x => x.RouteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.FromStation)
            .WithMany()
            .HasForeignKey(x => x.FromStationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ToStation)
            .WithMany()
            .HasForeignKey(x => x.ToStationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
