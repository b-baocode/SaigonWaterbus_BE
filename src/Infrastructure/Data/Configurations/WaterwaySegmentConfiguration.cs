using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class WaterwaySegmentConfiguration : IEntityTypeConfiguration<WaterwaySegment>
{
    public void Configure(EntityTypeBuilder<WaterwaySegment> builder)
    {
        builder.ToTable("waterway_segments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("waterway_segment_id");

        builder.Property(x => x.OsmId).HasColumnName("osm_id").HasMaxLength(50);
        builder.Property(x => x.WaterwayName).HasColumnName("waterway_name").HasMaxLength(200);
        builder.Property(x => x.WaterwayType).HasColumnName("waterway_type").HasMaxLength(20).IsRequired();
        builder.Property(x => x.SegmentOrder).HasColumnName("segment_order").IsRequired();
        builder.Property(x => x.LengthKm).HasColumnName("length_km").HasColumnType("numeric(8,2)");
        builder.Property(x => x.Geometry).HasColumnName("geometry").HasColumnType("geography(LineString,4326)");

        builder.HasIndex(x => x.OsmId).HasDatabaseName("ix_waterway_segments_osm_id");
        builder.HasIndex(x => x.WaterwayName).HasDatabaseName("ix_waterway_segments_name");
        builder.HasIndex(x => new { x.OsmId, x.SegmentOrder }).IsUnique();
        builder.HasIndex(x => x.Geometry).HasMethod("gist").HasDatabaseName("ix_waterway_segments_geometry");

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);
    }
}
