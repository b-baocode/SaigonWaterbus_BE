using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class OperationScheduleEntryConfiguration : IEntityTypeConfiguration<OperationScheduleEntry>
{
    public void Configure(EntityTypeBuilder<OperationScheduleEntry> builder)
    {
        builder.ToTable("operation_schedule_entries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("operation_schedule_entry_id");

        builder.Property(x => x.SourceType)
            .HasColumnName("source_type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.SourceId).HasColumnName("source_id").IsRequired();
        builder.Property(x => x.SourceCode).HasColumnName("source_code").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();

        builder.Property(x => x.ServiceId).HasColumnName("service_id");
        builder.Property(x => x.ServiceCode).HasColumnName("service_code").HasMaxLength(50);
        builder.Property(x => x.ServiceName).HasColumnName("service_name").HasMaxLength(150);
        builder.Property(x => x.BookingMode)
            .HasColumnName("booking_mode")
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(x => x.VesselId).HasColumnName("vessel_id");
        builder.Property(x => x.VesselCode).HasColumnName("vessel_code").HasMaxLength(50);
        builder.Property(x => x.VesselName).HasColumnName("vessel_name").HasMaxLength(150);

        builder.Property(x => x.RouteId).HasColumnName("route_id");
        builder.Property(x => x.RouteCode).HasColumnName("route_code").HasMaxLength(50);
        builder.Property(x => x.RouteName).HasColumnName("route_name").HasMaxLength(150);

        builder.Property(x => x.FromStationId).HasColumnName("from_station_id");
        builder.Property(x => x.FromStationCode).HasColumnName("from_station_code").HasMaxLength(50);
        builder.Property(x => x.FromLocation).HasColumnName("from_location").HasMaxLength(200).IsRequired();
        builder.Property(x => x.ToStationId).HasColumnName("to_station_id");
        builder.Property(x => x.ToStationCode).HasColumnName("to_station_code").HasMaxLength(50);
        builder.Property(x => x.ToLocation).HasColumnName("to_location").HasMaxLength(200).IsRequired();

        builder.Property(x => x.OperatingDate).HasColumnName("operating_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.StartAt).HasColumnName("start_at").IsRequired();
        builder.Property(x => x.EndAt).HasColumnName("end_at").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
        builder.Property(x => x.ScheduleState).HasColumnName("schedule_state").HasMaxLength(50).IsRequired();
        builder.Property(x => x.OperationStatus).HasColumnName("operation_status").HasMaxLength(50).IsRequired();
        builder.Property(x => x.PaymentStage).HasColumnName("payment_stage").HasMaxLength(50);
        builder.Property(x => x.RemainingPaymentDeadline).HasColumnName("remaining_payment_deadline");
        builder.Property(x => x.IsPaymentOverdue).HasColumnName("is_payment_overdue").IsRequired();
        builder.Property(x => x.DelayMinutes).HasColumnName("delay_minutes").IsRequired();
        builder.Property(x => x.DelayReason).HasColumnName("delay_reason").HasMaxLength(500);
        builder.Property(x => x.AdjustedStartAt).HasColumnName("adjusted_start_at");
        builder.Property(x => x.AdjustedEndAt).HasColumnName("adjusted_end_at");
        builder.Property(x => x.ActualStartAt).HasColumnName("actual_start_at");
        builder.Property(x => x.ActualEndAt).HasColumnName("actual_end_at");
        builder.Property(x => x.CustomerUserId).HasColumnName("customer_user_id");
        builder.Property(x => x.AssignedManagerUserId).HasColumnName("assigned_manager_user_id");
        builder.Property(x => x.SyncedAt).HasColumnName("synced_at").IsRequired();

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.SourceType, x.SourceId }).IsUnique();
        builder.HasIndex(x => new { x.OperatingDate, x.StartAt });
        builder.HasIndex(x => new { x.StartAt, x.EndAt });
        builder.HasIndex(x => new { x.AdjustedStartAt, x.AdjustedEndAt });
        builder.HasIndex(x => new { x.VesselId, x.StartAt });
        builder.HasIndex(x => x.OperationStatus);
        builder.HasIndex(x => x.ServiceId);
        builder.HasIndex(x => x.RouteId);
        builder.HasIndex(x => x.FromStationId);
        builder.HasIndex(x => x.ToStationId);
        builder.HasIndex(x => x.CustomerUserId);
        builder.HasIndex(x => x.AssignedManagerUserId);

        builder.HasOne<WaterbusService>()
            .WithMany()
            .HasForeignKey(x => x.ServiceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Vessel>()
            .WithMany()
            .HasForeignKey(x => x.VesselId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Domain.Entities.Route>()
            .WithMany()
            .HasForeignKey(x => x.RouteId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Station>()
            .WithMany()
            .HasForeignKey(x => x.FromStationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Station>()
            .WithMany()
            .HasForeignKey(x => x.ToStationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CustomerUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.AssignedManagerUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
