using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class TicketScanEventConfiguration : IEntityTypeConfiguration<TicketScanEvent>
{
    public void Configure(EntityTypeBuilder<TicketScanEvent> builder)
    {
        builder.ToTable("ticket_scan_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ticket_scan_event_id");

        builder.Property(x => x.TicketId).HasColumnName("ticket_id");
        builder.Property(x => x.BookingId).HasColumnName("booking_id");
        builder.Property(x => x.TripId).HasColumnName("trip_id");
        builder.Property(x => x.PerformedByUserId).HasColumnName("performed_by_user_id").IsRequired();
        builder.Property(x => x.StaffWorkAssignmentId).HasColumnName("staff_work_assignment_id");
        builder.Property(x => x.BoatId).HasColumnName("boat_id");
        builder.Property(x => x.StationId).HasColumnName("station_id");
        builder.Property(x => x.TripStopId).HasColumnName("trip_stop_id");
        builder.Property(x => x.Action)
            .HasColumnName("action")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(x => x.Result)
            .HasColumnName("result")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
        builder.Property(x => x.Source)
            .HasColumnName("source")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.ClientOperationId).HasColumnName("client_operation_id").HasMaxLength(100);
        builder.Property(x => x.DeviceTime).HasColumnName("device_time");
        builder.Property(x => x.ServerTime).HasColumnName("server_time").IsRequired();
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(x => x.ScannedCodeOrToken).HasColumnName("scanned_code_or_token").HasMaxLength(150);
        builder.Property(x => x.TicketStatusBefore)
            .HasColumnName("ticket_status_before")
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(x => x.TicketStatusAfter)
            .HasColumnName("ticket_status_after")
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => x.ServerTime);
        builder.HasIndex(x => new { x.PerformedByUserId, x.ServerTime });
        builder.HasIndex(x => new { x.TicketId, x.ServerTime });
        builder.HasIndex(x => new { x.TripId, x.ServerTime });
        builder.HasIndex(x => x.BookingId);
        builder.HasIndex(x => x.StaffWorkAssignmentId);
        builder.HasIndex(x => x.TripStopId);
        builder.HasIndex(x => x.ClientOperationId);

        builder.HasOne(x => x.Ticket)
            .WithMany()
            .HasForeignKey(x => x.TicketId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Booking)
            .WithMany()
            .HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Trip)
            .WithMany()
            .HasForeignKey(x => x.TripId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.PerformedByUser)
            .WithMany()
            .HasForeignKey(x => x.PerformedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.StaffWorkAssignment)
            .WithMany()
            .HasForeignKey(x => x.StaffWorkAssignmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Boat)
            .WithMany()
            .HasForeignKey(x => x.BoatId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Station)
            .WithMany()
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.TripStop)
            .WithMany()
            .HasForeignKey(x => x.TripStopId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
