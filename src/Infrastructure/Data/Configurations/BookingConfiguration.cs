using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("booking_id");
        builder.HasDiscriminator<string>("booking_type")
            .HasValue<Booking>("SeatBooking")
            .HasValue<CustomBooking>("CustomBooking");
        builder.Property<string>("booking_type")
            .HasColumnName("booking_type")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.UserId).HasColumnName("customer_user_id");
        builder.Property(x => x.PromotionId).HasColumnName("promotion_id");
        builder.Property(x => x.TripId).HasColumnName("trip_id");
        builder.Property(x => x.BookingCode).HasColumnName("booking_code").HasMaxLength(50).IsRequired();
        builder.Property(x => x.ContactName).HasColumnName("contact_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.ContactPhone).HasColumnName("contact_phone").HasMaxLength(30).IsRequired();
        builder.Property(x => x.ContactEmail).HasColumnName("contact_email").HasMaxLength(255);
        builder.HasIndex(x => x.BookingCode).IsUnique();

        builder.Property(x => x.BookingStatus)
            .HasColumnName("status")
            .HasConversion(
                value => ToDatabaseBookingStatus(value),
                value => FromDatabaseBookingStatus(value))
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.SubtotalAmount).HasColumnName("subtotal_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.DiscountAmount).HasColumnName("discount_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.TotalAmount).HasColumnName("total_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.PaymentStatus).HasColumnName("payment_status").HasMaxLength(30).IsRequired();
        builder.Property(x => x.DepositAmount).HasColumnName("deposit_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.RemainingAmount).HasColumnName("remaining_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Ignore(x => x.PointsUsed);
        builder.Ignore(x => x.PointsEarned);
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.Promotion).WithMany(p => p.Bookings).HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.Trip).WithMany().HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.SetNull);
    }

    private static string ToDatabaseBookingStatus(BookingStatus status) =>
        status switch
        {
            BookingStatus.PendingPayment => "Pending",
            BookingStatus.Expired => "Expired",
            BookingStatus.Refunded => "Refunded",
            BookingStatus.Quoted => "Quoted",
            BookingStatus.Completed => "Completed",
            _ => status.ToString()
        };

    private static BookingStatus FromDatabaseBookingStatus(string status) =>
        status switch
        {
            "Pending" => BookingStatus.PendingPayment,
            "Quoted" => BookingStatus.Quoted,
            "Completed" => BookingStatus.Completed,
            _ => Enum.Parse<BookingStatus>(status, true)
        };
}
