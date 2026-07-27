using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class FarePolicyConfiguration : IEntityTypeConfiguration<FarePolicy>
{
    public void Configure(EntityTypeBuilder<FarePolicy> builder)
    {
        builder.ToTable("fare_policies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("fare_policy_id");

        builder.Property(x => x.BaseFare).HasColumnName("base_fare").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.PricePerKm).HasColumnName("price_per_km").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.RoundingStep).HasColumnName("rounding_step").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);
    }
}
