using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

internal static class CharterBookingTicketSupport
{
    private const string PaidBookingPaymentStatus = BookingPaymentStatusExtensions.PaidValue;

    public static async Task<PassengerTicketEnsureResult?> EnsurePassengerTicketsAsync(
        IApplicationDbContext context,
        Booking booking,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(booking.PaymentStatus, PaidBookingPaymentStatus, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(booking.PaymentStatus, BookingPaymentStatusExtensions.DepositPaidValue, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        await EnsureCharterBookingQrTokenAsync(context, booking, cancellationToken);
        await CharterBookingSeatAssignmentSupport.AssignApprovedPassengersAsync(
            context,
            booking,
            cancellationToken);

        var passengers = booking.Passengers
            .Where(CharterBookingPassengerSupport.IsApproved)
            .OrderBy(x => x.FullName)
            .ThenBy(x => x.Id)
            .ToList();

        await CancelBookingLevelTicketsAsync(context, booking.Id, cancellationToken);

        if (passengers.Count == 0)
        {
            return new PassengerTicketEnsureResult([], []);
        }

        var passengerIds = passengers.Select(x => x.Id).ToArray();
        var existingTickets = await context.Tickets
            .Include(x => x.BookingPassenger)
            .Where(x => x.BookingId == booking.Id
                && x.BookingPassengerId.HasValue
                && passengerIds.Contains(x.BookingPassengerId.Value)
                && x.TicketStatus != TicketStatus.Cancelled
                && x.TicketStatus != TicketStatus.Expired)
            .ToListAsync(cancellationToken);
        var ticketedPassengerIds = existingTickets
            .Where(x => x.BookingPassengerId.HasValue)
            .Select(x => x.BookingPassengerId!.Value)
            .ToHashSet();

        var now = timeProvider.GetUtcNow();
        var createdTickets = new List<Ticket>();
        foreach (var passenger in passengers.Where(x => !ticketedPassengerIds.Contains(x.Id)))
        {
            var ticketCode = await TicketIssueSupport.GenerateTicketCodeAsync(context, now, cancellationToken);
            var ticket = new Ticket
            {
                BookingId = booking.Id,
                BookingPassengerId = passenger.Id,
                BookingPassenger = passenger,
                TicketCode = ticketCode,
                QrToken = await TicketIssueSupport.GenerateQrTokenAsync(context, ticketCode, cancellationToken),
                TicketStatus = TicketStatus.Active,
                IssuedAt = now
            };

            context.Tickets.Add(ticket);
            createdTickets.Add(ticket);
        }

        return new PassengerTicketEnsureResult(
            existingTickets.Concat(createdTickets)
                .OrderBy(x => x.BookingPassenger?.FullName)
                .ThenBy(x => x.TicketCode)
                .ToList(),
            createdTickets);
    }

    public static void CancelTicketsBeforeReplacingPassengers(Booking booking)
    {
        if (booking.Tickets.Any(x => x.TicketStatus is TicketStatus.CheckedIn or TicketStatus.CheckedOut))
        {
            throw new ValidationException([new ValidationFailure("tickets",
                "Không thể thay danh sách hành khách khi đã có vé check-in hoặc check-out.")]);
        }

        foreach (var ticket in booking.Tickets.Where(x =>
            x.TicketStatus != TicketStatus.Cancelled
            && x.TicketStatus != TicketStatus.Expired))
        {
            ticket.TicketStatus = TicketStatus.Cancelled;
            ticket.BookingPassengerId = null;
            ticket.BookingPassenger = null;
        }
    }

    public static IReadOnlyList<Ticket> GetDisplayTickets(IEnumerable<Ticket> tickets)
    {
        var currentTickets = tickets
            .Where(x => x.TicketStatus != TicketStatus.Cancelled
                && x.TicketStatus != TicketStatus.Expired)
            .ToList();
        var passengerTickets = currentTickets
            .Where(x => x.BookingPassengerId.HasValue)
            .OrderBy(x => x.BookingPassenger?.FullName)
            .ThenBy(x => x.TicketCode)
            .ToList();

        return passengerTickets.Count > 0
            ? passengerTickets
            : currentTickets
                .Where(x => x.BookingPassengerId == null)
                .OrderByDescending(x => x.IssuedAt)
                .ToList();
    }

    public static CharterBookingTicketDto ToDto(Ticket ticket)
    {
        var passenger = ticket.BookingPassenger;
        return new(
            ticket.Id,
            ticket.TicketCode,
            ticket.QrToken,
            ticket.TicketStatus.ToString(),
            ticket.IssuedAt,
            ticket.CheckedInAt,
            passenger?.Id ?? ticket.BookingPassengerId,
            passenger?.FullName,
            passenger?.BirthYear,
            passenger?.PassengerType,
            passenger?.TripId,
            passenger?.TripSeatId,
            passenger?.TripSeat?.Seat?.Code ?? passenger?.CharterSeat?.Code);
    }

    public static async Task EnsureCharterBookingQrTokenAsync(
        IApplicationDbContext context,
        Booking booking,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(booking.CharterBookingQrToken))
        {
            return;
        }

        booking.CharterBookingQrToken = await GenerateCharterBookingQrTokenAsync(context, booking, cancellationToken);
    }

    private static async Task<string> GenerateCharterBookingQrTokenAsync(
        IApplicationDbContext context,
        Booking booking,
        CancellationToken cancellationToken)
    {
        var token = booking.BookingCode.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ValidationException([new ValidationFailure("charterBookingQrToken",
                "Khong the tao QR token tong khi bookingCode rong.")]);
        }

        if (!await context.Set<Booking>().AnyAsync(x => x.CharterBookingQrToken == token, cancellationToken))
        {
            return token;
        }

        throw new ValidationException([new ValidationFailure("charterBookingQrToken",
            "Khong the tao QR token tong duy nhat.")]);
    }

    private static async Task CancelBookingLevelTicketsAsync(
        IApplicationDbContext context,
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        var bookingLevelTickets = await context.Tickets
            .Where(x => x.BookingId == bookingId
                && x.BookingPassengerId == null
                && x.TicketStatus != TicketStatus.Cancelled
                && x.TicketStatus != TicketStatus.Expired)
            .ToListAsync(cancellationToken);

        foreach (var ticket in bookingLevelTickets)
        {
            ticket.TicketStatus = TicketStatus.Cancelled;
        }
    }
}

internal sealed record PassengerTicketEnsureResult(
    IReadOnlyList<Ticket> Tickets,
    IReadOnlyList<Ticket> CreatedTickets);
