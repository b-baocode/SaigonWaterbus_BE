using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class CharterRouteDrawRequestConfiguration : IEntityTypeConfiguration<CharterRouteDrawRequest>
{
    public void Configure(EntityTypeBuilder<CharterRouteDrawRequest> builder)
    {
        builder.ToTable("charter_route_draw_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("request_id");
        builder.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
        builder.Property(x => x.CandidateRouteId).HasColumnName("candidate_route_id");
        builder.Property(x => x.ResultRouteId).HasColumnName("result_route_id");
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id");
        builder.Property(x => x.InProgressByUserId).HasColumnName("in_progress_by_user_id");
        builder.Property(x => x.InProgressAt).HasColumnName("in_progress_at");
        builder.Property(x => x.AcknowledgedAt).HasColumnName("acknowledged_at");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(1000);
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.BookingId, x.Status })
            .HasDatabaseName("ix_charter_route_draw_requests_booking_status");
        builder.HasIndex(x => x.CandidateRouteId);
        builder.HasIndex(x => x.ResultRouteId);
        builder.HasIndex(x => x.RequestedByUserId);
        builder.HasIndex(x => x.InProgressByUserId);
        builder.HasIndex(x => x.Created)
            .HasDatabaseName("ix_charter_route_draw_requests_created");
        builder.HasIndex(x => x.BookingId)
            .IsUnique()
            .HasDatabaseName("ux_charter_route_draw_requests_booking_open")
            .HasFilter("status IN ('Pending', 'InProgress')");

        builder.HasOne(x => x.Booking)
            .WithMany()
            .HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
        builder.HasOne(x => x.CandidateRoute)
            .WithMany()
            .HasForeignKey(x => x.CandidateRouteId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.ResultRoute)
            .WithMany()
            .HasForeignKey(x => x.ResultRouteId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.RequestedByUser)
            .WithMany()
            .HasForeignKey(x => x.RequestedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.InProgressByUser)
            .WithMany()
            .HasForeignKey(x => x.InProgressByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
