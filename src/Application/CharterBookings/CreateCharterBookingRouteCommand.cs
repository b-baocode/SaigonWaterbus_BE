using FluentValidation.Results;
using NetTopologySuite.Geometries;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Routes;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.CharterBookings;

public sealed record CreateCharterBookingRouteRequest(
    string? RouteCode = null,
    string? RouteName = null,
    string? Description = null);

public sealed record CharterBookingRouteSourceLegDto(
    int LegOrder,
    string FromStationName,
    string ToStationName,
    decimal? DistanceKm,
    int? TravelMinutes,
    int StayMinutesAtFromStation,
    Guid? SourceRouteId,
    string? SourceRouteCode,
    string? SourceRouteName);

public sealed record CreateCharterBookingRouteResult(
    Guid BookingId,
    string BookingCode,
    bool RouteAlreadyExisted,
    RouteDetailDto Route,
    IReadOnlyList<CharterBookingRouteSourceLegDto> SourceLegs);

public sealed record CreateCharterBookingRouteCommand(
    Guid BookingId,
    string? RouteCode,
    string? RouteName,
    string? Description) : IRequest<CreateCharterBookingRouteResult>;

public sealed class CreateCharterBookingRouteCommandValidator
    : AbstractValidator<CreateCharterBookingRouteCommand>
{
    public CreateCharterBookingRouteCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.RouteCode).MaximumLength(50);
        RuleFor(x => x.RouteName).MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public sealed class CreateCharterBookingRouteCommandHandler
    : IRequestHandler<CreateCharterBookingRouteCommand, CreateCharterBookingRouteResult>
{
    private const int MaxAutoRouteCodeSuffix = 20;

    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public CreateCharterBookingRouteCommandHandler(IApplicationDbContext context, IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<CreateCharterBookingRouteResult> Handle(
        CreateCharterBookingRouteCommand request,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(_context, _userContext, cancellationToken);

        var booking = await CharterBookingQuerySupport.BuildBaseQuery(_context)
            .Include(x => x.FromStation)
            .Include(x => x.ToStation)
            .Include(x => x.ItineraryStops)
                .ThenInclude(x => x.Station)
            .SingleOrDefaultAsync(x => x.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException("Charter booking not found.");

        var points = BuildRoutePoints(booking);
        EnsureItineraryIsComposable(points);

        var existingRoute = await CharterBookingTripSupport.FindMatchingRouteAsync(
            _context,
            points.Select(x => x.Station.Id).ToArray(),
            cancellationToken);
        if (existingRoute is not null)
        {
            return new CreateCharterBookingRouteResult(
                booking.Id,
                booking.BookingCode,
                RouteAlreadyExisted: true,
                await LoadRouteDetailAsync(existingRoute, cancellationToken),
                []);
        }

        var relatedRoutes = await CharterBookingRoutePricingSupport.LoadRelatedRoutesAsync(
            _context, booking, cancellationToken);
        var estimate = CharterBookingRoutePricingSupport.EstimateRoute(booking, relatedRoutes);
        EnsureEveryLegHasSourceRoute(estimate.Legs);

        var routeGeometry = BuildComposedGeometry(points, estimate.Legs, relatedRoutes);

        var route = new Route
        {
            RouteCode = await ResolveRouteCodeAsync(request.RouteCode, booking.BookingCode, cancellationToken),
            RouteName = ResolveRouteName(request.RouteName, points),
            RouteType = RouteTypes.Charter,
            Description = string.IsNullOrWhiteSpace(request.Description)
                ? $"Route tao tu lo trinh charter booking {booking.BookingCode}."
                : request.Description.Trim(),
            BaseDistanceKm = estimate.TotalDistanceKm,
            EstimatedDurationMin = estimate.EstimatedTravelMinutes + estimate.EstimatedStayMinutes,
            Status = "Active",
            IsBookable = RouteTypes.IsBookableByDefault(RouteTypes.Charter),
            RouteGeometry = routeGeometry
        };

        _context.Set<Route>().Add(route);

        var stopDtos = new List<RouteStopDto>();
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var routeStop = new RouteStop
            {
                RouteId = route.Id,
                StationId = point.Station.Id,
                StopOrder = i + 1,
                // Thoi gian toi ben nay = thoi gian chay chang + thoi gian dung tai ben truoc.
                StandardTravelMin = i == 0
                    ? null
                    : estimate.Legs[i - 1].TravelMinutes!.Value + points[i - 1].StayMinutes,
                IsPickupAllowed = i < points.Count - 1,
                IsDropoffAllowed = i > 0
            };

            _context.Set<RouteStop>().Add(routeStop);
            stopDtos.Add(new RouteStopDto(
                routeStop.Id,
                point.Station.Id,
                point.Station.StationCode,
                point.Station.StationName,
                routeStop.StopOrder,
                routeStop.StandardTravelMin,
                routeStop.IsPickupAllowed,
                routeStop.IsDropoffAllowed));
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new CreateCharterBookingRouteResult(
            booking.Id,
            booking.BookingCode,
            RouteAlreadyExisted: false,
            new RouteDetailDto(
                route.Id,
                route.RouteCode,
                route.RouteName,
                route.RouteType,
                route.Description,
                route.BaseDistanceKm,
                route.EstimatedDurationMin,
                route.Status,
                stopDtos,
                ToGeometryDto(route.RouteGeometry),
                RoutePresentationSupport.ResolveLabel(route.RouteType),
                RoutePresentationSupport.IsSelectableForCharterQuote(route),
                RoutePresentationSupport.IsGeneratedForBooking(route)),
            estimate.Legs
                .Select((leg, index) => new CharterBookingRouteSourceLegDto(
                    leg.LegOrder,
                    leg.FromStationName,
                    leg.ToStationName,
                    leg.DistanceKm,
                    leg.TravelMinutes,
                    points[index].StayMinutes,
                    leg.MatchedRouteId,
                    leg.MatchedRouteCode,
                    leg.MatchedRouteName))
                .ToList());
    }

    /// <summary>Diem lo trinh theo thu tu: ben di -> diem dung (kem thoi gian dung) -> ben den.</summary>
    private static List<RoutePointDraft> BuildRoutePoints(Booking booking)
    {
        var points = new List<RoutePointDraft>();
        if (booking.FromStationId.HasValue)
        {
            points.Add(new RoutePointDraft(booking.FromStation
                ?? throw new NotFoundException("From station of booking not found."), 0));
        }

        foreach (var stop in booking.ItineraryStops.OrderBy(x => x.StopOrder))
        {
            points.Add(new RoutePointDraft(stop.Station
                ?? throw new NotFoundException($"Station of itinerary stop {stop.StopOrder} not found."),
                stop.StayDurationMinutes));
        }

        if (booking.ToStationId.HasValue)
        {
            points.Add(new RoutePointDraft(booking.ToStation
                ?? throw new NotFoundException("To station of booking not found."), 0));
        }

        return points;
    }

    private static void EnsureItineraryIsComposable(IReadOnlyList<RoutePointDraft> points)
    {
        if (points.Count < 2)
        {
            throw new ValidationException([new ValidationFailure("bookingId",
                "Lo trinh cua booking chua du diem (can it nhat ben di va mot diem den) de tao route.")]);
        }

        for (var i = 1; i < points.Count; i++)
        {
            if (points[i].Station.Id == points[i - 1].Station.Id)
            {
                throw new ValidationException([new ValidationFailure("bookingId",
                    $"Lo trinh co hai diem lien tiep trung ben '{points[i].Station.StationName}' nen khong tao duoc route.")]);
            }
        }
    }

    private static void EnsureEveryLegHasSourceRoute(IReadOnlyList<CharterBookingRouteLegEstimate> legs)
    {
        var missingLegs = legs
            .Where(x => !x.MatchedRouteId.HasValue || !x.TravelMinutes.HasValue)
            .Select(x => $"{x.FromStationName} -> {x.ToStationName}")
            .ToArray();

        if (missingLegs.Length > 0)
        {
            throw new ValidationException([new ValidationFailure("bookingId",
                $"Khong tim thay route co san cho chang: {string.Join("; ", missingLegs)}. "
                + "Tao route co GPS/geometry di qua cac ben nay (vi du POST /api/routes/from-gps) roi thu lai.")]);
        }
    }

    /// <summary>
    /// Ghep geometry route moi bang cach cat doan geometry cua route nguon giua 2 ben cua
    /// tung chang roi noi lai. Chang nao khong cat duoc -> loi validation, khong tao route.
    /// </summary>
    private static LineString BuildComposedGeometry(
        IReadOnlyList<RoutePointDraft> points,
        IReadOnlyList<CharterBookingRouteLegEstimate> legs,
        IReadOnlyCollection<Route> relatedRoutes)
    {
        var routesById = relatedRoutes.ToDictionary(x => x.Id);
        var coordinates = new List<Coordinate>();
        var missingLegs = new List<string>();
        for (var i = 0; i < legs.Count; i++)
        {
            var legCoordinates = legs[i].MatchedRouteId.HasValue
                && routesById.TryGetValue(legs[i].MatchedRouteId!.Value, out var sourceRoute)
                    ? CharterBookingRoutePricingSupport.TryExtractLegGeometry(
                        sourceRoute,
                        points[i].Station,
                        points[i + 1].Station)
                    : null;
            if (legCoordinates is null)
            {
                missingLegs.Add($"{legs[i].FromStationName} -> {legs[i].ToStationName}");
                continue;
            }

            foreach (var coordinate in legCoordinates)
            {
                if (coordinates.Count == 0 || !coordinates[^1].Equals2D(coordinate))
                {
                    coordinates.Add(coordinate);
                }
            }
        }

        if (missingLegs.Count > 0 || coordinates.Count < 2)
        {
            var detail = missingLegs.Count > 0
                ? $"Khong cat duoc geometry cho chang: {string.Join("; ", missingLegs)} (route nguon thieu geometry hoac ben cach geometry qua 1km)."
                : "Geometry ghep tu cac chang khong hop le (it hon 2 toa do).";
            throw new ValidationException([new ValidationFailure("bookingId",
                detail + " Route khong duoc tao; bo sung geometry/toa do ben roi thu lai.")]);
        }

        return new LineString([.. coordinates]) { SRID = 4326 };
    }

    private async Task<string> ResolveRouteCodeAsync(
        string? requestedRouteCode,
        string bookingCode,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedRouteCode))
        {
            var code = requestedRouteCode.Trim().ToUpperInvariant();
            if (await RouteCodeExistsAsync(code, cancellationToken))
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(CreateCharterBookingRouteCommand.RouteCode), "Route code already exists.")
                ]);
            }

            return code;
        }

        var baseCode = CharterBookingRouteSupport.BuildCompactRouteCodeBase(bookingCode);
        if (!await RouteCodeExistsAsync(baseCode, cancellationToken))
        {
            return baseCode;
        }

        for (var suffix = 2; suffix <= MaxAutoRouteCodeSuffix; suffix++)
        {
            var candidate = $"{baseCode}-{suffix}";
            if (!await RouteCodeExistsAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new ValidationException([
            new ValidationFailure(nameof(CreateCharterBookingRouteCommand.RouteCode),
                $"Khong tu sinh duoc route code tu '{baseCode}'. Gui routeCode cu the trong body.")
        ]);
    }

    private Task<bool> RouteCodeExistsAsync(string code, CancellationToken cancellationToken) =>
        _context.Set<Route>().AnyAsync(x => x.RouteCode == code, cancellationToken);

    private static string ResolveRouteName(string? requestedRouteName, IReadOnlyList<RoutePointDraft> points)
    {
        if (!string.IsNullOrWhiteSpace(requestedRouteName))
        {
            return requestedRouteName.Trim();
        }

        var name = string.Join(" - ", points.Select(x => x.Station.StationName));
        return name.Length <= 150 ? name : name[..150];
    }

    private async Task<RouteDetailDto> LoadRouteDetailAsync(Route route, CancellationToken cancellationToken)
    {
        var stops = await _context.Set<RouteStop>()
            .AsNoTracking()
            .Include(x => x.Station)
            .Where(x => x.RouteId == route.Id)
            .OrderBy(x => x.StopOrder)
            .ToListAsync(cancellationToken);

        return new RouteDetailDto(
            route.Id,
            route.RouteCode,
            route.RouteName,
            route.RouteType,
            route.Description,
            route.BaseDistanceKm,
            route.EstimatedDurationMin,
            route.Status,
            stops
                .Select(x => new RouteStopDto(
                    x.Id,
                    x.StationId,
                    x.Station.StationCode,
                    x.Station.StationName,
                    x.StopOrder,
                    x.StandardTravelMin,
                    x.IsPickupAllowed,
                    x.IsDropoffAllowed))
                .ToList(),
            ToGeometryDto(route.RouteGeometry),
            RoutePresentationSupport.ResolveLabel(route.RouteType),
            RoutePresentationSupport.IsSelectableForCharterQuote(route),
            RoutePresentationSupport.IsGeneratedForBooking(route));
    }

    private static IReadOnlyList<double[]>? ToGeometryDto(LineString? routeGeometry) =>
        routeGeometry is null
            ? null
            : routeGeometry.Coordinates
                .Select(coordinate => new[] { coordinate.X, coordinate.Y })
                .ToList();

    private sealed record RoutePointDraft(Station Station, int StayMinutes);
}
