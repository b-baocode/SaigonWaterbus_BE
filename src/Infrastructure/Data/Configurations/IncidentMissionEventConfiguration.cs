using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class IncidentMissionEventConfiguration : IEntityTypeConfiguration<IncidentMissionEvent>
{
    public void Configure(EntityTypeBuilder<IncidentMissionEvent> builder)
    {
        builder.ToTable("incident_mission_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("incident_mission_event_id");

        builder.Property(x => x.IncidentId).HasColumnName("incident_id").IsRequired();
        builder.Property(x => x.GpsEventId)
            .HasColumnName("gps_event_id")
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(x => x.Event).HasColumnName("event").HasMaxLength(50).IsRequired();
        builder.Property(x => x.BoatCode).HasColumnName("boat_code").HasMaxLength(50).IsRequired();
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.Latitude).HasColumnName("latitude").HasColumnType("numeric(10,7)");
        builder.Property(x => x.Longitude).HasColumnName("longitude").HasColumnType("numeric(10,7)");
        builder.Property(x => x.StationId).HasColumnName("station_id");
        builder.Property(x => x.StationCode).HasColumnName("station_code").HasMaxLength(50);
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(1000);
        builder.Property(x => x.ReportedPreviousMissionStatus)
            .HasColumnName("reported_previous_mission_status")
            .HasMaxLength(50);
        builder.Property(x => x.EstimatedTowingMinutes).HasColumnName("estimated_towing_minutes");
        builder.Property(x => x.PreviousMissionStatus)
            .HasColumnName("previous_mission_status")
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(x => x.MissionStatus)
            .HasColumnName("mission_status")
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.IncidentId, x.GpsEventId }).IsUnique();
        builder.HasIndex(x => x.Event);
        builder.HasIndex(x => x.StationId);
        builder.HasIndex(x => x.OccurredAt);

        builder.HasOne(x => x.Incident)
            .WithMany(x => x.MissionEvents)
            .HasForeignKey(x => x.IncidentId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne(x => x.Station)
            .WithMany()
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
