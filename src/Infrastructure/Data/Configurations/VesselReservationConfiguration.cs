using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class VesselReservationConfiguration : IEntityTypeConfiguration<VesselReservation>
{
    public void Configure(EntityTypeBuilder<VesselReservation> builder)
    {
        builder.ToTable("vessel_reservations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("vessel_reservation_id");

        builder.Property(x => x.VesselId).HasColumnName("vessel_id").IsRequired();
        builder.Property(x => x.SourceType)
            .HasColumnName("source_type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.SourceId).HasColumnName("source_id").IsRequired();
        builder.Property(x => x.StartAt).HasColumnName("start_at").IsRequired();
        builder.Property(x => x.EndAt).HasColumnName("end_at").IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.ConfirmedByUserId).HasColumnName("confirmed_by_user_id");
        builder.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at");
        builder.Property(x => x.ReleasedAt).HasColumnName("released_at");
        builder.Property(x => x.ReleaseReason).HasColumnName("release_reason").HasMaxLength(500);

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.SourceType, x.SourceId }).IsUnique();
        builder.HasIndex(x => new { x.VesselId, x.StartAt, x.EndAt });
        builder.HasIndex(x => new { x.Status, x.ExpiresAt });

        builder.HasOne(x => x.Vessel)
            .WithMany()
            .HasForeignKey(x => x.VesselId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ConfirmedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
