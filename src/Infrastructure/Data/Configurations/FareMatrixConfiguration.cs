using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class FareMatrixConfiguration : IEntityTypeConfiguration<FareMatrix>
{
    public void Configure(EntityTypeBuilder<FareMatrix> builder)
    {
        builder.ToTable("fare_rules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("fare_rule_id");

        builder.Property(x => x.ServiceId).HasColumnName("service_id");
        builder.Property(x => x.RouteId).HasColumnName("route_id").IsRequired();
        builder.Property(x => x.FromStationId).HasColumnName("from_station_id");
        builder.Property(x => x.ToStationId).HasColumnName("to_station_id");
        builder.Property(x => x.ServicePeriod).HasColumnName("service_period").HasMaxLength(30);
        builder.Property(x => x.BasePrice).HasColumnName("base_price").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

        builder.HasIndex(x => new { x.RouteId, x.FromStationId, x.ToStationId })
            .IsUnique()
            .HasFilter("is_active = true");

        builder.HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Route).WithMany(r => r.FareMatrices).HasForeignKey(x => x.RouteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.FromStation).WithMany().HasForeignKey(x => x.FromStationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ToStation).WithMany().HasForeignKey(x => x.ToStationId).OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(x => x.Created);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);
    }
}
