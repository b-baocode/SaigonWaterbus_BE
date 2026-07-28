using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Trips;

public sealed record TripPassengerManifestDto(
    Guid TripId,
    string TripCode,
    string RouteName,
    string RouteType,
    string TripType,
    Guid? BoatId,
    string? BoatCode,
    string? BoatName,
    DateTimeOffset DepartureTime,
    DateTimeOffset ArrivalTime,
    int PassengerCount,
    int ActiveTicketCount,
    int CheckedInTicketCount,
    int CheckedOutTicketCount,
    IReadOnlyList<TripPassengerManifestItemDto> Passengers);

public sealed record TripPassengerManifestItemDto(
    Guid PassengerId,
    Guid BookingId,
    string BookingCode,
    string BookingStatus,
    string PaymentStatus,
    string ContactName,
    string ContactPhone,
    string? ContactEmail,
    string FullName,
    string? PhoneNumber,
    string? Email,
    int? BirthYear,
    string? PassengerType,
    string? SeatNumber,
    decimal? UnitPrice,
    Guid? FromStationId,
    string? FromStationCode,
    string? FromStationName,
    int? FromStopOrder,
    DateTimeOffset? ScheduledBoardingAt,
    Guid? ToStationId,
    string? ToStationCode,
    string? ToStationName,
    int? ToStopOrder,
    DateTimeOffset? ScheduledAlightingAt,
    Guid? TicketId,
    string? TicketCode,
    string? TicketQrToken,
    string? TicketStatus,
    DateTimeOffset? CheckedInAt,
    DateTimeOffset? CheckedOutAt,
    bool CanCheckIn);

public sealed record GetTripPassengerManifestQuery(Guid TripId) : IRequest<TripPassengerManifestDto>;

public sealed class GetTripPassengerManifestQueryHandler
    : IRequestHandler<GetTripPassengerManifestQuery, TripPassengerManifestDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public GetTripPassengerManifestQueryHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<TripPassengerManifestDto> Handle(
        GetTripPassengerManifestQuery request,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        if (!AuthSupport.IsAdmin(actor) && !AuthSupport.IsManager(actor) && !AuthSupport.IsStaff(actor))
        {
            throw new ForbiddenAccessException();
        }

        var trip = await _context.Set<Trip>()
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.Route)
                .ThenInclude(x => x.RouteStops)
                    .ThenInclude(x => x.Station)
            .Include(x => x.TripStops)
                .ThenInclude(x => x.Station)
            .SingleOrDefaultAsync(x => x.Id == request.TripId, cancellationToken)
            ?? throw new NotFoundException("Trip not found.");

        var now = _timeProvider.GetUtcNow();
        var passengers = await _context.Set<BookingPassenger>()
            .AsNoTracking()
            .Include(x => x.Booking)
            .Include(x => x.TripSeat)
                .ThenInclude(x => x!.Seat)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.Tickets)
            .Where(x => x.Booking.BookingType == Booking.SeatBookingType)
            .Where(x => x.TripId == trip.Id || (!x.TripId.HasValue && x.Booking.TripId == trip.Id))
            .Where(BookingSeatOccupancySupport.PassengerOccupiesSeat(now))
            .ToListAsync(cancellationToken);

        var orderedStops = trip.TripStops
            .OrderBy(x => x.StopOrder)
            .ToArray();
        var orderedRouteStops = trip.Route.RouteStops
            .OrderBy(x => x.StopOrder)
            .ToArray();
        var firstEndpoint = ResolveEndpoint(orderedStops, orderedRouteStops, null, orderedRouteStops.FirstOrDefault()?.StopOrder);
        var lastEndpoint = ResolveEndpoint(orderedStops, orderedRouteStops, null, orderedRouteStops.LastOrDefault()?.StopOrder);

        var passengerDtos = passengers
            .Select(passenger =>
            {
                var currentTicket = passenger.Tickets
                    .Where(x => x.TicketStatus != TicketStatus.Cancelled && x.TicketStatus != TicketStatus.Expired)
                    .OrderByDescending(x => x.IssuedAt)
                    .FirstOrDefault();
                var from = ResolveEndpoint(
                    orderedStops,
                    orderedRouteStops,
                    passenger.FromStation,
                    passenger.FromStopOrder) ?? firstEndpoint;
                var to = ResolveEndpoint(
                    orderedStops,
                    orderedRouteStops,
                    passenger.ToStation,
                    passenger.ToStopOrder) ?? lastEndpoint;
                var canCheckIn = BookingManifestSupport.CanCheckInBooking(passenger.Booking)
                    && currentTicket?.TicketStatus == TicketStatus.Active;

                return new TripPassengerManifestItemDto(
                    passenger.Id,
                    passenger.BookingId,
                    passenger.Booking.BookingCode,
                    passenger.Booking.BookingStatus.ToString(),
                    passenger.Booking.PaymentStatus,
                    passenger.Booking.ContactName,
                    passenger.Booking.ContactPhone,
                    passenger.Booking.ContactEmail,
                    passenger.FullName,
                    passenger.PhoneNumber,
                    passenger.Email,
                    passenger.BirthYear,
                    passenger.PassengerType,
                    passenger.TripSeat?.Seat?.Code,
                    passenger.UnitPrice,
                    from?.StationId,
                    from?.StationCode,
                    from?.StationName,
                    passenger.FromStopOrder ?? from?.StopOrder,
                    from?.ScheduledDepartureAt ?? from?.ScheduledArrivalAt,
                    to?.StationId,
                    to?.StationCode,
                    to?.StationName,
                    passenger.ToStopOrder ?? to?.StopOrder,
                    to?.ScheduledArrivalAt ?? to?.ScheduledDepartureAt,
                    currentTicket?.Id,
                    currentTicket?.TicketCode,
                    currentTicket?.QrToken,
                    currentTicket?.TicketStatus.ToString(),
                    currentTicket?.CheckedInAt,
                    currentTicket?.CheckedOutAt,
                    canCheckIn);
            })
            .OrderBy(x => x.FromStopOrder ?? int.MaxValue)
            .ThenBy(x => x.SeatNumber)
            .ThenBy(x => x.FullName)
            .ToArray();

        return new TripPassengerManifestDto(
            trip.Id,
            trip.TripCode,
            trip.Route.RouteName,
            trip.Route.RouteType,
            trip.TripType,
            trip.BoatId,
            trip.Boat?.Code,
            trip.Boat?.Name,
            trip.DepartureTime,
            trip.ArrivalTime,
            passengerDtos.Length,
            passengerDtos.Count(x => x.TicketStatus == TicketStatus.Active.ToString()),
            passengerDtos.Count(x => x.TicketStatus == TicketStatus.CheckedIn.ToString()),
            passengerDtos.Count(x => x.TicketStatus == TicketStatus.CheckedOut.ToString()),
            passengerDtos);
    }

    private static TripPassengerStationEndpointDto? ResolveEndpoint(
        IReadOnlyList<TripStop> tripStops,
        IReadOnlyList<RouteStop> routeStops,
        Station? passengerStation,
        int? stopOrder)
    {
        var tripStop = stopOrder.HasValue
            ? tripStops.FirstOrDefault(x => x.StopOrder == stopOrder.Value)
            : passengerStation is null
                ? null
                : tripStops.FirstOrDefault(x => x.StationId == passengerStation.Id);
        var routeStop = stopOrder.HasValue
            ? routeStops.FirstOrDefault(x => x.StopOrder == stopOrder.Value)
            : passengerStation is null
                ? null
                : routeStops.FirstOrDefault(x => x.StationId == passengerStation.Id);

        var stationId = tripStop?.StationId ?? passengerStation?.Id ?? routeStop?.StationId;
        if (!stationId.HasValue)
        {
            return null;
        }

        return new TripPassengerStationEndpointDto(
            stationId.Value,
            tripStop?.Station?.StationCode ?? passengerStation?.StationCode ?? routeStop?.Station?.StationCode,
            tripStop?.Station?.StationName ?? passengerStation?.StationName ?? routeStop?.Station?.StationName,
            tripStop?.StopOrder ?? routeStop?.StopOrder,
            tripStop?.PlannedArrivalTime,
            tripStop?.PlannedDepartureTime,
            tripStop?.AdjustedArrivalTime,
            tripStop?.AdjustedDepartureTime);
    }

    private sealed record TripPassengerStationEndpointDto(
        Guid StationId,
        string? StationCode,
        string? StationName,
        int? StopOrder,
        DateTimeOffset? PlannedArrivalAt,
        DateTimeOffset? PlannedDepartureAt,
        DateTimeOffset? AdjustedArrivalAt,
        DateTimeOffset? AdjustedDepartureAt)
    {
        public DateTimeOffset? ScheduledArrivalAt => AdjustedArrivalAt ?? PlannedArrivalAt;

        public DateTimeOffset? ScheduledDepartureAt => AdjustedDepartureAt ?? PlannedDepartureAt;
    }
}
