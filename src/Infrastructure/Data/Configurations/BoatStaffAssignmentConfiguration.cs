using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class BoatStaffAssignmentConfiguration : IEntityTypeConfiguration<BoatStaffAssignment>
{
    public void Configure(EntityTypeBuilder<BoatStaffAssignment> builder)
    {
        builder.ToTable("boat_staff_assignments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("boat_staff_assignment_id");

        builder.Property(x => x.BoatId).HasColumnName("boat_id").IsRequired();
        builder.Property(x => x.StaffUserId).HasColumnName("staff_user_id").IsRequired();
        builder.Property(x => x.WorkingDate).HasColumnName("working_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.ShiftCode).HasColumnName("shift_code").HasMaxLength(30);
        builder.Property(x => x.DutyRole).HasColumnName("duty_role").HasMaxLength(50);
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.AssignedByUserId).HasColumnName("assigned_by_user_id").IsRequired();
        builder.Property(x => x.AssignedAt).HasColumnName("assigned_at").IsRequired();
        builder.Property(x => x.ReplacesAssignmentId).HasColumnName("replaces_assignment_id");
        builder.Property(x => x.ReplacedByAssignmentId).HasColumnName("replaced_by_assignment_id");
        builder.Property(x => x.ReplacementReason).HasColumnName("replacement_reason").HasMaxLength(500);
        builder.Property(x => x.ReplacedAt).HasColumnName("replaced_at");
        builder.Property(x => x.ReplacedByUserId).HasColumnName("replaced_by_user_id");
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.BoatId, x.WorkingDate, x.ShiftCode, x.IsActive });
        builder.HasIndex(x => new { x.StaffUserId, x.WorkingDate, x.ShiftCode, x.IsActive });
        builder.HasIndex(x => x.AssignedByUserId);
        builder.HasIndex(x => x.ReplacesAssignmentId);
        builder.HasIndex(x => x.ReplacedByAssignmentId);
        builder.HasIndex(x => x.ReplacedByUserId);

        builder.HasOne(x => x.Boat)
            .WithMany(x => x.StaffAssignments)
            .HasForeignKey(x => x.BoatId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne(x => x.StaffUser)
            .WithMany(x => x.BoatStaffAssignments)
            .HasForeignKey(x => x.StaffUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.AssignedByUser)
            .WithMany(x => x.AssignedBoatStaffAssignments)
            .HasForeignKey(x => x.AssignedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.ReplacesAssignment)
            .WithMany()
            .HasForeignKey(x => x.ReplacesAssignmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.ReplacedByAssignment)
            .WithMany()
            .HasForeignKey(x => x.ReplacedByAssignmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.ReplacedByUser)
            .WithMany()
            .HasForeignKey(x => x.ReplacedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
