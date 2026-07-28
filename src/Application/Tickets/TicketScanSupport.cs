using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.TicketTypes;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
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
            .Include(x => x.BookingPassenger)
                .ThenInclude(x => x!.TripSeat)
                    .ThenInclude(x => x!.Seat)
            .Include(x => x.BookingPassenger)
                .ThenInclude(x => x!.FromStation)
            .Include(x => x.BookingPassenger)
                .ThenInclude(x => x!.ToStation)
            .Include(x => x.BookingPassenger)
                .ThenInclude(x => x!.Trip)
                    .ThenInclude(x => x!.Boat)
            .Include(x => x.BookingPassenger)
                .ThenInclude(x => x!.Trip)
                    .ThenInclude(x => x!.Route)
                        .ThenInclude(x => x.RouteStops)
                            .ThenInclude(x => x.Station)
            .Include(x => x.BookingPassenger)
                .ThenInclude(x => x!.Trip)
                    .ThenInclude(x => x!.TripStops)
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
            .Include(x => x.Booking)
                .ThenInclude(x => x.Trip)
                    .ThenInclude(x => x!.TripStops)
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

    /// <summary>Loại vé hiển thị của charter theo PassengerType (Adult/Child) — độc lập với danh mục vé booking thường.</summary>
    private static (string? Code, string? Name) ResolveCharterTicketType(string? passengerType) =>
        passengerType?.Trim().ToUpperInvariant() switch
        {
            "ADULT" => ("ADULT", "Vé người lớn"),
            "CHILD" => ("CHILD", "Vé trẻ em"),
            _ => ((string?)null, (string?)null)
        };

    private static TicketScanDto ToCharterBookingScanDto(Ticket ticket, Booking booking)
    {
        var ticketPassenger = ticket.BookingPassenger;
        var seatCode = ticket.BookingPassenger?.TripSeat?.Seat?.Code;
        var (charterTicketTypeCode, charterTicketTypeName) =
            ResolveCharterTicketType(ticket.BookingPassenger?.PassengerType);

        return new TicketScanDto(
            ticket.Id,
            ticket.TicketCode,
            ticket.QrToken,
            charterTicketTypeCode,
            charterTicketTypeName,
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
                .Select(p => ToPassengerDto(p, companion: null))
                .ToList(),
            booking.FromStationId,
            booking.ToStationId,
            booking.BoatId,
            CanCheckIn(ticket, booking),
            CanCheckOut(ticket, booking));
    }

    private static TicketScanDto ToBookingScanDto(Ticket ticket, Booking booking)
    {
        // Booking khứ hồi: trip của vé lấy theo chiều của hành khách (TripId null = dữ liệu cũ → trip đi).
        var trip = ticket.BookingPassenger?.Trip ?? booking.Trip;
        var stops = trip?.Route?.RouteStops
            .OrderBy(x => x.StopOrder)
            .ToArray() ?? [];
        var fromStop = stops.FirstOrDefault();
        var toStop = stops.LastOrDefault();
        var ticketPassenger = ticket.BookingPassenger;
        var seatCode = ticket.BookingPassenger?.TripSeat?.Seat?.Code;
        TicketTypePricing.TryGet(ticket.BookingPassenger?.PassengerType, out var ticketType);
        var ticketTypeCode = string.IsNullOrWhiteSpace(ticketType.Code)
            ? ticket.BookingPassenger?.PassengerType
            : ticketType.Code;
        var ticketTypeName = string.IsNullOrWhiteSpace(ticketType.Name)
            ? ticket.BookingPassenger?.PassengerType
            : ticketType.Name;

        // Ga lên/xuống + giờ đi/đến theo CHẶNG trên vé (ghế bán theo chặng) — staff quét vé phải
        // thấy đúng chỗ khách lên/xuống; dữ liệu cũ chưa lưu chặng thì rơi về đầu/cuối chuyến.
        var fromStationName = ticketPassenger?.FromStation?.StationName ?? fromStop?.Station.StationName;
        var toStationName = ticketPassenger?.ToStation?.StationName ?? toStop?.Station.StationName;
        var fromStationId = ticketPassenger?.FromStationId ?? fromStop?.StationId;
        var toStationId = ticketPassenger?.ToStationId ?? toStop?.StationId;
        var segmentTimes = trip is null
            ? default((DateTimeOffset Departure, DateTimeOffset Arrival)?)
            : Trips.TripStopScheduleSupport.ResolveSegmentTimes(
                trip, ticketPassenger?.FromStopOrder, ticketPassenger?.ToStopOrder);

        // Danh sách hành khách trong DTO chỉ gồm nhóm được đại diện bởi vé được quét:
        // người có ghế + INFANT không ghế đi kèm (nếu có).
        var legPassengers = booking.Passengers
            .Where(p => ticketPassenger?.TripId is null || p.TripId == ticketPassenger.TripId)
            .ToList();
        var representedPassengers = LapInfantTicketSupport.ResolvePassengersRepresentedByTicket(
            legPassengers,
            ticketPassenger);
        var companionByInfantId = LapInfantTicketSupport.AssignInfantsToCompanions(legPassengers);
        var passengerById = legPassengers.ToDictionary(x => x.Id);

        return new TicketScanDto(
            ticket.Id,
            ticket.TicketCode,
            ticket.QrToken,
            ticketTypeCode,
            ticketTypeName,
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
            representedPassengers.Count,
            representedPassengers.Count,
            null,
            null,
            trip?.OperatingDate,
            segmentTimes is null ? null : TimeOnly.FromDateTime(segmentTimes.Value.Departure.LocalDateTime),
            trip?.TripCode,
            segmentTimes?.Departure,
            segmentTimes?.Arrival,
            trip?.Boat?.Name,
            trip?.Boat?.Name,
            fromStationName,
            toStationName,
            seatCode,
            ToPassengerDtoOrNull(ticketPassenger),
            representedPassengers
                .Select(p => ToPassengerDto(
                    p,
                    ResolveCompanion(p, companionByInfantId, passengerById)))
                .ToList(),
            fromStationId,
            toStationId,
            trip?.BoatId,
            CanCheckIn(ticket, booking),
            CanCheckOut(ticket, booking));
    }

    private static bool CanCheckIn(Ticket ticket, Booking booking) =>
        ticket.TicketStatus == TicketStatus.Active
        && booking.BookingStatus == BookingStatus.Confirmed
        && (string.Equals(booking.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase)
            || booking.RemainingAmount <= 0);

    private static bool CanCheckOut(Ticket ticket, Booking booking) =>
        ticket.TicketStatus == TicketStatus.CheckedIn
        && ticket.CheckedInAt.HasValue
        && booking.BookingStatus == BookingStatus.Confirmed;

    private static BookingPassenger? ResolveCompanion(
        BookingPassenger passenger,
        IReadOnlyDictionary<Guid, Guid> companionByInfantId,
        IReadOnlyDictionary<Guid, BookingPassenger> passengerById) =>
        companionByInfantId.TryGetValue(passenger.Id, out var companionPassengerId)
            && passengerById.TryGetValue(companionPassengerId, out var companion)
            ? companion
            : null;

    private static TicketScanPassengerDto? ToPassengerDtoOrNull(BookingPassenger? passenger) =>
        passenger is null ? null : ToPassengerDto(passenger, companion: null);

    private static TicketScanPassengerDto ToPassengerDto(BookingPassenger passenger, BookingPassenger? companion) =>
        new(
            passenger.Id,
            passenger.FullName,
            passenger.PhoneNumber,
            passenger.Email,
            passenger.BirthYear,
            passenger.PassengerType,
            passenger.TripSeat?.Seat?.Code,
            LapInfantTicketSupport.IsLapInfant(passenger),
            companion?.Id,
            companion?.FullName);
}
