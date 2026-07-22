using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Trips;

public sealed record TripAdminListItemDto(
    Guid TripId,
    string TripCode,
    string RouteCode,
    string RouteName,
    string RouteType,
    DateOnly OperatingDate,
    DateTimeOffset DepartureTime,
    DateTimeOffset ArrivalTime,
    int CapacitySnapshot,
    string TripStatus,
    string? StatusNote,
    string TripType,
    Guid? SourceBookingId,
    int TotalPassengerCount = 0,
    string? SourceBookingCode = null,
    Guid? BoatId = null,
    string? BoatCode = null,
    string? BoatName = null,
    string? BoatStatus = null,
    string? BoatImageUrl = null,
    IReadOnlyList<string>? BoatImageUrls = null,
    TripRouteEndpointDto? FromStation = null,
    TripRouteEndpointDto? ToStation = null,
    int StopCount = 0);

[Authorize(Roles = "Admin,Manager,Staff")]
public sealed record GetTripListQuery(
    DateOnly? OperatingDate,
    string? RouteCode,
    string? Status,
    string? TripType = null,
    string? RouteType = null) : IRequest<IReadOnlyList<TripAdminListItemDto>>;

public sealed class GetTripListQueryValidator : AbstractValidator<GetTripListQuery>
{
    public GetTripListQueryValidator()
    {
        RuleFor(x => x.TripType)
            .Must(x => string.Equals(x, TripTypes.Regular, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x, TripTypes.Charter, StringComparison.OrdinalIgnoreCase))
            .WithMessage($"tripType chi nhan {TripTypes.Regular} hoac {TripTypes.Charter}.")
            .When(x => !string.IsNullOrWhiteSpace(x.TripType));

        RuleFor(x => x.RouteType)
            .Must(RouteTypes.IsValid)
            .WithMessage($"routeType chi nhan {RouteTypes.Regular}, {RouteTypes.SightseeingLoop}, {RouteTypes.Charter} hoac {RouteTypes.CharterReference}.")
            .When(x => !string.IsNullOrWhiteSpace(x.RouteType));
    }
}

public sealed class GetTripListQueryHandler : IRequestHandler<GetTripListQuery, IReadOnlyList<TripAdminListItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public GetTripListQueryHandler(IApplicationDbContext context, TimeProvider? timeProvider = null)
    {
        _context = context;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<TripAdminListItemDto>> Handle(
        GetTripListQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Set<Trip>()
            .AsNoTracking()
            .AsQueryable();

        if (request.OperatingDate.HasValue)
            query = query.Where(t => t.OperatingDate == request.OperatingDate.Value);

        if (!string.IsNullOrWhiteSpace(request.RouteCode))
        {
            var rc = request.RouteCode.Trim().ToUpperInvariant();
            query = query.Where(t => t.Route.RouteCode == rc);
        }

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<TripStatus>(request.Status, ignoreCase: true, out var status))
            query = query.Where(t => t.TripStatus == status);

        if (!string.IsNullOrWhiteSpace(request.TripType))
        {
            var tripType = string.Equals(request.TripType, TripTypes.Charter, StringComparison.OrdinalIgnoreCase)
                ? TripTypes.Charter
                : TripTypes.Regular;
            query = query.Where(t => t.TripType == tripType);
        }

        if (!string.IsNullOrWhiteSpace(request.RouteType))
        {
            var routeType = RouteTypes.Normalize(request.RouteType);
            query = query.Where(t => t.Route.RouteType == routeType);
        }

        var tripRows = await query
            .Include(t => t.Boat)
            .Include(t => t.Route)
                .ThenInclude(r => r.RouteStops)
                    .ThenInclude(rs => rs.Station)
            .Include(t => t.TripStops)
                .ThenInclude(ts => ts.Station)
            .OrderByDescending(t => t.OperatingDate)
            .ThenBy(t => t.DepartureTime)
            .ToListAsync(cancellationToken);

        var sourceBookingIds = tripRows
            .Where(t => t.SourceBookingId.HasValue)
            .Select(t => t.SourceBookingId!.Value)
            .Distinct()
            .ToArray();
        var sourceBookingCodes = sourceBookingIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await _context.Set<Booking>()
                .AsNoTracking()
                .Where(x => sourceBookingIds.Contains(x.Id))
                .Select(x => new { x.Id, x.BookingCode })
                .ToDictionaryAsync(x => x.Id, x => x.BookingCode, cancellationToken);

        var trips = tripRows
            .Select(t =>
            {
                var boatImageUrls = TripMediaSupport.CreateBoatImageUrls(t.Boat);
                return new TripAdminListItemDto(
                t.Id, t.TripCode,
                t.Route.RouteCode, t.Route.RouteName, t.Route.RouteType,
                t.OperatingDate, t.DepartureTime, t.ArrivalTime,
                t.CapacitySnapshot, t.TripStatus.ToString(), t.StatusNote,
                t.TripType, t.SourceBookingId, 0,
                t.SourceBookingId.HasValue ? sourceBookingCodes.GetValueOrDefault(t.SourceBookingId.Value) : null,
                t.BoatId,
                t.Boat?.Code,
                t.Boat?.Name,
                t.Boat?.Status.ToString(),
                boatImageUrls.FirstOrDefault(),
                boatImageUrls,
                TripMediaSupport.ResolveFromStation(t),
                TripMediaSupport.ResolveToStation(t),
                t.TripStops.Count > 0 ? t.TripStops.Count : t.Route.RouteStops.Count);
            })
            .ToList();

        if (trips.Count == 0)
        {
            return trips;
        }

        var now = _timeProvider.GetUtcNow();
        var tripIds = trips.Select(x => x.TripId).ToArray();
        var passengerCounts = await _context.Set<BookingPassenger>()
            .AsNoTracking()
            .Where(x => (x.TripId.HasValue && tripIds.Contains(x.TripId.Value))
                || (!x.TripId.HasValue && x.Booking.TripId.HasValue && tripIds.Contains(x.Booking.TripId.Value)))
            .Where(BookingSeatOccupancySupport.PassengerOccupiesSeat(now))
            .Select(x => new { TripId = x.TripId ?? x.Booking.TripId })
            .Where(x => x.TripId.HasValue)
            .GroupBy(x => x.TripId!.Value)
            .Select(g => new { TripId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TripId, x => x.Count, cancellationToken);

        return trips
            .Select(x => x with { TotalPassengerCount = passengerCounts.GetValueOrDefault(x.TripId) })
            .ToList();
    }
}
