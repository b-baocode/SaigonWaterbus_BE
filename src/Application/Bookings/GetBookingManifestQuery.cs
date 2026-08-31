using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Tickets;
using SaigonWaterbus.Application.TicketTypes;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Bookings;

public sealed record BookingManifestPassengerDto(
    Guid PassengerId,
    string FullName,
    string? PassengerType,
    string? SeatNumber,
    Guid? TicketId,
    string? TicketCode,
    string? QrToken,
    string? TicketStatus,
    DateTimeOffset? CheckedInAt,
    string? CheckedInByName,
    bool CanCheckIn,
    string? TripCode = null,
    string? FromStationName = null,
    string? ToStationName = null,
    string? BookingCode = null,
    string? TicketTypeCode = null,
    string? TicketTypeName = null,
    decimal? UnitPrice = null,
    Guid? FromStationId = null,
    string? FromStationCode = null,
    Guid? ToStationId = null,
    string? ToStationCode = null,
    DateTimeOffset? ScheduledBoardingAt = null,
    DateTimeOffset? ScheduledAlightingAt = null,
    DateTimeOffset? CheckedOutAt = null,
    string? CheckedOutByName = null,
    bool CanCheckOut = false,
    bool IsLapInfant = false,
    Guid? CompanionPassengerId = null,
    string? CompanionPassengerName = null,
    bool UsesCompanionTicket = false,
    DateTimeOffset? IssuedAt = null,
    int? BirthYear = null,
    string? SeatTypeCode = null,
    string? SeatTypeName = null,
    int? Deck = null);

// Booking khứ hồi: các field Return* mô tả chiều về (null với booking một chiều);
// mỗi passenger mang TripCode của chiều mình thuộc về.
public sealed record BookingManifestDto(
    Guid BookingId,
    string BookingCode,
    string? BookingQrToken,
    string BookingStatus,
    string PaymentStatus,
    string ContactName,
    string ContactPhone,
    string? ContactEmail,
    string? TripCode,
    string? RouteName,
    DateTimeOffset? DepartureTime,
    DateTimeOffset? ArrivalTime,
    string? FromStationName,
    string? ToStationName,
    int PassengerCount,
    int ActiveTicketCount,
    int CheckedInTicketCount,
    IReadOnlyList<BookingManifestPassengerDto> Passengers,
    string? ReturnTripCode = null,
    string? ReturnRouteName = null,
    DateTimeOffset? ReturnDepartureTime = null,
    DateTimeOffset? ReturnArrivalTime = null,
    string? ReturnFromStationName = null,
    string? ReturnToStationName = null,
    string? BoatName = null,
    DateOnly? OperatingDate = null);

public sealed record GetBookingManifestByCodeQuery(string BookingCode) : IRequest<BookingManifestDto>;

public sealed record GetBookingManifestByQrTokenQuery(string QrToken) : IRequest<BookingManifestDto>;

public sealed record GetBookingManifestByCodeOrQrTokenQuery(string CodeOrToken) : IRequest<BookingManifestDto>;

public sealed class GetBookingManifestByCodeQueryValidator : AbstractValidator<GetBookingManifestByCodeQuery>
{
    public GetBookingManifestByCodeQueryValidator()
    {
        RuleFor(x => x.BookingCode).NotEmpty().MaximumLength(50);
    }
}

public sealed class GetBookingManifestByQrTokenQueryValidator : AbstractValidator<GetBookingManifestByQrTokenQuery>
{
    public GetBookingManifestByQrTokenQueryValidator()
    {
        RuleFor(x => x.QrToken).NotEmpty().MaximumLength(100);
    }
}

public sealed class GetBookingManifestByCodeOrQrTokenQueryValidator : AbstractValidator<GetBookingManifestByCodeOrQrTokenQuery>
{
    public GetBookingManifestByCodeOrQrTokenQueryValidator()
    {
        RuleFor(x => x.CodeOrToken).NotEmpty().MaximumLength(100);
    }
}

public sealed class GetBookingManifestQueryHandler :
    IRequestHandler<GetBookingManifestByCodeQuery, BookingManifestDto>,
    IRequestHandler<GetBookingManifestByQrTokenQuery, BookingManifestDto>,
    IRequestHandler<GetBookingManifestByCodeOrQrTokenQuery, BookingManifestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public GetBookingManifestQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<BookingManifestDto> Handle(
        GetBookingManifestByCodeQuery request,
        CancellationToken cancellationToken)
    {
        var normalizedBookingCode = request.BookingCode.Trim().ToUpperInvariant();
        return await GetManifestAsync(
            x => x.BookingCode.ToUpper() == normalizedBookingCode,
            cancellationToken);
    }

    public async Task<BookingManifestDto> Handle(
        GetBookingManifestByQrTokenQuery request,
        CancellationToken cancellationToken)
    {
        var qrToken = request.QrToken.Trim();
        return await GetManifestAsync(
            x => x.CharterBookingQrToken == qrToken,
            cancellationToken);
    }

    public async Task<BookingManifestDto> Handle(
        GetBookingManifestByCodeOrQrTokenQuery request,
        CancellationToken cancellationToken)
    {
        var codeOrToken = request.CodeOrToken.Trim();
        var normalizedBookingCode = codeOrToken.ToUpperInvariant();
        return await GetManifestAsync(
            x => x.CharterBookingQrToken == codeOrToken
                || x.BookingCode.ToUpper() == normalizedBookingCode,
            cancellationToken);
    }

    private async Task<BookingManifestDto> GetManifestAsync(
        System.Linq.Expressions.Expression<Func<Booking, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var currentUser = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);

        var booking = await BookingManifestSupport.FindSeatBookingAsync(_context, predicate, cancellationToken)
            ?? throw new NotFoundException("Booking not found.");

        var now = _timeProvider.GetUtcNow();
        await BookingManifestSupport.EnsureCanViewAsync(
            _context, currentUser, booking, now, cancellationToken);
        return BookingManifestSupport.ToDto(booking, now);
    }
}

internal static class BookingManifestSupport
{
    public static async Task<Booking?> FindSeatBookingAsync(
        IApplicationDbContext context,
        System.Linq.Expressions.Expression<Func<Booking, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var booking = await context.Set<Booking>()
            .AsNoTracking()
            .Where(x => x.BookingType == Booking.SeatBookingType)
            .SingleOrDefaultAsync(predicate, cancellationToken);

        if (booking is not null)
        {
            await LoadRegularBookingManifestGraphAsync(context, booking, cancellationToken);
        }

        return booking;
    }

    public static async Task<Booking> GetByIdAsync(
        IApplicationDbContext context,
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        var booking = await context.Set<Booking>()
            .AsNoTracking()
            .SingleAsync(x => x.Id == bookingId, cancellationToken);

        await LoadRegularBookingManifestGraphAsync(context, booking, cancellationToken);
        return booking;
    }

    private static async Task LoadRegularBookingManifestGraphAsync(
        IApplicationDbContext context,
        Booking booking,
        CancellationToken cancellationToken)
    {
        if (booking.TripId.HasValue)
        {
            booking.Trip = await LoadTripAsync(context, booking.TripId.Value, cancellationToken);
        }

        if (booking.ReturnTripId.HasValue)
        {
            booking.ReturnTrip = booking.ReturnTripId == booking.TripId
                ? booking.Trip
                : await LoadTripAsync(context, booking.ReturnTripId.Value, cancellationToken);
        }

        booking.Passengers = await context.Set<BookingPassenger>()
            .AsNoTracking()
            .Include(x => x.TripSeat)
                .ThenInclude(x => x!.Seat)
                    .ThenInclude(x => x!.SeatType)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Where(x => x.BookingId == booking.Id)
            .ToListAsync(cancellationToken);

        booking.Tickets = await context.Set<Ticket>()
            .AsNoTracking()
            .Include(x => x.CheckedInByUser)
            .Include(x => x.CheckedOutByUser)
            .Where(x => x.BookingId == booking.Id)
            .ToListAsync(cancellationToken);
    }

    private static async Task<Trip?> LoadTripAsync(
        IApplicationDbContext context,
        Guid tripId,
        CancellationToken cancellationToken) =>
        await context.Set<Trip>()
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.Route)
                .ThenInclude(x => x.RouteStops)
                    .ThenInclude(x => x.Station)
            .Include(x => x.TripStops)
            .SingleOrDefaultAsync(x => x.Id == tripId, cancellationToken);

    public static async Task EnsureCanViewAsync(
        IApplicationDbContext context,
        User currentUser,
        Booking booking,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (booking.UserId == currentUser.Id
            || AuthSupport.IsAdmin(currentUser)
            || AuthSupport.IsManager(currentUser))
        {
            return;
        }

        if (AuthSupport.IsStaff(currentUser))
        {
            await TicketStaffScanAuthorizationSupport.EnsureStaffCanOperateAnyTripAsync(
                context,
                currentUser,
                [booking.Trip, booking.ReturnTrip],
                now,
                cancellationToken);
            return;
        }

        throw new NotFoundException("Booking not found.");
    }

    public static bool CanCheckInBooking(Booking booking) =>
        booking.BookingStatus == BookingStatus.Confirmed
        && (string.Equals(booking.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase)
            || booking.RemainingAmount <= 0);

    public static BookingManifestDto ToDto(Booking booking, DateTimeOffset? now = null)
    {
        var passengerById = booking.Passengers.ToDictionary(x => x.Id);
        var companionByPassengerId = LapInfantTicketSupport.AssignCompanionTicketPassengersToAdults(booking.Passengers);
        var currentTickets = booking.Tickets
            .Where(x => x.TicketStatus != TicketStatus.Cancelled && x.TicketStatus != TicketStatus.Expired)
            .Where(x => !x.BookingPassengerId.HasValue
                || !passengerById.TryGetValue(x.BookingPassengerId.Value, out var passenger)
                || LapInfantTicketSupport.RequiresOwnTicket(passenger))
            .ToList();
        var ticketsByPassengerId = currentTickets
            .Where(x => x.BookingPassengerId.HasValue)
            .GroupBy(x => x.BookingPassengerId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.IssuedAt).First());
        var canCheckInBooking = CanCheckInBooking(booking);

        var stops = booking.Trip?.Route.RouteStops.OrderBy(x => x.StopOrder).ToArray() ?? [];
        var fromStop = stops.FirstOrDefault();
        var toStop = stops.LastOrDefault();

        var returnStops = booking.ReturnTrip?.Route.RouteStops.OrderBy(x => x.StopOrder).ToArray() ?? [];
        var returnFromStop = returnStops.FirstOrDefault();
        var returnToStop = returnStops.LastOrDefault();

        // Booking khứ hồi: sắp xếp theo chiều (đi trước, về sau) rồi mới tới ghế.
        var passengers = booking.Passengers
            .OrderBy(x => x.TripId.HasValue && x.TripId == booking.ReturnTripId ? 1 : 0)
            .ThenBy(x => x.TripSeat?.Seat?.Code)
            .ThenBy(x => x.FullName)
            .Select(passenger =>
            {
                var isLapInfant = LapInfantTicketSupport.IsLapInfant(passenger);
                var usesCompanionTicket = LapInfantTicketSupport.UsesCompanionTicket(passenger);
                BookingPassenger? companion = null;
                if (usesCompanionTicket
                    && companionByPassengerId.TryGetValue(passenger.Id, out var companionPassengerId)
                    && passengerById.TryGetValue(companionPassengerId, out var assignedCompanion))
                {
                    companion = assignedCompanion;
                }

                var ticket = companion is not null
                    ? ticketsByPassengerId.GetValueOrDefault(companion.Id)
                    : ticketsByPassengerId.GetValueOrDefault(passenger.Id);
                var isReturnLeg = passenger.TripId.HasValue && passenger.TripId == booking.ReturnTripId;
                var legTrip = isReturnLeg ? booking.ReturnTrip : booking.Trip;
                var legTripCode = legTrip?.TripCode;
                var ticketTypeCode = passenger.PassengerType is null
                    ? null
                    : TicketTypeCatalog.NormalizeCode(passenger.PassengerType);
                var ticketTypeName = TicketTypePricing.TryGet(passenger.PassengerType, out var ticketType)
                    ? ticketType.Name
                    : null;
                var segmentTimes = legTrip is null
                    ? default((DateTimeOffset Departure, DateTimeOffset Arrival)?)
                    : TripStopScheduleSupport.ResolveSegmentTimes(
                        legTrip, passenger.FromStopOrder, passenger.ToStopOrder);
                return new BookingManifestPassengerDto(
                    passenger.Id,
                    passenger.FullName,
                    passenger.PassengerType,
                    passenger.TripSeat?.Seat?.Code,
                    ticket?.Id,
                    ticket?.TicketCode,
                    ticket?.QrToken,
                    ticket?.TicketStatus.ToString(),
                    ticket?.CheckedInAt,
                    ticket?.CheckedInByUser?.FullName,
                    canCheckInBooking
                    && ticket?.TicketStatus == TicketStatus.Active
                    && TicketAttendanceWindowSupport.IsWithinCheckInWindow(booking, passenger, now),
                    legTripCode,
                    passenger.FromStation?.StationName,
                    passenger.ToStation?.StationName,
                    booking.BookingCode,
                    ticketTypeCode,
                    ticketTypeName,
                    passenger.UnitPrice,
                    passenger.FromStationId,
                    passenger.FromStation?.StationCode,
                    passenger.ToStationId,
                    passenger.ToStation?.StationCode,
                    segmentTimes?.Departure,
                    segmentTimes?.Arrival,
                    ticket?.CheckedOutAt,
                    ticket?.CheckedOutByUser?.FullName,
                    canCheckInBooking
                    && ticket?.TicketStatus == TicketStatus.CheckedIn
                    && TicketAttendanceWindowSupport.IsWithinCheckOutWindow(booking, passenger, now),
                    isLapInfant,
                    companion?.Id,
                    companion?.FullName,
                    usesCompanionTicket && companion is not null,
                    ticket?.IssuedAt,
                    passenger.BirthYear,
                    passenger.TripSeat?.Seat?.SeatTypeCode,
                    passenger.TripSeat?.Seat?.SeatType?.Name,
                    passenger.TripSeat?.Seat?.Deck);
            })
            .ToList();

        return new BookingManifestDto(
            booking.Id,
            booking.BookingCode,
            booking.CharterBookingQrToken,
            booking.BookingStatus.ToString(),
            booking.PaymentStatus,
            booking.ContactName,
            booking.ContactPhone,
            booking.ContactEmail,
            booking.Trip?.TripCode,
            booking.Trip?.Route.RouteName,
            booking.Trip?.DepartureTime,
            booking.Trip?.ArrivalTime,
            fromStop?.Station.StationName,
            toStop?.Station.StationName,
            booking.Passengers.Count,
            currentTickets.Count(x => x.TicketStatus == TicketStatus.Active),
            currentTickets.Count(x => x.TicketStatus == TicketStatus.CheckedIn),
            passengers,
            booking.ReturnTrip?.TripCode,
            booking.ReturnTrip?.Route.RouteName,
            booking.ReturnTrip?.DepartureTime,
            booking.ReturnTrip?.ArrivalTime,
            returnFromStop?.Station.StationName,
            returnToStop?.Station.StationName,
            booking.Trip?.Boat?.Name,
            booking.Trip?.OperatingDate);
    }
}
