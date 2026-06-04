using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class StationConfiguration : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> builder)
    {
        builder.ToTable("stations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("station_id");

        builder.Property(x => x.StationCode).HasColumnName("station_code").HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.StationCode).IsUnique();

        builder.Property(x => x.StationName).HasColumnName("station_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Address).HasColumnName("address");
        builder.Property(x => x.Latitude).HasColumnName("latitude").HasColumnType("numeric(9,6)");
        builder.Property(x => x.Longitude).HasColumnName("longitude").HasColumnType("numeric(9,6)");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Location).HasColumnName("location").HasColumnType("geography(Point,4326)");
        builder.Property(x => x.OsmId).HasColumnName("osm_id").HasMaxLength(50);
        builder.HasIndex(x => x.Location).HasMethod("gist").HasDatabaseName("ix_stations_location");
        builder.HasIndex(x => x.OsmId).HasDatabaseName("ix_stations_osm_id");

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);
    }
}
