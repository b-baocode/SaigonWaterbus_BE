using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Infrastructure.Data.Configurations;

public sealed class BookingTicketConfiguration : IEntityTypeConfiguration<BookingTicket>
{
    public void Configure(EntityTypeBuilder<BookingTicket> builder)
    {
        builder.ToTable("tickets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ticket_id");

        builder.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired();
        builder.Property(x => x.BookingPassengerId).HasColumnName("booking_passenger_id");
        builder.Property(x => x.TicketCode).HasColumnName("ticket_code").HasMaxLength(50).IsRequired();
        builder.Property(x => x.QrToken).HasColumnName("qr_token").HasMaxLength(100).IsRequired();
        builder.Property(x => x.TicketTypeCode).HasColumnName("ticket_type_code").HasMaxLength(50).IsRequired();
        builder.Property(x => x.TicketTypeName).HasColumnName("ticket_type_name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.TicketStatus)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.IssuedAt).HasColumnName("issued_at").IsRequired();
        builder.Property(x => x.CheckedInAt).HasColumnName("checked_in_at");
        builder.Property(x => x.CheckedInByUserId).HasColumnName("checked_in_by_user_id");
        builder.Property(x => x.Created).HasColumnName("created_at");
        builder.Property<DateTimeOffset?>("UpdatedAt").HasColumnName("updated_at");
        builder.Ignore(x => x.CreatedBy);
        builder.Ignore(x => x.LastModified);
        builder.Ignore(x => x.LastModifiedBy);

        builder.HasIndex(x => x.TicketCode).IsUnique();
        builder.HasIndex(x => x.QrToken).IsUnique();
        builder.HasIndex(x => x.BookingId)
            .IsUnique()
            .HasFilter("\"booking_passenger_id\" IS NULL");
        builder.HasIndex(x => x.BookingPassengerId)
            .IsUnique()
            .HasFilter("\"booking_passenger_id\" IS NOT NULL");

        builder.HasOne(x => x.Booking)
            .WithMany(x => x.Tickets)
            .HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.BookingPassenger)
            .WithMany(x => x.Tickets)
            .HasForeignKey(x => x.BookingPassengerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.CheckedInByUser)
            .WithMany()
            .HasForeignKey(x => x.CheckedInByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
