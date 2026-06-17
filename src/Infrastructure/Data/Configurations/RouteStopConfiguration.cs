using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class RouteStopConfiguration : IEntityTypeConfiguration<RouteStop>
{
    public void Configure(EntityTypeBuilder<RouteStop> builder)
    {
        builder.ToTable("route_stops");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("route_stop_id");

        builder.Property(x => x.RouteId).HasColumnName("route_id").IsRequired();
        builder.Property(x => x.StationId).HasColumnName("station_id").IsRequired();
        builder.Property(x => x.StopOrder).HasColumnName("stop_order").IsRequired();
        builder.Property(x => x.StandardTravelMin).HasColumnName("standard_travel_min");
        builder.Property(x => x.StandardDwellMin).HasColumnName("standard_dwell_min");
        builder.Property(x => x.IsPickupAllowed).HasColumnName("is_pickup_allowed").IsRequired();
        builder.Property(x => x.IsDropoffAllowed).HasColumnName("is_dropoff_allowed").IsRequired();

        builder.HasIndex(x => new { x.RouteId, x.StopOrder }).IsUnique();

        builder.HasOne(x => x.Route).WithMany(r => r.RouteStops).HasForeignKey(x => x.RouteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Station).WithMany(s => s.RouteStops).HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(x => x.Created);
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);
    }
}
