using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class CustomBookingQuoteConfiguration : IEntityTypeConfiguration<CustomBookingQuote>
{
    public void Configure(EntityTypeBuilder<CustomBookingQuote> builder)
    {
        builder.ToTable("custom_booking_quotes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("custom_booking_quote_id");
        builder.Property(x => x.CustomBookingRequestId).HasColumnName("custom_booking_request_id").IsRequired();
        builder.Property(x => x.QuotedPrice).HasColumnName("quoted_price").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.ServiceFeeAmount).HasColumnName("service_fee_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.DepositPercent).HasColumnName("deposit_percent").HasColumnType("numeric(5,2)").IsRequired();
        builder.Property(x => x.DepositAmount).HasColumnName("deposit_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.RemainingAmount).HasColumnName("remaining_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.PriceNote).HasColumnName("price_note").HasMaxLength(1000);
        builder.Property(x => x.ValidUntil).HasColumnName("valid_until");

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => x.CustomBookingRequestId).IsUnique();

        builder.HasOne(x => x.CustomBookingRequest)
            .WithOne(x => x.Quote)
            .HasForeignKey<CustomBookingQuote>(x => x.CustomBookingRequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
