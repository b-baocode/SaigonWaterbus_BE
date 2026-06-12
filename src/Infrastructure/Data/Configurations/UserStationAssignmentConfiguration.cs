using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class UserStationAssignmentConfiguration : IEntityTypeConfiguration<UserStationAssignment>
{
    public void Configure(EntityTypeBuilder<UserStationAssignment> builder)
    {
        builder.ToTable("user_station_assignments");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("user_station_assignment_id");

        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.StationId).HasColumnName("station_id").IsRequired();
        builder.Property(x => x.IsPrimary).HasColumnName("is_primary").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.AssignedAt).HasColumnName("assigned_at").IsRequired();
        builder.Property(x => x.AssignedByUserId).HasColumnName("assigned_by_user_id").IsRequired();

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.UserId, x.StationId }).IsUnique();
        builder.HasIndex(x => new { x.StationId, x.IsActive });
        builder.HasIndex(x => new { x.UserId, x.IsActive });

        builder.HasOne(x => x.User)
            .WithMany(x => x.StationAssignments)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne(x => x.Station)
            .WithMany(x => x.UserAssignments)
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne(x => x.AssignedByUser)
            .WithMany(x => x.AssignedStationAssignments)
            .HasForeignKey(x => x.AssignedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
