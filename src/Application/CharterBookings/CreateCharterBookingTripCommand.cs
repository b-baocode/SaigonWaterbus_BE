using FluentValidation.Results;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record CreateCharterBookingTripCommand(Guid BookingId)
    : IRequest<CreateCharterBookingTripResult>;

public sealed class CreateCharterBookingTripCommandValidator
    : AbstractValidator<CreateCharterBookingTripCommand>
{
    public CreateCharterBookingTripCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
    }
}

public sealed class CreateCharterBookingTripCommandHandler
    : IRequestHandler<CreateCharterBookingTripCommand, CreateCharterBookingTripResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;
    private readonly ICharterBookingRealtimeNotifier _realtimeNotifier;

    public CreateCharterBookingTripCommandHandler(
        IApplicationDbContext context,
        IUserContext userContext,
        ICharterBookingRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _userContext = userContext;
        _realtimeNotifier = realtimeNotifier ?? NullCharterBookingRealtimeNotifier.Instance;
    }

    public async Task<CreateCharterBookingTripResult> Handle(
        CreateCharterBookingTripCommand request,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(_context, _userContext, cancellationToken);

        var booking = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .Include(x => x.CharterBoats)
                .ThenInclude(x => x.Boat)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.CharterRoute)
                .ThenInclude(x => x!.RouteStops)
            .Include(x => x.ItineraryStops)
                .ThenInclude(x => x.Station)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        EnsureCanCreateTrip(booking);

        var charterBoats = booking.CharterBoats
            .OrderBy(x => x.BoatOrder)
            .ToList();

        var route = booking.CharterRoute ?? await ResolveLegacyMatchingRouteAsync(booking, cancellationToken);

        var departureTime = CharterBookingTripSupport.ResolveDepartureTimeUtc(booking);
        var relatedRoutes = booking.CharterRoute is not null
            ? [route]
            : await CharterBookingRoutePricingSupport.LoadRelatedRoutesAsync(
                _context, booking, cancellationToken);
        var routeEstimate = CharterBookingRoutePricingSupport.EstimateRoute(booking, relatedRoutes);
        var arrivalTime = CharterBookingTripSupport.ResolveArrivalTimeUtc(
            departureTime,
            booking,
            routeEstimate);
        var tripStopDrafts = CharterBookingTripSupport.BuildTripStopSchedule(
            booking, departureTime, routeEstimate);

        await EnsureBoatsAreFreeAsync(charterBoats, departureTime, arrivalTime, cancellationToken);

        var boatIds = charterBoats.Select(x => x.BoatId).ToArray();
        var activeSeatCounts = await _context.Set<Seat>()
            .Where(x => boatIds.Contains(x.BoatId) && x.IsActive)
            .GroupBy(x => x.BoatId)
            .Select(g => new { BoatId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BoatId, x => x.Count, cancellationToken);

        var trips = new List<(Trip Trip, CharterBookingBoat CharterBoat)>();
        foreach (var charterBoat in charterBoats)
        {
            var trip = new Trip
            {
                RouteId = route.Id,
                BoatId = charterBoat.BoatId,
                TripCode = CharterBookingTripSupport.BuildTripCode(booking, charterBoat.BoatOrder),
                TripType = TripTypes.Charter,
                SourceBookingId = booking.Id,
                OperatingDate = booking.DepartureDate!.Value,
                DepartureTime = departureTime,
                ArrivalTime = arrivalTime,
                CapacitySnapshot = activeSeatCounts.TryGetValue(charterBoat.BoatId, out var seatCount)
                    ? seatCount
                    : charterBoat.Boat?.SeatCount ?? 0,
                TripStatus = TripStatus.Scheduled,
                StatusNote = $"Chuyến thuê trọn tàu của booking {booking.BookingCode}."
            };

            _context.Set<Trip>().Add(trip);
            charterBoat.TripId = trip.Id;
            trips.Add((trip, charterBoat));

            TripStopScheduleSupport.CreateTripStops(trip, tripStopDrafts);
        }

        booking.TripId = trips[0].Trip.Id;

        await _context.SaveChangesAsync(cancellationToken);
        await _realtimeNotifier.PublishChangedAsync(
            new CharterBookingRealtimeEvent(
                booking.Id,
                "TripsCreated",
                booking.BookingStatus.ToString(),
                booking.PaymentStatus),
            cancellationToken);

        return new CreateCharterBookingTripResult(
            booking.Id,
            booking.BookingCode,
            route.Id,
            route.RouteCode,
            route.RouteName,
            trips
                .Select(x => new CharterBookingTripDto(
                    x.Trip.Id,
                    x.Trip.TripCode,
                    x.CharterBoat.BoatId,
                    x.CharterBoat.Boat?.Name,
                    x.CharterBoat.BoatOrder,
                    x.Trip.DepartureTime,
                    x.Trip.ArrivalTime,
                    x.Trip.CapacitySnapshot,
                    x.Trip.TripStatus.ToString()))
                .ToList(),
            tripStopDrafts
                .Select(x => new CharterBookingTripStopDto(
                    x.StationId,
                    x.Station?.StationName,
                    x.StopOrder,
                    x.StayDurationMinutes,
                    x.PlannedArrivalTime,
                    x.PlannedDepartureTime,
                    x.Note))
                .ToList());
    }

    private static void EnsureCanCreateTrip(Booking booking)
    {
        if (booking.BookingStatus != BookingStatus.Confirmed)
        {
            throw new ValidationException([new ValidationFailure("bookingStatus",
                "Chỉ tạo được trip cho charter booking đã Confirmed (đã thanh toán cọc hoặc đủ).")]);
        }

        if (!booking.DepartureDate.HasValue)
        {
            throw new ValidationException([new ValidationFailure("departureDate",
                "Charter booking chưa có ngày khởi hành.")]);
        }

        if (booking.CharterBoats.Count == 0)
        {
            throw new ValidationException([new ValidationFailure("boats",
                "Charter booking chưa có tàu được chốt qua bước quote.")]);
        }

        if (booking.TripId.HasValue || booking.CharterBoats.Any(x => x.TripId.HasValue))
        {
            throw new ValidationException([new ValidationFailure("bookingId",
                "Charter booking đã có trip.")]);
        }
    }

    private async Task<Route> ResolveLegacyMatchingRouteAsync(
        Booking booking,
        CancellationToken cancellationToken)
    {
        var stationSequence = CharterBookingTripSupport.BuildStationSequence(booking);
        if (stationSequence.Count < 2)
        {
            throw new ValidationException([new ValidationFailure("bookingId",
                "Lộ trình của booking chưa đủ điểm (cần ít nhất bến đi và một điểm đến) để khớp route.")]);
        }

        return await CharterBookingTripSupport.FindMatchingRouteAsync(
                _context,
                stationSequence,
                cancellationToken)
            ?? throw new ValidationException([new ValidationFailure("routeId",
                "Không có route nào khớp với lộ trình của booking. "
                + "Tạo route (ví dụ qua POST /api/routes/from-gps) có các bến đúng thứ tự lộ trình rồi thử lại.")]);
    }

    /// <summary>
    /// Moi tau cua booking khong duoc trung gio voi bat ky trip nao khac (charter lan tuyen thuong),
    /// tinh ca thoi gian quay dau toi thieu.
    /// </summary>
    private async Task EnsureBoatsAreFreeAsync(
        IReadOnlyList<CharterBookingBoat> charterBoats,
        DateTimeOffset departureTime,
        DateTimeOffset arrivalTime,
        CancellationToken cancellationToken)
    {
        var bufferedArrival = arrivalTime.Add(TripScheduleSupport.BoatTurnaroundBuffer);
        var bufferedDeparture = departureTime.Subtract(TripScheduleSupport.BoatTurnaroundBuffer);

        foreach (var charterBoat in charterBoats)
        {
            var conflict = await _context.Set<Trip>()
                .AsNoTracking()
                .Where(x => x.BoatId == charterBoat.BoatId
                    && x.TripStatus != TripStatus.Cancelled
                    && x.DepartureTime < bufferedArrival
                    && bufferedDeparture < x.ArrivalTime)
                .OrderBy(x => x.DepartureTime)
                .Select(x => new { x.TripCode, x.DepartureTime, x.ArrivalTime })
                .FirstOrDefaultAsync(cancellationToken);

            if (conflict is not null)
            {
                throw new ValidationException([new ValidationFailure("boats",
                    "Tàu đã có chuyến vào ngày/giờ charter đã chọn: "
                    + TripScheduleSupport.BuildConflictMessage(
                        conflict.TripCode, conflict.DepartureTime, conflict.ArrivalTime))]);
            }
        }
    }
}
