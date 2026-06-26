using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class BoatConfiguration : IEntityTypeConfiguration<Boat>
{
    public void Configure(EntityTypeBuilder<Boat> builder)
    {
        builder.ToTable("boats");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("boat_id");

        builder.Property(x => x.Code)
            .HasColumnName("boat_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(x => x.Code)
            .IsUnique();

        builder.Property(x => x.RegistrationNumber)
            .HasColumnName("registration_number")
            .HasMaxLength(100);

        builder.HasIndex(x => x.RegistrationNumber)
            .IsUnique()
            .HasFilter("\"registration_number\" IS NOT NULL");

        builder.Property(x => x.Name)
            .HasColumnName("boat_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.SeatCount)
            .HasColumnName("seat_count");

        builder.Property(x => x.NumberOfDecks)
            .HasColumnName("number_of_decks");

        builder.Property(x => x.SeatSetupType)
            .HasColumnName("seat_setup_type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.SeatsConfigured)
            .HasColumnName("seats_configured")
            .IsRequired();

        builder.Property(x => x.MaxSpeedKmh)
            .HasColumnName("max_speed_kmh");

        builder.Property(x => x.YearBuilt)
            .HasColumnName("year_built");

        builder.Property(x => x.ImageUrl)
            .HasColumnName("image_url")
            .HasMaxLength(1000);

        builder.Property(x => x.ImagePublicId)
            .HasColumnName("image_public_id")
            .HasMaxLength(500);

        builder.Property(x => x.HourlyRentalPrice)
            .HasColumnName("hourly_rental_price")
            .HasColumnType("numeric(12,2)");

        builder.Property(x => x.DailyRentalPrice)
            .HasColumnName("daily_rental_price")
            .HasColumnType("numeric(12,2)");

        builder.Property(x => x.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasColumnName("description")
            .HasMaxLength(1000);

        builder.Property(x => x.Created)
            .HasColumnName("created_at");

        builder.Property<DateTimeOffset?>("UpdatedAt")
            .HasColumnName("updated_at");

        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => x.Status);
    }
}
