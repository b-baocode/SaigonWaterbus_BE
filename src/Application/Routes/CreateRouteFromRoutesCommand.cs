using FluentValidation.Results;
using NetTopologySuite.Geometries;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Routes;

[Authorize(Roles = "Admin")]
public sealed record CreateRouteFromRoutesCommand(
    string RouteCode,
    string RouteName,
    string? Description,
    IReadOnlyList<Guid> SourceRouteIds) : IRequest<RouteDetailDto>;

public sealed class CreateRouteFromRoutesCommandValidator : AbstractValidator<CreateRouteFromRoutesCommand>
{
    public CreateRouteFromRoutesCommandValidator()
    {
        RuleFor(x => x.RouteCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.RouteName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.SourceRouteIds)
            .NotNull()
            .Must(x => x is { Count: > 0 })
            .WithMessage("Can gui it nhat 1 sourceRouteId.")
            .Must(x => x is not null && x.All(id => id != Guid.Empty))
            .WithMessage("sourceRouteIds khong duoc chua Guid rong.")
            .Must(x => x is not null && x.Count == x.Distinct().Count())
            .WithMessage("sourceRouteIds khong duoc trung nhau.");
    }
}

public sealed class CreateRouteFromRoutesCommandHandler : IRequestHandler<CreateRouteFromRoutesCommand, RouteDetailDto>
{
    private readonly IApplicationDbContext _context;

    public CreateRouteFromRoutesCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<RouteDetailDto> Handle(CreateRouteFromRoutesCommand request, CancellationToken cancellationToken)
    {
        var code = request.RouteCode.Trim().ToUpperInvariant();
        const string routeType = RouteTypes.Regular;

        if (await _context.Set<Route>().AnyAsync(r => r.RouteCode == code, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.RouteCode), "Route code already exists.")
            ]);
        }

        var sourceRoutes = await _context.Set<Route>()
            .Include(route => route.RouteStops)
                .ThenInclude(stop => stop.Station)
            .Where(route => request.SourceRouteIds.Contains(route.Id))
            .ToListAsync(cancellationToken);

        var routesById = sourceRoutes.ToDictionary(route => route.Id);
        var missingRouteIds = request.SourceRouteIds.Where(id => !routesById.ContainsKey(id)).ToArray();
        if (missingRouteIds.Length > 0)
        {
            throw new NotFoundException($"Source routes not found: {string.Join(", ", missingRouteIds)}.");
        }

        var orderedSources = request.SourceRouteIds.Select(id => routesById[id]).ToList();
        EnsureSourceRoutesCanBeComposed(orderedSources);
        var composedStops = BuildComposedStops(orderedSources);
        EnsureRegularRouteShapeIsValid(composedStops);

        var routeGeometry = BuildComposedGeometry(orderedSources);
        var baseDistanceKm = routeGeometry is not null
            ? (decimal)Math.Round(RouteGeoJsonImportSupport.CalculateLengthKm(routeGeometry), 2)
            : SumIfAllPresent(orderedSources.Select(route => route.BaseDistanceKm));
        var estimatedDurationMin = SumComposedTravelMinutes(composedStops)
            ?? SumIfAllPresent(orderedSources.Select(route => route.EstimatedDurationMin));

        var route = new Route
        {
            RouteCode = code,
            RouteName = request.RouteName.Trim(),
            RouteType = routeType,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            BaseDistanceKm = baseDistanceKm,
            EstimatedDurationMin = estimatedDurationMin,
            Status = "Active",
            IsBookable = RouteTypes.IsBookableByDefault(routeType),
            RouteGeometry = routeGeometry
        };

        _context.Set<Route>().Add(route);

        var stopDtos = new List<RouteStopDto>();
        for (var i = 0; i < composedStops.Count; i++)
        {
            var draft = composedStops[i];
            var routeStop = new RouteStop
            {
                RouteId = route.Id,
                StationId = draft.Station.Id,
                StopOrder = i + 1,
                StandardTravelMin = i == 0 ? null : draft.StandardTravelMin,
                DistanceFromPreviousKm = i == 0 ? null : draft.DistanceFromPreviousKm,
                IsPickupAllowed = i < composedStops.Count - 1,
                IsDropoffAllowed = i > 0
            };

            _context.Set<RouteStop>().Add(routeStop);
            stopDtos.Add(new RouteStopDto(
                routeStop.Id,
                draft.Station.Id,
                draft.Station.StationCode,
                draft.Station.StationName,
                routeStop.StopOrder,
                routeStop.StandardTravelMin,
                routeStop.DistanceFromPreviousKm,
                routeStop.IsPickupAllowed,
                routeStop.IsDropoffAllowed));
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new RouteDetailDto(
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
            RoutePresentationSupport.IsGeneratedForBooking(route));
    }

    private static void EnsureSourceRoutesCanBeComposed(IReadOnlyList<Route> sourceRoutes)
    {
        var loopRoute = sourceRoutes.FirstOrDefault(route =>
            string.Equals(route.RouteType, RouteTypes.SightseeingLoop, StringComparison.OrdinalIgnoreCase));
        if (loopRoute is not null)
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(CreateRouteFromRoutesCommand.SourceRouteIds),
                    $"Route '{loopRoute.RouteCode}' la SightseeingLoop nen khong dung de ghep route booking thuong.")
            ]);
        }
    }

    private static List<RouteStopDraft> BuildComposedStops(IReadOnlyList<Route> sourceRoutes)
    {
        var composedStops = new List<RouteStopDraft>();
        for (var routeIndex = 0; routeIndex < sourceRoutes.Count; routeIndex++)
        {
            var route = sourceRoutes[routeIndex];
            var stops = route.RouteStops
                .OrderBy(stop => stop.StopOrder)
                .ToList();

            if (stops.Count < 2)
            {
                throw new ValidationException([
                    new ValidationFailure(
                        nameof(CreateRouteFromRoutesCommand.SourceRouteIds),
                        $"Route '{route.RouteCode}' phai co it nhat 2 stops de ghep luong.")
                ]);
            }

            if (routeIndex > 0)
            {
                var previousStationId = composedStops[^1].Station.Id;
                var currentStartStationId = stops[0].StationId;
                if (previousStationId != currentStartStationId)
                {
                    throw new ValidationException([
                        new ValidationFailure(
                            nameof(CreateRouteFromRoutesCommand.SourceRouteIds),
                            $"Khong the noi route '{sourceRoutes[routeIndex - 1].RouteCode}' voi '{route.RouteCode}': ben cuoi cua route truoc phai trung ben dau cua route sau.")
                    ]);
                }
            }

            var startIndex = routeIndex == 0 ? 0 : 1;
            for (var stopIndex = startIndex; stopIndex < stops.Count; stopIndex++)
            {
                var stop = stops[stopIndex];
                composedStops.Add(new RouteStopDraft(stop.Station, stop.StandardTravelMin, stop.DistanceFromPreviousKm));
            }
        }

        return composedStops;
    }

    private static void EnsureRegularRouteShapeIsValid(IReadOnlyList<RouteStopDraft> stops)
    {
        if (stops.Count < 2)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateRouteFromRoutesCommand.SourceRouteIds), "Can it nhat 2 stops de tao route.")
            ]);
        }

        var sameTerminal = stops[0].Station.Id == stops[^1].Station.Id;
        if (sameTerminal)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateRouteFromRoutesCommand.SourceRouteIds), "Route ghep GPS la route Regular nen khong duoc trung ben dau/cuoi.")
            ]);
        }
    }

    private static LineString? BuildComposedGeometry(IReadOnlyList<Route> sourceRoutes)
    {
        if (sourceRoutes.Any(route => route.RouteGeometry is null))
        {
            return null;
        }

        var coordinates = new List<Coordinate>();
        foreach (var route in sourceRoutes)
        {
            foreach (var coordinate in route.RouteGeometry!.Coordinates)
            {
                if (coordinates.Count == 0 || !coordinates[^1].Equals2D(coordinate))
                {
                    coordinates.Add(new Coordinate(coordinate.X, coordinate.Y));
                }
            }
        }

        return coordinates.Count < 2
            ? null
            : new LineString(coordinates.ToArray()) { SRID = 4326 };
    }

    private static decimal? SumIfAllPresent(IEnumerable<decimal?> values)
    {
        var list = values.ToList();
        return list.All(value => value.HasValue)
            ? list.Sum(value => value!.Value)
            : null;
    }

    private static decimal? SumComposedTravelMinutes(IReadOnlyList<RouteStopDraft> stops)
    {
        var travelMinutes = stops
            .Skip(1)
            .Select(stop => stop.StandardTravelMin)
            .ToList();

        return travelMinutes.Count > 0 && travelMinutes.All(value => value.HasValue)
            ? decimal.Round(travelMinutes.Sum(value => value!.Value), 2)
            : null;
    }

    private static IReadOnlyList<double[]>? ToGeometryDto(LineString? routeGeometry) =>
        routeGeometry is null
            ? null
            : routeGeometry.Coordinates
                .Select(coordinate => new[] { coordinate.X, coordinate.Y })
                .ToList();

    private sealed record RouteStopDraft(Station Station, decimal? StandardTravelMin, decimal? DistanceFromPreviousKm);
}
