using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Application.TicketTypes;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CustomBookings;

internal static class CustomBookingTicketSupport
{
    private const string PaidBookingPaymentStatus = "Paid";

    public static async Task<BookingLevelTicketEnsureResult?> EnsureBookingLevelTicketAsync(
        IApplicationDbContext context,
        Booking booking,
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
            return new BookingLevelTicketEnsureResult(existingTicket, false);
        }

        var now = timeProvider.GetUtcNow();
        var ticket = new Ticket
        {
            BookingId = booking.Id,
            TicketCode = await TicketIssueSupport.GenerateTicketCodeAsync(context, now, cancellationToken),
            QrToken = await TicketIssueSupport.GenerateQrTokenAsync(context, cancellationToken),
            TicketTypeCode = TicketTypeCatalog.CustomBookingTicketTypeCode,
            TicketTypeName = TicketTypeCatalog.CustomBookingTicketTypeName,
            TicketStatus = TicketStatus.Active,
            IssuedAt = now
        };

        context.Tickets.Add(ticket);
        return new BookingLevelTicketEnsureResult(ticket, true);
    }

    public static CustomBookingTicketDto ToDto(Ticket ticket) =>
        new(
            ticket.Id,
            ticket.TicketCode,
            ticket.QrToken,
            ticket.TicketTypeCode,
            ticket.TicketTypeName,
            ticket.TicketStatus.ToString(),
            ticket.IssuedAt,
            ticket.CheckedInAt);

}

internal sealed record BookingLevelTicketEnsureResult(
    Ticket Ticket,
    bool Created);
