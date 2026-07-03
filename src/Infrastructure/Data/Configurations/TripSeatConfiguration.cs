using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class TripSeatConfiguration : IEntityTypeConfiguration<TripSeat>
{
    public void Configure(EntityTypeBuilder<TripSeat> builder)
    {
        builder.ToTable("trip_seats");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("trip_seat_id");

        builder.Property(x => x.TripId).HasColumnName("trip_id").IsRequired();
        builder.Property(x => x.SeatId).HasColumnName("seat_id").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Price).HasColumnName("price").HasColumnType("numeric(12,2)");

        builder.HasIndex(x => new { x.TripId, x.SeatId }).IsUnique();

        builder.HasOne(x => x.Trip)
            .WithMany(t => t.TripSeats)
            .HasForeignKey(x => x.TripId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Seat)
            .WithMany()
            .HasForeignKey(x => x.SeatId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
