using System.Globalization;
using System.Security.Cryptography;
using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.TicketTypes;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Tickets;

internal static class TicketIssueSupport
{
    private const string PassengerTicketTypeCode = "PASSENGER";
    private const string PassengerTicketTypeName = "Ve hanh khach";

    public static async Task<IReadOnlyList<BookingTicket>> EnsureRegularBookingPassengerTicketsAsync(
        IApplicationDbContext context,
        Booking booking,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (booking.BookingType == Booking.CustomBookingType || booking.BookingStatus != BookingStatus.Confirmed)
        {
            return [];
        }

        if (!string.Equals(booking.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase)
            && booking.RemainingAmount > 0)
        {
            return [];
        }

        var passengers = await context.Set<BookingPassenger>()
            .Where(x => x.BookingId == booking.Id)
            .ToListAsync(cancellationToken);
        if (passengers.Count == 0)
        {
            return [];
        }

        var passengerIds = passengers.Select(x => x.Id).ToArray();
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
        var createdTickets = new List<BookingTicket>();
        foreach (var passenger in passengers.Where(x => !ticketedPassengerIds.Contains(x.Id)))
        {
            var (ticketTypeCode, ticketTypeName) = ResolveTicketType(passenger.PassengerType);
            var ticket = new BookingTicket
            {
                BookingId = booking.Id,
                BookingPassengerId = passenger.Id,
                TicketCode = await GenerateTicketCodeAsync(context, now, cancellationToken),
                QrToken = await GenerateQrTokenAsync(context, cancellationToken),
                TicketTypeCode = ticketTypeCode,
                TicketTypeName = ticketTypeName,
                TicketStatus = BookingTicketStatus.Active,
                IssuedAt = now
            };

            context.Tickets.Add(ticket);
            createdTickets.Add(ticket);
        }

        if (createdTickets.Count > 0)
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

    private static (string Code, string Name) ResolveTicketType(string? passengerType)
    {
        if (string.IsNullOrWhiteSpace(passengerType))
        {
            return (PassengerTicketTypeCode, PassengerTicketTypeName);
        }

        var ticketType = TicketTypeCatalog.FindActiveByCode(passengerType);
        if (ticketType is not null)
        {
            return (ticketType.TicketTypeCode, ticketType.TicketTypeName);
        }

        var code = TicketTypeCatalog.NormalizeCode(passengerType);
        return (code, code);
    }
}
