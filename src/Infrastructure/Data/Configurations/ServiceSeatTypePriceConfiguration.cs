using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class ServiceSeatTypePriceConfiguration : IEntityTypeConfiguration<ServiceSeatTypePrice>
{
    public void Configure(EntityTypeBuilder<ServiceSeatTypePrice> builder)
    {
        builder.ToTable("service_seat_type_prices");

        builder.Property(x => x.PriceModifier)
            .HasColumnType("numeric(5,2)")
            .HasDefaultValue(1m)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.HasIndex(x => new { x.WaterbusServiceId, x.SeatTypeId })
            .IsUnique();

        builder.HasOne(x => x.WaterbusService)
            .WithMany(x => x.SeatTypePrices)
            .HasForeignKey(x => x.WaterbusServiceId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne(x => x.SeatType)
            .WithMany(x => x.ServicePrices)
            .HasForeignKey(x => x.SeatTypeId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
