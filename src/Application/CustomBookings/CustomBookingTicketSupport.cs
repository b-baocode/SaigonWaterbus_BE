using System.Globalization;
using System.Security.Cryptography;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.TicketTypes;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CustomBookings;

internal static class CustomBookingTicketSupport
{
    private const string PaidBookingPaymentStatus = "Paid";

    public static async Task<BookingTicket?> EnsureBookingLevelTicketAsync(
        IApplicationDbContext context,
        CustomBooking booking,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var existingTicket = await context.Tickets
            .SingleOrDefaultAsync(x => x.BookingId == booking.Id && x.BookingPassengerId == null, cancellationToken);
        if (existingTicket is not null)
        {
            return existingTicket;
        }

        var now = timeProvider.GetUtcNow();
        var ticket = new BookingTicket
        {
            BookingId = booking.Id,
            TicketCode = await GenerateTicketCodeAsync(context, now, cancellationToken),
            QrToken = await GenerateQrTokenAsync(context, cancellationToken),
            TicketTypeCode = TicketTypeCatalog.CustomBookingTicketTypeCode,
            TicketTypeName = TicketTypeCatalog.CustomBookingTicketTypeName,
            TicketStatus = BookingTicketStatus.Active,
            IssuedAt = now
        };

        context.Tickets.Add(ticket);
        return ticket;
    }

    public static CustomBookingTicketDto ToDto(BookingTicket ticket) =>
        new(
            ticket.Id,
            ticket.TicketCode,
            ticket.QrToken,
            ticket.TicketTypeCode,
            ticket.TicketTypeName,
            ticket.TicketStatus.ToString(),
            ticket.IssuedAt,
            ticket.CheckedInAt);

    private static async Task<string> GenerateTicketCodeAsync(
        IApplicationDbContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var prefix = $"TK{now:yyMMdd}";
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var code = prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
            if (!await context.Tickets.AnyAsync(x => x.TicketCode == code, cancellationToken))
            {
                return code;
            }
        }

        var fallbackCode = prefix + now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        if (!await context.Tickets.AnyAsync(x => x.TicketCode == fallbackCode, cancellationToken))
        {
            return fallbackCode;
        }

        throw new InvalidOperationException("Could not generate a unique ticket code.");
    }

    private static async Task<string> GenerateQrTokenAsync(
        IApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            if (!await context.Tickets.AnyAsync(x => x.QrToken == token, cancellationToken))
            {
                return token;
            }
        }

        throw new InvalidOperationException("Could not generate a unique QR token.");
    }
}
