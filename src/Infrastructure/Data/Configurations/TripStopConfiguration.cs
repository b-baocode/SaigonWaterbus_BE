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
        builder.Property(x => x.RouteStopId).HasColumnName("route_stop_id").IsRequired();
        builder.Property(x => x.StopOrder).HasColumnName("stop_order").IsRequired();
        builder.Property(x => x.ScheduledArrival).HasColumnName("scheduled_arrival");
        builder.Property(x => x.ScheduledDeparture).HasColumnName("scheduled_departure");
        builder.Property(x => x.ActualArrival).HasColumnName("actual_arrival");
        builder.Property(x => x.ActualDeparture).HasColumnName("actual_departure");
        builder.Property(x => x.StopStatus).HasColumnName("stop_status").HasMaxLength(20).IsRequired();

        builder.HasIndex(x => new { x.TripId, x.StopOrder }).IsUnique();

        builder.HasOne(x => x.Trip).WithMany(t => t.TripStops).HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.RouteStop).WithMany(rs => rs.TripStops).HasForeignKey(x => x.RouteStopId).OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(x => x.Created);
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);
    }
}
