using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class StationImageConfiguration : IEntityTypeConfiguration<StationImage>
{
    public void Configure(EntityTypeBuilder<StationImage> builder)
    {
        builder.ToTable("station_images");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("station_image_id");

        builder.Property(x => x.StationId)
            .HasColumnName("station_id")
            .IsRequired();

        builder.Property(x => x.Url)
            .HasColumnName("url")
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(x => x.PublicId)
            .HasColumnName("public_id")
            .HasMaxLength(255);

        builder.Property(x => x.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        builder.Property(x => x.IsPrimary)
            .HasColumnName("is_primary")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasOne(x => x.Station)
            .WithMany(x => x.Images)
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasIndex(x => new { x.StationId, x.DisplayOrder });

        builder.HasIndex(x => new { x.StationId, x.IsPrimary })
            .IsUnique()
            .HasFilter("is_primary = TRUE");
    }
}
