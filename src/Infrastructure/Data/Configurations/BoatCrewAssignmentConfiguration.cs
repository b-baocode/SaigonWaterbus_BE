using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class BoatCrewAssignmentConfiguration : IEntityTypeConfiguration<BoatCrewAssignment>
{
    public void Configure(EntityTypeBuilder<BoatCrewAssignment> builder)
    {
        builder.ToTable("boat_crew_assignments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("boat_crew_assignment_id");

        builder.Property(x => x.BoatId).HasColumnName("boat_id").IsRequired();
        builder.Property(x => x.StaffUserId).HasColumnName("staff_user_id").IsRequired();
        builder.Property(x => x.CrewRole)
            .HasColumnName("crew_role")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.FromDate).HasColumnName("from_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.ToDate).HasColumnName("to_date").HasColumnType("date");
        builder.Property(x => x.ReplacesAssignmentId).HasColumnName("replaces_assignment_id");
        builder.Property(x => x.ReplacementReason)
            .HasColumnName("replacement_reason")
            .HasMaxLength(500);
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.AssignedByUserId).HasColumnName("assigned_by_user_id").IsRequired();
        builder.Property(x => x.AssignedAt).HasColumnName("assigned_at").IsRequired();

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.BoatId, x.CrewRole, x.IsActive });
        builder.HasIndex(x => new { x.StaffUserId, x.IsActive });
        builder.HasIndex(x => x.AssignedByUserId);
        builder.HasIndex(x => x.ReplacesAssignmentId);

        builder.HasOne(x => x.Boat)
            .WithMany(x => x.CrewAssignments)
            .HasForeignKey(x => x.BoatId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne(x => x.StaffUser)
            .WithMany(x => x.BoatCrewAssignments)
            .HasForeignKey(x => x.StaffUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.AssignedByUser)
            .WithMany(x => x.AssignedBoatCrewAssignments)
            .HasForeignKey(x => x.AssignedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.ReplacesAssignment)
            .WithMany(x => x.ReplacementAssignments)
            .HasForeignKey(x => x.ReplacesAssignmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
