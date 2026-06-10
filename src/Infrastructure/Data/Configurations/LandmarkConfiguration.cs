using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class LandmarkConfiguration : IEntityTypeConfiguration<Landmark>
{
    public void Configure(EntityTypeBuilder<Landmark> builder)
    {
        builder.ToTable("landmarks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("landmark_id");

        builder.Property(x => x.StationId).HasColumnName("station_id").IsRequired();
        builder.Property(x => x.LandmarkName).HasColumnName("landmark_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description");
        builder.Property(x => x.Latitude).HasColumnName("latitude").HasColumnType("numeric(9,6)");
        builder.Property(x => x.Longitude).HasColumnName("longitude").HasColumnType("numeric(9,6)");
        builder.Property(x => x.AudioViUrl).HasColumnName("audio_vi_url");
        builder.Property(x => x.AudioEnUrl).HasColumnName("audio_en_url");
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

        builder.HasOne(x => x.Station).WithMany(s => s.Landmarks).HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);
    }
}
