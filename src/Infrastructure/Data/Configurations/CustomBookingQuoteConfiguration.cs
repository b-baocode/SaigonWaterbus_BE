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
        builder.Property(x => x.DiscountCode).HasColumnName("discount_code").HasMaxLength(50);
        builder.Property(x => x.DiscountAmount).HasColumnName("discount_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.DepositPercent).HasColumnName("deposit_percent").HasColumnType("numeric(5,2)").IsRequired();
        builder.Property(x => x.DepositAmount).HasColumnName("deposit_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.RemainingAmount).HasColumnName("remaining_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.PriceNote).HasColumnName("price_note").HasMaxLength(1000);
        builder.Property(x => x.ValidUntil).HasColumnName("valid_until");
        builder.Property(x => x.DepositPaymentStatus)
            .HasColumnName("deposit_payment_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.DepositPaymentOrderCode).HasColumnName("deposit_payment_order_code");
        builder.Property(x => x.DepositPaymentLinkId).HasColumnName("deposit_payment_link_id").HasMaxLength(100);
        builder.Property(x => x.DepositPaymentCheckoutUrl).HasColumnName("deposit_payment_checkout_url").HasMaxLength(1000);
        builder.Property(x => x.DepositPaymentQrCode).HasColumnName("deposit_payment_qr_code").HasMaxLength(2000);
        builder.Property(x => x.DepositPaymentCreatedAt).HasColumnName("deposit_payment_created_at");
        builder.Property(x => x.DepositPaymentPaidAt).HasColumnName("deposit_payment_paid_at");
        builder.Property(x => x.DepositPaymentCancelledAt).HasColumnName("deposit_payment_cancelled_at");
        builder.Property(x => x.DepositPaymentFailureReason).HasColumnName("deposit_payment_failure_reason").HasMaxLength(500);
        builder.Property(x => x.RemainingPaymentStatus)
            .HasColumnName("remaining_payment_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.RemainingPaymentOrderCode).HasColumnName("remaining_payment_order_code");
        builder.Property(x => x.RemainingPaymentLinkId).HasColumnName("remaining_payment_link_id").HasMaxLength(100);
        builder.Property(x => x.RemainingPaymentCheckoutUrl).HasColumnName("remaining_payment_checkout_url").HasMaxLength(1000);
        builder.Property(x => x.RemainingPaymentQrCode).HasColumnName("remaining_payment_qr_code").HasMaxLength(2000);
        builder.Property(x => x.RemainingPaymentCreatedAt).HasColumnName("remaining_payment_created_at");
        builder.Property(x => x.RemainingPaymentPaidAt).HasColumnName("remaining_payment_paid_at");
        builder.Property(x => x.RemainingPaymentCancelledAt).HasColumnName("remaining_payment_cancelled_at");
        builder.Property(x => x.RemainingPaymentFailureReason).HasColumnName("remaining_payment_failure_reason").HasMaxLength(500);
        builder.Property(x => x.RefundEligiblePercent).HasColumnName("refund_eligible_percent").HasColumnType("numeric(5,2)").IsRequired();
        builder.Property(x => x.RefundAmount).HasColumnName("refund_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.RefundPolicyNote).HasColumnName("refund_policy_note").HasMaxLength(500);
        builder.Property(x => x.RefundBankBin).HasColumnName("refund_bank_bin").HasMaxLength(20);
        builder.Property(x => x.RefundAccountNumber).HasColumnName("refund_account_number").HasMaxLength(50);
        builder.Property(x => x.RefundAccountName).HasColumnName("refund_account_name").HasMaxLength(150);
        builder.Property(x => x.RefundReferenceId).HasColumnName("refund_reference_id").HasMaxLength(100);
        builder.Property(x => x.RefundPayoutId).HasColumnName("refund_payout_id").HasMaxLength(100);
        builder.Property(x => x.RefundStatus).HasColumnName("refund_status").HasMaxLength(50);
        builder.Property(x => x.RefundFailureReason).HasColumnName("refund_failure_reason").HasMaxLength(500);
        builder.Property(x => x.RefundRequestedAt).HasColumnName("refund_requested_at");
        builder.Property(x => x.RefundProcessedAt).HasColumnName("refund_processed_at");

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => x.CustomBookingRequestId).IsUnique();
        builder.HasIndex(x => x.DepositPaymentOrderCode).IsUnique();
        builder.HasIndex(x => x.RemainingPaymentOrderCode).IsUnique();
        builder.HasIndex(x => x.RefundReferenceId).IsUnique();

        builder.HasOne(x => x.CustomBookingRequest)
            .WithOne(x => x.Quote)
            .HasForeignKey<CustomBookingQuote>(x => x.CustomBookingRequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
