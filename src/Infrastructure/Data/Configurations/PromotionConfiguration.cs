using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    private static readonly JsonSerializerOptions ScopeJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ValueComparer<PromotionScope?> ScopeComparer = new(
        (left, right) => SerializeScope(left) == SerializeScope(right),
        scope => SerializeScope(scope) == null ? 0 : SerializeScope(scope)!.GetHashCode(),
        scope => DeserializeScope(SerializeScope(scope)));

    public void Configure(EntityTypeBuilder<Promotion> builder)
    {
        builder.ToTable("promotions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("promotion_id");

        builder.Property(x => x.PromotionCode).HasColumnName("promotion_code").HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.PromotionCode).IsUnique();

        builder.Property(x => x.PromotionName).HasColumnName("promotion_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(1000);

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
        builder.Property(x => x.MaxDiscountAmount).HasColumnName("max_discount_amount").HasColumnType("numeric(12,2)");
        builder.Property(x => x.MinOrderValue).HasColumnName("min_order_value").HasColumnType("numeric(12,2)");
        builder.Property(x => x.ValidFrom).HasColumnName("valid_from").IsRequired();
        builder.Property(x => x.ValidTo).HasColumnName("valid_to").IsRequired();
        builder.Property(x => x.UsageLimit).HasColumnName("usage_limit");
        builder.Property(x => x.MaxUsesPerAccount).HasColumnName("max_uses_per_account");
        builder.Property(x => x.BudgetCap).HasColumnName("budget_cap").HasColumnType("numeric(14,2)");
        builder.Property(x => x.FirstBookingOnly).HasColumnName("first_booking_only").IsRequired().HasDefaultValue(false);

        builder.Property(x => x.Scope)
            .HasColumnName("scope")
            .HasColumnType("jsonb")
            .HasConversion(
                scope => SerializeScope(scope),
                json => DeserializeScope(json))
            .Metadata.SetValueComparer(ScopeComparer);

        builder.Property(x => x.Visibility)
            .HasColumnName("visibility")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(PromotionVisibility.Public);

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(PromotionStatus.Draft);

        builder.Property(x => x.ImageUrl).HasColumnName("image_url").HasMaxLength(2048);

        // Audit: giữ created_at / updated_at (trước đây bị ignore hết — tính năng dính tiền cần dấu vết thời gian).
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.ImagePublicId);
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);
    }

    private static string? SerializeScope(PromotionScope? scope) =>
        scope is null || scope.IsEmpty ? null : JsonSerializer.Serialize(scope, ScopeJsonOptions);

    private static PromotionScope? DeserializeScope(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<PromotionScope>(json, ScopeJsonOptions);
}
