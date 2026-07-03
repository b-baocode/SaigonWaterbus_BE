using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class TicketItemConfiguration : IEntityTypeConfiguration<TicketItem>
{
    public void Configure(EntityTypeBuilder<TicketItem> builder)
    {
        builder.ToTable("ticket_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ticket_item_id");

        builder.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired();
        builder.Property(x => x.BookingPassengerId).HasColumnName("booking_passenger_id").IsRequired();
        builder.Property(x => x.TripSeatId).HasColumnName("trip_seat_id");
        builder.Property(x => x.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.TicketTypeId).HasColumnName("ticket_type_id");

        builder.HasOne(x => x.Booking).WithMany(x => x.TicketItems).HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.BookingPassenger).WithMany(x => x.TicketItems).HasForeignKey(x => x.BookingPassengerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.TripSeat).WithMany().HasForeignKey(x => x.TripSeatId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.TicketType).WithMany().HasForeignKey(x => x.TicketTypeId).OnDelete(DeleteBehavior.SetNull);
    }
}
