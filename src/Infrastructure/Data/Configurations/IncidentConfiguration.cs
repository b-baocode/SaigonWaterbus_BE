using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("incidents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("incident_id");

        builder.Property(x => x.BoatId).HasColumnName("boat_id").IsRequired();
        builder.Property(x => x.TripId).HasColumnName("trip_id");
        builder.Property(x => x.ReportedBy).HasColumnName("reported_by_user_id");
        builder.Property(x => x.IncidentType).HasColumnName("incident_type").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(30);
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.ResolutionStatus).HasColumnName("resolution_status").HasMaxLength(30).IsRequired();
        builder.Property(x => x.AssignedManagerId).HasColumnName("assigned_manager_id");
        builder.Property(x => x.AssignedAt).HasColumnName("assigned_at");
        builder.Property(x => x.AssignedByUserId).HasColumnName("assigned_by_user_id");
        builder.Property(x => x.RescueBoatId).HasColumnName("rescue_boat_id");
        builder.Property(x => x.RescueDispatchedAt).HasColumnName("rescue_dispatched_at");
        builder.Property(x => x.RescueDispatchedByUserId).HasColumnName("rescue_dispatched_by_user_id");
        builder.Property(x => x.ReplacementBoatId).HasColumnName("replacement_boat_id");
        builder.Property(x => x.ReplacementAssignedAt).HasColumnName("replacement_assigned_at");
        builder.Property(x => x.ReplacementAssignedByUserId).HasColumnName("replacement_assigned_by_user_id");
        builder.Property(x => x.ReplacementMissionType)
            .HasColumnName("replacement_mission_type")
            .HasMaxLength(50)
            .HasDefaultValue("None")
            .IsRequired();
        builder.Property(x => x.ReplacementTargetStationId).HasColumnName("replacement_target_station_id");
        builder.Property(x => x.ReplacementTargetStopOrder).HasColumnName("replacement_target_stop_order");
        builder.Property(x => x.ActiveTicketCountSnapshot)
            .HasColumnName("active_ticket_count_snapshot")
            .HasDefaultValue(0);
        builder.Property(x => x.OnboardPassengerCountSnapshot)
            .HasColumnName("onboard_passenger_count_snapshot")
            .HasDefaultValue(0);
        builder.Property(x => x.FuturePassengerCountSnapshot)
            .HasColumnName("future_passenger_count_snapshot")
            .HasDefaultValue(0);
        builder.Property(x => x.ReplacementNote).HasColumnName("replacement_note").HasMaxLength(1000);
        builder.Property(x => x.ResolutionNote).HasColumnName("resolution_note").HasMaxLength(1000);
        builder.Property(x => x.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(x => x.ResolvedByUserId).HasColumnName("resolved_by_user_id");

        builder.HasIndex(x => new { x.BoatId, x.ResolutionStatus });
        builder.HasIndex(x => x.TripId);
        builder.HasIndex(x => x.ReportedBy);
        builder.HasIndex(x => x.AssignedManagerId);
        builder.HasIndex(x => x.AssignedByUserId);
        builder.HasIndex(x => x.RescueBoatId);
        builder.HasIndex(x => x.RescueDispatchedByUserId);
        builder.HasIndex(x => x.ReplacementBoatId);
        builder.HasIndex(x => x.ReplacementAssignedByUserId);
        builder.HasIndex(x => x.ReplacementTargetStationId);
        builder.HasIndex(x => x.ResolvedByUserId);

        builder.HasOne(x => x.Boat)
            .WithMany(x => x.Incidents)
            .HasForeignKey(x => x.BoatId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Trip)
            .WithMany(x => x.Incidents)
            .HasForeignKey(x => x.TripId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Reporter)
            .WithMany()
            .HasForeignKey(x => x.ReportedBy)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.AssignedManager)
            .WithMany()
            .HasForeignKey(x => x.AssignedManagerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.AssignedByUser)
            .WithMany()
            .HasForeignKey(x => x.AssignedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.RescueBoat)
            .WithMany()
            .HasForeignKey(x => x.RescueBoatId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.RescueDispatchedByUser)
            .WithMany()
            .HasForeignKey(x => x.RescueDispatchedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.ReplacementBoat)
            .WithMany()
            .HasForeignKey(x => x.ReplacementBoatId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.ReplacementAssignedByUser)
            .WithMany()
            .HasForeignKey(x => x.ReplacementAssignedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.ReplacementTargetStation)
            .WithMany()
            .HasForeignKey(x => x.ReplacementTargetStationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Resolver)
            .WithMany()
            .HasForeignKey(x => x.ResolvedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
