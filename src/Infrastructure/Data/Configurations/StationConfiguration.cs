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
        builder.Property(x => x.Address).HasColumnName("address").HasMaxLength(255);
        builder.Ignore(x => x.Description);
        builder.Property(x => x.Latitude).HasColumnName("latitude").HasColumnType("numeric(10,7)");
        builder.Property(x => x.Longitude).HasColumnName("longitude").HasColumnType("numeric(10,7)");
        builder.Property(x => x.OpeningTime).HasColumnName("opening_time");
        builder.Property(x => x.ClosingTime).HasColumnName("closing_time");
        builder.Property(x => x.IsWaterbusStation).HasColumnName("is_waterbus_station").HasDefaultValue(true);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(30).IsRequired()
            .HasConversion<string>();
        builder.Ignore(x => x.Location);
        builder.Ignore(x => x.OsmId);
        builder.Property(x => x.ImageUrl).HasColumnName("image_url").HasMaxLength(2048);
        builder.Property(x => x.ImageUrls)
            .HasColumnName("image_urls")
            .HasColumnType("text[]")
            .HasDefaultValueSql("ARRAY[]::text[]");
        builder.Ignore(x => x.ImagePublicId);
        builder.Property(x => x.HasWaitingArea).HasColumnName("has_waiting_area");
        builder.Property(x => x.HasParking).HasColumnName("has_parking");
        builder.Property(x => x.HasTicketCounter).HasColumnName("has_ticket_counter");

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);
    }
}
