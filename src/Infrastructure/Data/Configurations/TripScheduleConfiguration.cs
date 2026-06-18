using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class TripScheduleConfiguration : IEntityTypeConfiguration<TripSchedule>
{
    public void Configure(EntityTypeBuilder<TripSchedule> builder)
    {
        builder.ToTable("trip_schedules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("trip_schedule_id");

        builder.Property(x => x.RouteId).HasColumnName("route_id").IsRequired();
        builder.Property(x => x.VesselId).HasColumnName("vessel_id").IsRequired();

        builder.Property(x => x.DepartureTime).HasColumnName("departure_time").IsRequired();

        builder.Property(x => x.RecurrenceType)
            .HasColumnName("recurrence_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.WeeklyDays).HasColumnName("weekly_days").HasMaxLength(20);
        builder.Property(x => x.MonthlyDay).HasColumnName("monthly_day");
        builder.Property(x => x.HorizonDays).HasColumnName("horizon_days").IsRequired().HasDefaultValue(30);

        builder.Property(x => x.ValidFrom).HasColumnName("valid_from").HasColumnType("date").IsRequired();
        builder.Property(x => x.ValidTo).HasColumnName("valid_to").HasColumnType("date");

        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired().HasDefaultValue(true);

        builder.HasOne(x => x.Route)
            .WithMany()
            .HasForeignKey(x => x.RouteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Vessel)
            .WithMany()
            .HasForeignKey(x => x.VesselId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);
    }
}
