using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class InsurancePackageConfiguration : IEntityTypeConfiguration<InsurancePackage>
{
    public void Configure(EntityTypeBuilder<InsurancePackage> builder)
    {
        builder.ToTable("insurance_packages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("insurance_package_id");

        builder.Property(x => x.Code).HasColumnName("package_code").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasColumnName("package_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.BookingType).HasColumnName("booking_type").HasMaxLength(30).IsRequired();
        builder.Property(x => x.IsRequired).HasColumnName("is_required").IsRequired();
        builder.Property(x => x.ProviderName).HasColumnName("provider_name").HasMaxLength(150);
        builder.Property(x => x.ProviderLogoUrl).HasColumnName("provider_logo_url").HasMaxLength(1000);
        builder.Property(x => x.UnitPremiumAmount)
            .HasColumnName("unit_premium_amount")
            .HasColumnType("numeric(12,2)")
            .IsRequired();
        builder.Property(x => x.CoverageAmount)
            .HasColumnName("coverage_amount")
            .HasColumnType("numeric(14,2)")
            .IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Conditions)
            .HasColumnName("conditions")
            .HasColumnType("text[]")
            .HasDefaultValueSql("ARRAY[]::text[]");
        builder.Property(x => x.TermsUrl).HasColumnName("terms_url").HasMaxLength(1000);
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

        builder.Property(x => x.ProviderSource)
            .HasColumnName("provider_source")
            .HasMaxLength(30)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.BookingType, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.BookingType, x.IsActive });
        builder.HasIndex(x => new { x.BookingType, x.ProviderSource, x.IsActive });
    }
}
