using System.Globalization;
using System.Security.Cryptography;
using FluentValidation.Results;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Tickets;

internal static class TicketIssueSupport
{
    private const int BookingQrTokenByteCount = 24;

    public static async Task<IReadOnlyList<Ticket>> EnsureRegularBookingPassengerTicketsAsync(
        IApplicationDbContext context,
        Booking booking,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (Booking.IsCharterBookingType(booking.BookingType) || booking.BookingStatus != BookingStatus.Confirmed)
        {
            return [];
        }

        if (!string.Equals(booking.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase)
            && booking.RemainingAmount > 0)
        {
            return [];
        }

        // QR chung của booking: dùng để mở manifest và check-in cả nhóm.
        var bookingQrTokenCreated = await EnsureBookingQrTokenAsync(context, booking, cancellationToken);

        var passengers = await context.Set<BookingPassenger>()
            .Where(x => x.BookingId == booking.Id)
            .ToListAsync(cancellationToken);
        if (passengers.Count == 0)
        {
            return [];
        }

        var ticketablePassengers = passengers
            .Where(LapInfantTicketSupport.RequiresOwnTicket)
            .ToList();
        if (ticketablePassengers.Count == 0)
        {
            if (bookingQrTokenCreated)
            {
                await context.SaveChangesAsync(cancellationToken);
            }

            return [];
        }

        var passengerIds = ticketablePassengers.Select(x => x.Id).ToArray();
        var existingTickets = await context.Tickets
            .Where(x => x.BookingId == booking.Id
                     && x.BookingPassengerId.HasValue
                     && passengerIds.Contains(x.BookingPassengerId.Value))
            .ToListAsync(cancellationToken);
        var ticketedPassengerIds = existingTickets
            .Where(x => x.BookingPassengerId.HasValue)
            .Select(x => x.BookingPassengerId!.Value)
            .ToHashSet();

        var now = timeProvider.GetUtcNow();
        var createdTickets = new List<Ticket>();
        foreach (var passenger in ticketablePassengers.Where(x => !ticketedPassengerIds.Contains(x.Id)))
        {
            var ticket = new Ticket
            {
                BookingId = booking.Id,
                BookingPassengerId = passenger.Id,
                TicketCode = await GenerateTicketCodeAsync(context, now, cancellationToken),
                QrToken = await GenerateQrTokenAsync(context, cancellationToken),
                TicketStatus = TicketStatus.Active,
                IssuedAt = now
            };

            context.Tickets.Add(ticket);
            createdTickets.Add(ticket);
        }

        if (createdTickets.Count > 0 || bookingQrTokenCreated)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return existingTickets.Concat(createdTickets).ToArray();
    }

    public static async Task<string> GenerateTicketCodeAsync(
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

        throw new ValidationException([new ValidationFailure("ticketCode", "Khong the tao ma ve duy nhat.")]);
    }

    public static async Task<string> GenerateQrTokenAsync(
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

        throw new ValidationException([new ValidationFailure("qrToken", "Khong the tao QR token duy nhat.")]);
    }

    /// <summary>
    /// Sinh QR chung cấp booking cho booking thường (lưu vào cột charter_booking_qr_token dùng chung).
    /// Prefix "BK" để phân biệt với QR tổng của charter ("CB").
    /// </summary>
    public static async Task<bool> EnsureBookingQrTokenAsync(
        IApplicationDbContext context,
        Booking booking,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(booking.CharterBookingQrToken))
        {
            return false;
        }

        for (var attempt = 0; attempt < 50; attempt++)
        {
            var token = "BK" + Convert.ToHexString(RandomNumberGenerator.GetBytes(BookingQrTokenByteCount));
            if (!await context.Set<Booking>().AnyAsync(x => x.CharterBookingQrToken == token, cancellationToken))
            {
                booking.CharterBookingQrToken = token;
                return true;
            }
        }

        throw new ValidationException([new ValidationFailure("bookingQrToken",
            "Khong the tao QR chung duy nhat cho booking.")]);
    }
}
