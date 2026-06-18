using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class CustomBookingPassengerConfiguration : IEntityTypeConfiguration<CustomBookingPassenger>
{
    public void Configure(EntityTypeBuilder<CustomBookingPassenger> builder)
    {
        builder.ToTable("custom_booking_passengers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("custom_booking_passenger_id");

        builder.Property(x => x.CustomBookingRequestId).HasColumnName("custom_booking_request_id").IsRequired();
        builder.Property(x => x.PassengerOrder).HasColumnName("passenger_order").IsRequired();
        builder.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.PassengerType)
            .HasColumnName("passenger_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(x => x.DateOfBirth).HasColumnName("date_of_birth").IsRequired();

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at").IsConcurrencyToken();
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => x.CustomBookingRequestId);
        builder.HasIndex(x => new { x.CustomBookingRequestId, x.PassengerOrder }).IsUnique();

        builder.HasOne(x => x.CustomBookingRequest)
            .WithMany(x => x.Passengers)
            .HasForeignKey(x => x.CustomBookingRequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
