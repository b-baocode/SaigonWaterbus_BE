using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class StationStaffAssignmentConfiguration : IEntityTypeConfiguration<StationStaffAssignment>
{
    public void Configure(EntityTypeBuilder<StationStaffAssignment> builder)
    {
        builder.ToTable("station_staff_assignments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("station_staff_assignment_id");

        builder.Property(x => x.StationId).HasColumnName("station_id").IsRequired();
        builder.Property(x => x.StaffUserId).HasColumnName("staff_user_id").IsRequired();
        builder.Property(x => x.SourceType)
            .HasColumnName("source_type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.SourceId).HasColumnName("source_id").IsRequired();
        builder.Property(x => x.WorkingDate).HasColumnName("working_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.ShiftCode).HasColumnName("shift_code").HasMaxLength(30);
        builder.Property(x => x.DutyRole).HasColumnName("duty_role").HasMaxLength(50);
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.AssignedByUserId).HasColumnName("assigned_by_user_id").IsRequired();
        builder.Property(x => x.AssignedAt).HasColumnName("assigned_at").IsRequired();
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.SourceType, x.SourceId, x.StationId, x.IsActive });
        builder.HasIndex(x => new { x.StaffUserId, x.WorkingDate, x.ShiftCode, x.IsActive });
        builder.HasIndex(x => x.AssignedByUserId);

        builder.HasOne(x => x.Station)
            .WithMany(x => x.StaffAssignments)
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne(x => x.StaffUser)
            .WithMany(x => x.StationStaffAssignments)
            .HasForeignKey(x => x.StaffUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.AssignedByUser)
            .WithMany(x => x.AssignedStationStaffAssignments)
            .HasForeignKey(x => x.AssignedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
