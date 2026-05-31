using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

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
        builder.Property(x => x.PromotionType).HasColumnName("promotion_type").HasMaxLength(30).IsRequired();
        builder.Property(x => x.DiscountValue).HasColumnName("discount_value").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.MinOrderValue).HasColumnName("min_order_value").HasColumnType("numeric(12,2)");
        builder.Property(x => x.ValidFrom).HasColumnName("valid_from").IsRequired();
        builder.Property(x => x.ValidTo).HasColumnName("valid_to").IsRequired();
        builder.Property(x => x.UsageLimit).HasColumnName("usage_limit");
        builder.Property(x => x.UsageCount).HasColumnName("usage_count").IsRequired().HasDefaultValue(0);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);
    }
}
