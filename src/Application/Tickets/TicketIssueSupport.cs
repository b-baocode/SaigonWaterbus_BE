using System.Globalization;
using System.Security.Cryptography;
using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Tickets;

internal static class TicketIssueSupport
{
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

        var ticketItems = await context.Set<TicketItem>()
            .Where(x => x.BookingId == booking.Id)
            .ToListAsync(cancellationToken);
        if (ticketItems.Count == 0)
        {
            return [];
        }

        var ticketItemIds = ticketItems.Select(x => x.Id).ToArray();
        var existingTickets = await context.Tickets
            .Where(x => x.BookingId == booking.Id
                     && x.TicketItemId.HasValue
                     && ticketItemIds.Contains(x.TicketItemId.Value))
            .ToListAsync(cancellationToken);
        var ticketedItemIds = existingTickets
            .Where(x => x.TicketItemId.HasValue)
            .Select(x => x.TicketItemId!.Value)
            .ToHashSet();

        var now = timeProvider.GetUtcNow();
        var createdTickets = new List<Ticket>();
        foreach (var item in ticketItems.Where(x => !ticketedItemIds.Contains(x.Id)))
        {
            var ticket = new Ticket
            {
                BookingId = booking.Id,
                TicketItemId = item.Id,
                TicketCode = await GenerateTicketCodeAsync(context, now, cancellationToken),
                QrToken = await GenerateQrTokenAsync(context, cancellationToken),
                TicketStatus = TicketStatus.Active,
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

}
