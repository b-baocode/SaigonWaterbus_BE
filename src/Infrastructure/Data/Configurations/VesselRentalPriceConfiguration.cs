using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class VesselRentalPriceConfiguration : IEntityTypeConfiguration<VesselRentalPrice>
{
    public void Configure(EntityTypeBuilder<VesselRentalPrice> builder)
    {
        builder.ToTable("vessel_rental_prices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("vessel_rental_price_id");

        builder.Property(x => x.VesselId).HasColumnName("vessel_id").IsRequired();
        builder.Property(x => x.RentalUnit)
            .HasColumnName("rental_unit")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(x => x.UnitPrice)
            .HasColumnName("unit_price")
            .HasColumnType("numeric(12,2)")
            .IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => new { x.VesselId, x.RentalUnit }).IsUnique();

        builder.HasOne(x => x.Vessel)
            .WithMany(x => x.RentalPrices)
            .HasForeignKey(x => x.VesselId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
