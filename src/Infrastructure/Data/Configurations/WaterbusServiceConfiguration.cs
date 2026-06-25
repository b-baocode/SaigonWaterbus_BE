using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class WaterbusServiceConfiguration : IEntityTypeConfiguration<WaterbusService>
{
    public void Configure(EntityTypeBuilder<WaterbusService> builder)
    {
        builder.ToTable("services");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("service_id");

        builder.Property(x => x.Code)
            .HasColumnName("service_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(x => x.Code)
            .IsUnique();

        builder.Property(x => x.Name)
            .HasColumnName("service_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasColumnName("description")
            .HasMaxLength(1000);

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.DisplayOrder)
            .HasColumnName("display_order")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.BookingMode)
            .HasColumnName("booking_mode")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Created)
            .HasColumnName("created_at");

        builder.Property<DateTimeOffset?>("UpdatedAt")
            .HasColumnName("updated_at");

        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => x.DisplayOrder);
    }
}
