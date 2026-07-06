using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Tickets;

internal static class TicketScanSupport
{
    public static async Task<Ticket> GetTicketAsync(
        IApplicationDbContext context,
        string codeOrToken,
        CancellationToken cancellationToken)
    {
        var normalizedCodeOrToken = codeOrToken.Trim();
        return await context.Tickets
            .Include(x => x.TicketItem)
                .ThenInclude(x => x!.BookingPassenger)
            .Include(x => x.TicketItem)
                .ThenInclude(x => x!.TripSeat)
                    .ThenInclude(x => x!.Seat)
            .Include(x => x.TicketItem)
                .ThenInclude(x => x!.TicketType)
            .Include(x => x.CheckedInByUser)
            .Include(x => x.CheckedOutByUser)
            .Include(x => x.Booking)
                .ThenInclude(x => x.Passengers)
            .Include(x => x.Booking)
                .ThenInclude(x => x.Trip)
                    .ThenInclude(x => x!.Boat)
            .Include(x => x.Booking)
                .ThenInclude(x => x.Trip)
                    .ThenInclude(x => x!.Route)
                        .ThenInclude(x => x.RouteStops)
                            .ThenInclude(x => x.Station)
            .SingleOrDefaultAsync(
                x => x.TicketCode == normalizedCodeOrToken || x.QrToken == normalizedCodeOrToken,
                cancellationToken)
            ?? throw new NotFoundException("Ticket not found.");
    }

    public static void EnsureCanViewTicket(User currentUser, Ticket ticket)
    {
        if (AuthSupport.IsAdmin(currentUser)
            || AuthSupport.IsManager(currentUser)
            || AuthSupport.IsStaff(currentUser))
        {
            return;
        }

        if (ticket.Booking.UserId != currentUser.Id)
        {
            throw new NotFoundException("Ticket not found.");
        }
    }

    public static async Task<TicketScanDto> ToDtoAsync(
        IApplicationDbContext context,
        Ticket ticket,
        CancellationToken cancellationToken)
    {
        if (Booking.IsCharterBookingType(ticket.Booking.BookingType))
        {
            var charterBooking = await context.Set<Booking>()
                .Include(x => x.Boat)
                .Include(x => x.FromStation)
                .Include(x => x.ToStation)
                .Include(x => x.Passengers)
                .SingleAsync(
                    x => x.Id == ticket.BookingId && x.BookingType == Booking.CharterBookingType,
                    cancellationToken);

            return ToCharterBookingScanDto(ticket, charterBooking);
        }

        return ToBookingScanDto(ticket, ticket.Booking);
    }

    private static TicketScanDto ToCharterBookingScanDto(Ticket ticket, Booking booking)
    {
        var ticketPassenger = ticket.TicketItem?.BookingPassenger;
        var seatCode = ticket.TicketItem?.TripSeat?.Seat?.Code;

        return new TicketScanDto(
            ticket.Id,
            ticket.TicketCode,
            ticket.QrToken,
            ticket.TicketItem?.TicketType?.Code,
            ticket.TicketItem?.TicketType?.Name,
            ticket.TicketStatus.ToString(),
            ticket.IssuedAt,
            ticket.CheckedInAt,
            ticket.CheckedInByUserId,
            ticket.CheckedInByUser?.FullName,
            ticket.CheckedOutAt,
            ticket.CheckedOutByUserId,
            ticket.CheckedOutByUser?.FullName,
            booking.Id,
            booking.BookingCode,
            Booking.CharterBookingType,
            booking.BookingStatus.ToString(),
            booking.PaymentStatus,
            booking.ContactName,
            booking.ContactPhone,
            booking.ContactEmail,
            booking.PassengerCount.GetValueOrDefault(),
            booking.Passengers.Count,
            booking.AdultCount.GetValueOrDefault(),
            booking.ChildCount.GetValueOrDefault(),
            booking.DepartureDate.GetValueOrDefault(),
            booking.StartTime,
            null,
            null,
            null,
            booking.Boat?.Name,
            booking.Boat?.Name,
            booking.FromStation?.StationName,
            booking.ToStation?.StationName,
            seatCode,
            ToPassengerDtoOrNull(ticketPassenger),
            booking.Passengers
                .OrderBy(x => x.FullName)
                .Select(p => ToPassengerDto(p))
                .ToList());
    }

    private static TicketScanDto ToBookingScanDto(Ticket ticket, Booking booking)
    {
        var stops = booking.Trip?.Route.RouteStops
            .OrderBy(x => x.StopOrder)
            .ToArray() ?? [];
        var fromStop = stops.FirstOrDefault();
        var toStop = stops.LastOrDefault();
        var ticketPassenger = ticket.TicketItem?.BookingPassenger;
        var seatCode = ticket.TicketItem?.TripSeat?.Seat?.Code;

        return new TicketScanDto(
            ticket.Id,
            ticket.TicketCode,
            ticket.QrToken,
            ticket.TicketItem?.TicketType?.Code,
            ticket.TicketItem?.TicketType?.Name,
            ticket.TicketStatus.ToString(),
            ticket.IssuedAt,
            ticket.CheckedInAt,
            ticket.CheckedInByUserId,
            ticket.CheckedInByUser?.FullName,
            ticket.CheckedOutAt,
            ticket.CheckedOutByUserId,
            ticket.CheckedOutByUser?.FullName,
            booking.Id,
            booking.BookingCode,
            nameof(Booking),
            booking.BookingStatus.ToString(),
            booking.PaymentStatus,
            booking.ContactName,
            booking.ContactPhone,
            booking.ContactEmail,
            booking.Passengers.Count,
            booking.Passengers.Count,
            null,
            null,
            booking.Trip?.OperatingDate,
            booking.Trip is null ? null : TimeOnly.FromDateTime(booking.Trip.DepartureTime.LocalDateTime),
            booking.Trip?.TripCode,
            booking.Trip?.DepartureTime,
            booking.Trip?.ArrivalTime,
            booking.Trip?.Boat?.Name,
            booking.Trip?.Boat?.Name,
            fromStop?.Station.StationName,
            toStop?.Station.StationName,
            seatCode,
            ToPassengerDtoOrNull(ticketPassenger),
            booking.Passengers
                .OrderBy(x => x.FullName)
                .Select(p => ToPassengerDto(p))
                .ToList());
    }

    private static TicketScanPassengerDto? ToPassengerDtoOrNull(BookingPassenger? passenger) =>
        passenger is null ? null : ToPassengerDto(passenger);

    private static TicketScanPassengerDto ToPassengerDto(BookingPassenger passenger) =>
        new(
            passenger.Id,
            passenger.FullName,
            passenger.PhoneNumber,
            passenger.Email,
            passenger.BirthYear,
            passenger.PassengerType,
            null);
}
