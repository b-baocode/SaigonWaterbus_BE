using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> builder)
    {
        builder.ToTable("promotions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("promotion_id");

        builder.Property(x => x.PromotionCode).HasColumnName("promotion_code").HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.PromotionCode).IsUnique();

        builder.Property(x => x.PromotionName).HasColumnName("promotion_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.PromotionType)
            .HasColumnName("promotion_type")
            .HasConversion(
                value => value == PromotionType.Fixed ? "FixedAmount" : value.ToString(),
                value => string.Equals(value, "FixedAmount", StringComparison.OrdinalIgnoreCase)
                    ? PromotionType.Fixed
                    : Enum.Parse<PromotionType>(value, true))
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.DiscountValue).HasColumnName("discount_value").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.MinOrderValue).HasColumnName("min_order_value").HasColumnType("numeric(12,2)");
        builder.Property(x => x.ValidFrom).HasColumnName("valid_from").IsRequired();
        builder.Property(x => x.ValidTo).HasColumnName("valid_to").IsRequired();
        builder.Property(x => x.UsageLimit).HasColumnName("usage_limit");
        builder.Property(x => x.UsageCount).HasColumnName("usage_count").IsRequired().HasDefaultValue(0);
        builder.Property(x => x.AccountUsagePolicy)
            .HasColumnName("account_usage_policy")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired()
            .HasDefaultValue(PromotionAccountUsagePolicy.MultiplePerAccount);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(30).IsRequired();

        builder.Ignore(x => x.Created);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);
    }
}
