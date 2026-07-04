using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class GpsDeviceConfiguration : IEntityTypeConfiguration<GpsDevice>
{
    public void Configure(EntityTypeBuilder<GpsDevice> builder)
    {
        builder.ToTable("gps_devices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("gps_device_id");

        builder.Property(x => x.DeviceId).HasColumnName("device_id").HasMaxLength(100).IsRequired();
        builder.Property(x => x.BoatId).HasColumnName("boat_id").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.LastSequence).HasColumnName("last_sequence");
        builder.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => x.DeviceId).IsUnique();
        builder.HasIndex(x => new { x.BoatId, x.IsActive });

        builder.HasOne(x => x.Boat)
            .WithMany()
            .HasForeignKey(x => x.BoatId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
