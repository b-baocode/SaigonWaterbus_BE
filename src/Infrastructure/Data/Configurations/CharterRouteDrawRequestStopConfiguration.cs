using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class CharterRouteDrawRequestStopConfiguration : IEntityTypeConfiguration<CharterRouteDrawRequestStop>
{
    public void Configure(EntityTypeBuilder<CharterRouteDrawRequestStop> builder)
    {
        builder.ToTable("charter_route_draw_request_stops");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("request_stop_id");
        builder.Property(x => x.RequestId).HasColumnName("request_id").IsRequired();
        builder.Property(x => x.StationId).HasColumnName("station_id").IsRequired();
        builder.Property(x => x.StopOrder).HasColumnName("stop_order").IsRequired();
        builder.Property(x => x.StationCode).HasColumnName("station_code").HasMaxLength(50).IsRequired();
        builder.Property(x => x.StationName).HasColumnName("station_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Latitude).HasColumnName("latitude").HasColumnType("numeric(10,7)");
        builder.Property(x => x.Longitude).HasColumnName("longitude").HasColumnType("numeric(10,7)");
        builder.Property(x => x.StayDurationMinutes).HasColumnName("stay_duration_minutes").IsRequired();
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.RequestId, x.StopOrder }).IsUnique();
        builder.HasIndex(x => x.StationId);

        builder.HasOne(x => x.Request)
            .WithMany(x => x.Stops)
            .HasForeignKey(x => x.RequestId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
        builder.HasOne(x => x.Station)
            .WithMany()
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
