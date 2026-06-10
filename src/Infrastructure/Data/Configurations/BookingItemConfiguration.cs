using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class BookingItemConfiguration : IEntityTypeConfiguration<BookingItem>
{
    public void Configure(EntityTypeBuilder<BookingItem> builder)
    {
        builder.ToTable("booking_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("booking_item_id");

        builder.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired();
        builder.Property(x => x.TripId).HasColumnName("trip_id").IsRequired();
        builder.Property(x => x.TicketTypeId).HasColumnName("ticket_type_id").IsRequired();
        builder.Property(x => x.FromTripStopId).HasColumnName("from_trip_stop_id").IsRequired();
        builder.Property(x => x.ToTripStopId).HasColumnName("to_trip_stop_id").IsRequired();
        builder.Property(x => x.PassengerName).HasColumnName("passenger_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.PassengerPhone).HasColumnName("passenger_phone").HasMaxLength(20);
        builder.Property(x => x.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.ItemStatus).HasColumnName("item_status").HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasOne(x => x.Booking).WithMany(b => b.Items).HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Trip).WithMany(t => t.BookingItems).HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TicketType).WithMany(tt => tt.BookingItems).HasForeignKey(x => x.TicketTypeId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.FromTripStop).WithMany().HasForeignKey(x => x.FromTripStopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ToTripStop).WithMany().HasForeignKey(x => x.ToTripStopId).OnDelete(DeleteBehavior.Restrict);
    }
}
