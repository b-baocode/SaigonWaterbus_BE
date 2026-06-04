using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class VesselConfiguration : IEntityTypeConfiguration<Vessel>
{
    public void Configure(EntityTypeBuilder<Vessel> builder)
    {
        builder.ToTable("vessels");

        builder.Property(x => x.Code)
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(x => x.Code)
            .IsUnique();

        builder.Property(x => x.RegistrationNumber)
            .HasMaxLength(50);

        builder.HasIndex(x => x.RegistrationNumber)
            .IsUnique()
            .HasFilter("\"RegistrationNumber\" IS NOT NULL");

        builder.Property(x => x.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired();

        builder.Property(x => x.ImageUrl)
            .HasMaxLength(2048);

        builder.Property(x => x.ImagePublicId)
            .HasMaxLength(255);

        builder.Property(x => x.Description)
            .HasMaxLength(500);

        builder.HasIndex(x => x.WaterbusServiceId);

        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.WaterbusService)
            .WithMany(x => x.Vessels)
            .HasForeignKey(x => x.WaterbusServiceId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
