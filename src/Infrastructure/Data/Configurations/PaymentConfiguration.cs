using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("payment_id");

        builder.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired();
        builder.Property(x => x.PaymentCode).HasColumnName("payment_code").HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.PaymentCode).IsUnique();
        builder.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(50);
        builder.Property(x => x.ProviderTransactionId).HasColumnName("provider_transaction_id").HasMaxLength(100);
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.PaymentMethod).HasColumnName("payment_method").HasMaxLength(30).IsRequired();
        builder.Property(x => x.PaymentPurpose).HasColumnName("payment_purpose").HasMaxLength(30).IsRequired();
        builder.Property(x => x.PaymentStatus).HasColumnName("status").HasMaxLength(30).IsRequired();
        builder.Property(x => x.CheckoutUrl).HasColumnName("checkout_url").HasMaxLength(1000);
        builder.Property(x => x.QrCode).HasColumnName("qr_code").HasMaxLength(4000);
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.PaidAt).HasColumnName("paid_at");
        builder.Property(x => x.RefundAmount).HasColumnName("refund_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.RefundRequestedAmount).HasColumnName("refund_requested_amount").HasColumnType("numeric(12,2)");
        builder.Property(x => x.RefundMethod).HasColumnName("refund_method").HasMaxLength(30);
        builder.Property(x => x.RefundReason).HasColumnName("refund_reason").HasMaxLength(500);
        builder.Property(x => x.RefundReferenceId).HasColumnName("refund_reference_id").HasMaxLength(100);
        builder.Property(x => x.RefundPayoutId).HasColumnName("refund_payout_id").HasMaxLength(100);
        builder.Property(x => x.RefundStatus).HasColumnName("refund_status").HasMaxLength(30);
        builder.Property(x => x.RefundFailureReason).HasColumnName("refund_failure_reason").HasMaxLength(500);
        builder.Property(x => x.RefundProcessedByUserId).HasColumnName("refund_processed_by_user_id");
        builder.Property(x => x.RefundedAt).HasColumnName("refunded_at");
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.PaymentStatus, x.ExpiresAt });
        builder.HasOne(x => x.Booking).WithMany(x => x.Payments).HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
    }
}
