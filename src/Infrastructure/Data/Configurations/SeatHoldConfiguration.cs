using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class SeatHoldConfiguration : IEntityTypeConfiguration<SeatHold>
{
    public void Configure(EntityTypeBuilder<SeatHold> builder)
    {
        builder.ToTable("seat_holds");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("seat_hold_id");

        builder.Property(x => x.TripId).HasColumnName("trip_id").IsRequired();
        builder.Property(x => x.SeatId).HasColumnName("seat_id").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.HeldAt).HasColumnName("held_at").IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.HoldStatus).HasColumnName("hold_status").HasConversion<string>().HasMaxLength(20).IsRequired();

        // Partial unique: chỉ 1 hold Active cho mỗi (trip, seat)
        builder.HasIndex(x => new { x.TripId, x.SeatId })
            .IsUnique()
            .HasFilter($"hold_status = '{nameof(SeatHoldStatus.Active)}'");

        builder.HasOne(x => x.Trip).WithMany(t => t.SeatHolds).HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Seat).WithMany(s => s.SeatHolds).HasForeignKey(x => x.SeatId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
