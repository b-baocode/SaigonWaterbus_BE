using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class CustomBookingOperationServiceConfiguration
    : IEntityTypeConfiguration<CustomBookingOperationService>
{
    public void Configure(EntityTypeBuilder<CustomBookingOperationService> builder)
    {
        builder.ToTable("custom_booking_operation_services");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("custom_booking_operation_service_id");
        builder.Property(x => x.CustomBookingRequestId).HasColumnName("custom_booking_request_id").IsRequired();
        builder.Property(x => x.ServiceName).HasColumnName("service_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity").IsRequired();
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => x.CustomBookingRequestId);

        builder.HasOne(x => x.CustomBookingRequest)
            .WithMany(x => x.OperationServices)
            .HasForeignKey(x => x.CustomBookingRequestId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
