using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class GeneratedTripConfiguration : IEntityTypeConfiguration<GeneratedTrip>
{
    public void Configure(EntityTypeBuilder<GeneratedTrip> builder)
    {
        builder.ToTable("generated_trips");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("generated_trip_id");

        builder.Property(x => x.TripScheduleId).HasColumnName("trip_schedule_id").IsRequired();
        builder.Property(x => x.TripId).HasColumnName("trip_id").IsRequired();
        builder.Property(x => x.OperatingDate).HasColumnName("operating_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.GeneratedAt).HasColumnName("generated_at").IsRequired();

        // Idempotency: không sinh trùng trip cho cùng lịch + cùng ngày
        builder.HasIndex(x => new { x.TripScheduleId, x.OperatingDate }).IsUnique();

        builder.HasOne(x => x.TripSchedule)
            .WithMany(s => s.GeneratedTrips)
            .HasForeignKey(x => x.TripScheduleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Trip)
            .WithOne(t => t.GeneratedTrip)
            .HasForeignKey<GeneratedTrip>(x => x.TripId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
