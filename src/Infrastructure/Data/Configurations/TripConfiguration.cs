using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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
        builder.Property(x => x.TripCode).HasColumnName("trip_code").HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.TripCode).IsUnique();

        builder.Property(x => x.OperatingDate).HasColumnName("operating_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.DepartureTime).HasColumnName("departure_time").IsRequired();
        builder.Property(x => x.ArrivalTime).HasColumnName("arrival_time").IsRequired();
        builder.Property(x => x.CapacitySnapshot).HasColumnName("capacity_snapshot").IsRequired();
        builder.Property(x => x.TripStatus).HasColumnName("trip_status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.StatusNote).HasColumnName("status_note");

        builder.Property(x => x.VesselId).HasColumnName("vessel_id");

        builder.HasIndex(x => new { x.RouteId, x.OperatingDate });

        builder.HasOne(x => x.Route).WithMany(r => r.Trips).HasForeignKey(x => x.RouteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Vessel).WithMany().HasForeignKey(x => x.VesselId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);
    }
}
