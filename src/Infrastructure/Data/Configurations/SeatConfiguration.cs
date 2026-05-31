using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> builder)
    {
        builder.ToTable("seats");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("seat_id");

        builder.Property(x => x.BoatId).HasColumnName("boat_id").IsRequired();
        builder.Property(x => x.SeatNumber).HasColumnName("seat_number").HasMaxLength(20).IsRequired();
        builder.Property(x => x.SeatClass).HasColumnName("seat_class").HasMaxLength(30);
        builder.Property(x => x.SeatRow).HasColumnName("seat_row");
        builder.Property(x => x.SeatColumn).HasColumnName("seat_column");
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

        builder.HasIndex(x => new { x.BoatId, x.SeatNumber }).IsUnique();

        builder.HasOne(x => x.Boat).WithMany(b => b.Seats).HasForeignKey(x => x.BoatId).OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(x => x.Created);
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);
    }
}
