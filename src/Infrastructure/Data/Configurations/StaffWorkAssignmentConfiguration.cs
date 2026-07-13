using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class StaffWorkAssignmentConfiguration : IEntityTypeConfiguration<StaffWorkAssignment>
{
    public void Configure(EntityTypeBuilder<StaffWorkAssignment> builder)
    {
        builder.ToTable("staff_work_assignments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("staff_work_assignment_id");

        builder.Property(x => x.StaffUserId).HasColumnName("staff_user_id").IsRequired();
        builder.Property(x => x.AssignmentType)
            .HasColumnName("assignment_type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.BoatId).HasColumnName("boat_id");
        builder.Property(x => x.StationId).HasColumnName("station_id");
        builder.Property(x => x.WorkingDate).HasColumnName("working_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.StartAt).HasColumnName("start_at").IsRequired();
        builder.Property(x => x.EndAt).HasColumnName("end_at").IsRequired();
        builder.Property(x => x.DutyRole).HasColumnName("duty_role").HasMaxLength(80);
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.AssignedByUserId).HasColumnName("assigned_by_user_id").IsRequired();
        builder.Property(x => x.AssignedAt).HasColumnName("assigned_at").IsRequired();
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.StaffUserId, x.WorkingDate, x.Status });
        builder.HasIndex(x => new { x.AssignmentType, x.BoatId, x.Status });
        builder.HasIndex(x => new { x.AssignmentType, x.StationId, x.Status });
        builder.HasIndex(x => x.AssignedByUserId);

        builder.HasOne(x => x.StaffUser)
            .WithMany(x => x.StaffWorkAssignments)
            .HasForeignKey(x => x.StaffUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.AssignedByUser)
            .WithMany(x => x.AssignedStaffWorkAssignments)
            .HasForeignKey(x => x.AssignedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.Boat)
            .WithMany(x => x.WorkAssignments)
            .HasForeignKey(x => x.BoatId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Station)
            .WithMany()
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
