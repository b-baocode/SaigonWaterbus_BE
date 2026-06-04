using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class WaterbusServiceConfiguration : IEntityTypeConfiguration<WaterbusService>
{
    public void Configure(EntityTypeBuilder<WaterbusService> builder)
    {
        builder.ToTable("waterbus_services");

        builder.Property(x => x.Code)
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(x => x.Code)
            .IsUnique();

        builder.Property(x => x.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasMaxLength(500);

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.DisplayOrder)
            .HasDefaultValue(0)
            .IsRequired();

        builder.HasIndex(x => x.DisplayOrder);
    }
}
