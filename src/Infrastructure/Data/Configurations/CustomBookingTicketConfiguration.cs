using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class CustomBookingTicketConfiguration : IEntityTypeConfiguration<CustomBookingTicket>
{
    public void Configure(EntityTypeBuilder<CustomBookingTicket> builder)
    {
        builder.ToTable("custom_booking_tickets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("custom_booking_ticket_id");

        builder.Property(x => x.CustomBookingRequestId).HasColumnName("custom_booking_request_id").IsRequired();
        builder.Property(x => x.TicketCode).HasColumnName("ticket_code").HasMaxLength(50).IsRequired();
        builder.Property(x => x.QrTokenHash).HasColumnName("qr_token_hash").HasMaxLength(512).IsRequired();
        builder.Property(x => x.QrToken).HasColumnName("qr_token").HasMaxLength(256);
        builder.Property(x => x.QrIssuedAt).HasColumnName("qr_issued_at").IsRequired();
        builder.Property(x => x.QrExpiresAt).HasColumnName("qr_expires_at");
        builder.Property(x => x.QrUsedAt).HasColumnName("qr_used_at");
        builder.Property(x => x.QrUsedByUserId).HasColumnName("qr_used_by_user_id");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property(x => x.LastModified).HasColumnName("updated_at").IsConcurrencyToken();
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => x.CustomBookingRequestId);
        builder.HasIndex(x => x.TicketCode).IsUnique();
        builder.HasIndex(x => x.QrTokenHash).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.QrUsedByUserId);

        builder.HasOne(x => x.CustomBookingRequest)
            .WithMany(x => x.Tickets)
            .HasForeignKey(x => x.CustomBookingRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.QrUsedByUser)
            .WithMany()
            .HasForeignKey(x => x.QrUsedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
