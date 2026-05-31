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

        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.PromotionId).HasColumnName("promotion_id");
        builder.Property(x => x.BookingCode).HasColumnName("booking_code").HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.BookingCode).IsUnique();

        builder.Property(x => x.BookedAt).HasColumnName("booked_at").IsRequired();
        builder.Property(x => x.BookingStatus).HasColumnName("booking_status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.SubtotalAmount).HasColumnName("subtotal_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.DiscountAmount).HasColumnName("discount_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.TotalAmount).HasColumnName("total_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.PointsUsed).HasColumnName("points_used").IsRequired().HasDefaultValue(0);
        builder.Property(x => x.PointsEarned).HasColumnName("points_earned").IsRequired().HasDefaultValue(0);

        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Promotion).WithMany(p => p.Bookings).HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.SetNull);
    }
}
