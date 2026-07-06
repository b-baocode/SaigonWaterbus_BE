using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class BookingPassengerConfiguration : IEntityTypeConfiguration<BookingPassenger>
{
    public void Configure(EntityTypeBuilder<BookingPassenger> builder)
    {
        builder.ToTable("booking_passengers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("booking_passenger_id");

        builder.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired();
        builder.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.PhoneNumber).HasColumnName("phone_number").HasMaxLength(30);
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(255);
        builder.Property(x => x.DateOfBirth).HasColumnName("date_of_birth");
        builder.Property(x => x.BirthYear).HasColumnName("birth_year");
        builder.Property(x => x.Gender).HasColumnName("gender").HasMaxLength(20);
        builder.Property(x => x.Nationality).HasColumnName("nationality").HasMaxLength(100);
        builder.Property(x => x.PassengerType).HasColumnName("passenger_type").HasMaxLength(30);
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(x => x.TripSeatId).HasColumnName("trip_seat_id");
        builder.Property(x => x.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(12,2)");

        builder.HasOne(x => x.Booking).WithMany(x => x.Passengers).HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.TripSeat).WithMany().HasForeignKey(x => x.TripSeatId).OnDelete(DeleteBehavior.SetNull);
    }
}
