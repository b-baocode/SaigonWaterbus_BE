using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class FareAdjustmentConfiguration : IEntityTypeConfiguration<FareAdjustment>
{
    public void Configure(EntityTypeBuilder<FareAdjustment> builder)
    {
        builder.ToTable("fare_adjustments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("fare_adjustment_id");

        builder.Property(x => x.Scope).HasColumnName("scope").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Date).HasColumnName("date").HasColumnType("date");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.SurchargePercent)
            .HasColumnName("surcharge_percent")
            .HasColumnType("numeric(7,2)")
            .IsRequired();
        builder.Property(x => x.RoundingStep)
            .HasColumnName("rounding_step")
            .HasColumnType("numeric(12,2)")
            .IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => x.Scope)
            .HasDatabaseName("ix_fare_adjustments_weekend_scope")
            .IsUnique()
            .HasFilter("date IS NULL");
        builder.HasIndex(x => new { x.Scope, x.Date })
            .HasDatabaseName("ix_fare_adjustments_scope_date")
            .IsUnique()
            .HasFilter("date IS NOT NULL");
        builder.HasIndex(x => x.Date);
    }
}
