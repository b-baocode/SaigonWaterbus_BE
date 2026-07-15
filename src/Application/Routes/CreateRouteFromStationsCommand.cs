using FluentValidation.Results;
using NetTopologySuite.Geometries;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Routes;

public sealed record CreateRouteFromStationsStopRequest(
    Guid StationId,
    int StopOrder,
    int? StandardTravelMin = null);

public sealed record RouteComposedLegDto(
    int LegOrder,
    string FromStationName,
    string ToStationName,
    decimal DistanceKm,
    int TravelMinutes,
    Guid SourceRouteId,
    string SourceRouteCode,
    string SourceRouteName);

public sealed record CreateRouteFromStationsResult(
    RouteDetailDto Route,
    IReadOnlyList<RouteComposedLegDto> SourceLegs);

[Authorize(Roles = "Admin")]
public sealed record CreateRouteFromStationsCommand(
    string RouteCode,
    string RouteName,
    string? Description,
    string? RouteType,
    IReadOnlyList<CreateRouteFromStationsStopRequest> Stops) : IRequest<CreateRouteFromStationsResult>;

public sealed class CreateRouteFromStationsCommandValidator : AbstractValidator<CreateRouteFromStationsCommand>
{
    public CreateRouteFromStationsCommandValidator()
    {
        RuleFor(x => x.RouteCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.RouteName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.RouteType)
            .Must(x => x is null || RouteTypes.IsValid(x))
            .WithMessage($"routeType chi nhan {RouteTypes.Regular}, {RouteTypes.SightseeingLoop}, {RouteTypes.Charter} hoac {RouteTypes.CharterReference}.");
        RuleFor(x => x.Stops)
            .NotNull()
            .Must(x => x is { Count: >= 2 })
            .WithMessage("Can it nhat 2 stops de tao route.")
            .Must(x => x is not null && x.All(stop => stop.StationId != Guid.Empty))
            .WithMessage("stationId khong duoc rong.")
            .Must(x => x is not null && x.Select(stop => stop.StopOrder).Distinct().Count() == x.Count)
            .WithMessage("stopOrder khong duoc trung nhau.")
            .Must(x => x is null || x.All(stop => stop.StandardTravelMin is null or > 0))
            .WithMessage("standardTravelMin phai lon hon 0 neu duoc gui.");
    }
}

public sealed class CreateRouteFromStationsCommandHandler
    : IRequestHandler<CreateRouteFromStationsCommand, CreateRouteFromStationsResult>
{
    private readonly IApplicationDbContext _context;

    public CreateRouteFromStationsCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<CreateRouteFromStationsResult> Handle(
        CreateRouteFromStationsCommand request,
        CancellationToken cancellationToken)
    {
        var code = request.RouteCode.Trim().ToUpperInvariant();
        if (await _context.Set<Route>().AnyAsync(r => r.RouteCode == code, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.RouteCode), "Route code already exists.")
            ]);
        }

        var orderedStops = request.Stops.OrderBy(x => x.StopOrder).ToList();
        EnsureNoConsecutiveDuplicateStations(orderedStops);

        var stations = await LoadStationsAsync(orderedStops, cancellationToken);
        var routeType = ResolveRouteType(request.RouteType, orderedStops);

        var legs = await ComposeLegsAsync(orderedStops, stations, cancellationToken);
        var routeGeometry = BuildComposedGeometry(legs);

        var route = new Route
        {
            RouteCode = code,
            RouteName = request.RouteName.Trim(),
            RouteType = routeType,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            BaseDistanceKm = (decimal)Math.Round(RouteGeoJsonImportSupport.CalculateLengthKm(routeGeometry), 2),
            EstimatedDurationMin = legs.Sum(x => x.TravelMinutes),
            Status = "Active",
            IsBookable = RouteTypes.IsBookableByDefault(routeType),
            RouteGeometry = routeGeometry
        };

        _context.Set<Route>().Add(route);

        var stopDtos = new List<RouteStopDto>();
        for (var i = 0; i < orderedStops.Count; i++)
        {
            var station = stations[orderedStops[i].StationId];
            var routeStop = new RouteStop
            {
                RouteId = route.Id,
                StationId = station.Id,
                StopOrder = i + 1,
                StandardTravelMin = i == 0 ? null : legs[i - 1].TravelMinutes,
                DistanceFromPreviousKm = i == 0 ? null : Math.Round(legs[i - 1].DistanceKm, 3),
                IsPickupAllowed = i < orderedStops.Count - 1,
                IsDropoffAllowed = i > 0
            };

            _context.Set<RouteStop>().Add(routeStop);
            stopDtos.Add(new RouteStopDto(
                routeStop.Id,
                station.Id,
                station.StationCode,
                station.StationName,
                routeStop.StopOrder,
                routeStop.StandardTravelMin,
                routeStop.DistanceFromPreviousKm,
                routeStop.IsPickupAllowed,
                routeStop.IsDropoffAllowed));
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new CreateRouteFromStationsResult(
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
                route.RouteGeometry!.Coordinates
                    .Select(coordinate => new[] { coordinate.X, coordinate.Y })
                    .ToList()),
            legs
                .Select(x => new RouteComposedLegDto(
                    x.LegOrder,
                    x.FromStation.StationName,
                    x.ToStation.StationName,
                    x.DistanceKm,
                    x.TravelMinutes,
                    x.SourceRoute.Id,
                    x.SourceRoute.RouteCode,
                    x.SourceRoute.RouteName))
                .ToList());
    }

    private static void EnsureNoConsecutiveDuplicateStations(IReadOnlyList<CreateRouteFromStationsStopRequest> stops)
    {
        for (var i = 1; i < stops.Count; i++)
        {
            if (stops[i].StationId == stops[i - 1].StationId)
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(CreateRouteFromStationsCommand.Stops),
                        "Hai stop lien tiep khong duoc trung ben.")
                ]);
            }
        }
    }

    private async Task<Dictionary<Guid, Station>> LoadStationsAsync(
        IReadOnlyList<CreateRouteFromStationsStopRequest> stops,
        CancellationToken cancellationToken)
    {
        var stationIds = stops.Select(x => x.StationId).Distinct().ToArray();
        var stations = await _context.Set<Station>()
            .AsNoTracking()
            .Where(x => stationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var missingStationIds = stationIds.Where(id => !stations.ContainsKey(id)).ToArray();
        if (missingStationIds.Length > 0)
        {
            throw new NotFoundException($"Stations not found: {string.Join(", ", missingStationIds)}.");
        }

        return stations;
    }

    private static string ResolveRouteType(
        string? requestedRouteType,
        IReadOnlyList<CreateRouteFromStationsStopRequest> orderedStops)
    {
        var sameTerminal = orderedStops[0].StationId == orderedStops[^1].StationId;
        if (string.IsNullOrWhiteSpace(requestedRouteType))
        {
            return sameTerminal ? RouteTypes.SightseeingLoop : RouteTypes.Regular;
        }

        var routeType = RouteTypes.Normalize(requestedRouteType);
        if (routeType == RouteTypes.Regular && sameTerminal)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateRouteFromStationsCommand.RouteType),
                    "Route Regular khong duoc trung ben dau/cuoi; dung SightseeingLoop, Charter hoac CharterReference.")
            ]);
        }

        return routeType;
    }

    /// <summary>
    /// Moi chang (ben truoc -> ben sau) duoc khop voi mot route Active co geometry va co ca 2 ben
    /// trong RouteStops; geometry chang duoc cat tu geometry route nguon giua 2 diem chieu cua 2 ben.
    /// </summary>
    private async Task<List<ComposedLeg>> ComposeLegsAsync(
        IReadOnlyList<CreateRouteFromStationsStopRequest> orderedStops,
        IReadOnlyDictionary<Guid, Station> stations,
        CancellationToken cancellationToken)
    {
        var stationIds = orderedStops.Select(x => x.StationId).Distinct().ToArray();
        var candidateRoutes = await _context.Set<Route>()
            .AsNoTracking()
            .Include(x => x.RouteStops)
            .Where(x => x.Status == "Active"
                && x.RouteGeometry != null
                && x.RouteStops.Any(stop => stationIds.Contains(stop.StationId)))
            .ToListAsync(cancellationToken);

        var legs = new List<ComposedLeg>();
        var missingLegs = new List<string>();
        for (var i = 1; i < orderedStops.Count; i++)
        {
            var fromStation = stations[orderedStops[i - 1].StationId];
            var toStation = stations[orderedStops[i].StationId];
            var leg = TryComposeLeg(candidateRoutes, fromStation, toStation, i, orderedStops[i].StandardTravelMin);
            if (leg is null)
            {
                missingLegs.Add($"{fromStation.StationName} -> {toStation.StationName}");
                continue;
            }

            legs.Add(leg);
        }

        if (missingLegs.Count > 0)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateRouteFromStationsCommand.Stops),
                    $"Khong tim thay route co geometry cho chang: {string.Join("; ", missingLegs)}. "
                    + "Can co route Active co geometry va co ca 2 ben trong stops (vi du tao qua POST /api/routes/from-gps hoac geojson-import).")
            ]);
        }

        return legs;
    }

    private static ComposedLeg? TryComposeLeg(
        IReadOnlyList<Route> candidateRoutes,
        Station fromStation,
        Station toStation,
        int legOrder,
        int? requestedTravelMin)
    {
        foreach (var route in candidateRoutes)
        {
            if (route.RouteStops.All(stop => stop.StationId != fromStation.Id)
                || route.RouteStops.All(stop => stop.StationId != toStation.Id))
            {
                continue;
            }

            var coordinates = CharterBookingRoutePricingSupport.TryExtractLegGeometry(route, fromStation, toStation);
            if (coordinates is null)
            {
                continue;
            }

            var legLine = new LineString(coordinates) { SRID = 4326 };
            var distanceKm = (decimal)Math.Round(RouteGeoJsonImportSupport.CalculateLengthKm(legLine), 2);
            return new ComposedLeg(
                legOrder,
                fromStation,
                toStation,
                coordinates,
                distanceKm,
                requestedTravelMin ?? CharterBookingRoutePricingSupport.EstimateTravelMinutes(distanceKm),
                route);
        }

        return null;
    }

    private static LineString BuildComposedGeometry(IReadOnlyList<ComposedLeg> legs)
    {
        var coordinates = new List<Coordinate>();
        foreach (var leg in legs)
        {
            foreach (var coordinate in leg.Coordinates)
            {
                if (coordinates.Count == 0 || !coordinates[^1].Equals2D(coordinate))
                {
                    coordinates.Add(coordinate);
                }
            }
        }

        if (coordinates.Count < 2)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateRouteFromStationsCommand.Stops),
                    "Geometry ghep tu cac chang khong hop le (it hon 2 toa do).")
            ]);
        }

        return new LineString([.. coordinates]) { SRID = 4326 };
    }

    private sealed record ComposedLeg(
        int LegOrder,
        Station FromStation,
        Station ToStation,
        Coordinate[] Coordinates,
        decimal DistanceKm,
        int TravelMinutes,
        Route SourceRoute);
}
