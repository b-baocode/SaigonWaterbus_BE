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

        builder.Property(x => x.TripId).HasColumnName("trip_id").IsRequired();
        builder.Property(x => x.ReportedBy).HasColumnName("reported_by");
        builder.Property(x => x.IncidentType).HasColumnName("incident_type").HasMaxLength(30).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").IsRequired();
        builder.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(20);
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.ResolutionStatus).HasColumnName("resolution_status").HasMaxLength(20).IsRequired();

        builder.HasOne(x => x.Trip).WithMany(t => t.Incidents).HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Reporter).WithMany().HasForeignKey(x => x.ReportedBy).OnDelete(DeleteBehavior.SetNull);
    }
}
