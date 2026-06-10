using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class RouteConfiguration : IEntityTypeConfiguration<Domain.Entities.Route>
{
    public void Configure(EntityTypeBuilder<Domain.Entities.Route> builder)
    {
        builder.ToTable("routes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("route_id");

        builder.Property(x => x.RouteCode).HasColumnName("route_code").HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.RouteCode).IsUnique();

        builder.Property(x => x.RouteName).HasColumnName("route_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description");
        builder.Property(x => x.BaseDistanceKm).HasColumnName("base_distance_km").HasColumnType("numeric(8,2)");
        builder.Property(x => x.EstimatedDurationMin).HasColumnName("estimated_duration_min");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.RouteGeometry).HasColumnName("route_geometry").HasColumnType("geography(LineString,4326)");
        builder.Property(x => x.OsmId).HasColumnName("osm_id").HasMaxLength(50);

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);
    }
}
