using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class CustomBookingStaffAssignmentConfiguration
    : IEntityTypeConfiguration<CustomBookingStaffAssignment>
{
    public void Configure(EntityTypeBuilder<CustomBookingStaffAssignment> builder)
    {
        builder.ToTable("custom_booking_staff_assignments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("custom_booking_staff_assignment_id");
        builder.Property(x => x.CustomBookingRequestId).HasColumnName("custom_booking_request_id").IsRequired();
        builder.Property(x => x.StaffUserId).HasColumnName("staff_user_id").IsRequired();
        builder.Property(x => x.DutyNote).HasColumnName("duty_note").HasMaxLength(500);
        builder.Property(x => x.AssignedAt).HasColumnName("assigned_at").IsRequired();
        builder.Property(x => x.AssignedByManagerUserId).HasColumnName("assigned_by_manager_user_id").IsRequired();
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.CustomBookingRequestId, x.StaffUserId }).IsUnique();
        builder.HasIndex(x => x.StaffUserId);

        builder.HasOne(x => x.CustomBookingRequest)
            .WithMany(x => x.StaffAssignments)
            .HasForeignKey(x => x.CustomBookingRequestId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne(x => x.StaffUser)
            .WithMany()
            .HasForeignKey(x => x.StaffUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.AssignedByManagerUser)
            .WithMany()
            .HasForeignKey(x => x.AssignedByManagerUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
