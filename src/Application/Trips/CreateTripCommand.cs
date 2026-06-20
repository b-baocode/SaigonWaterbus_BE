using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Trips;

public sealed record CreateTripCommand(
    string RouteCode,
    int Capacity,
    DateOnly OperatingDate,
    DateTimeOffset DepartureTime) : IRequest<TripDetailDto>;

public sealed class CreateTripCommandValidator : AbstractValidator<CreateTripCommand>
{
    public CreateTripCommandValidator()
    {
        RuleFor(x => x.RouteCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Capacity).GreaterThan(0);
        RuleFor(x => x.DepartureTime).GreaterThan(DateTimeOffset.UtcNow);
    }
}

public sealed class CreateTripCommandHandler : IRequestHandler<CreateTripCommand, TripDetailDto>
{
    private readonly IApplicationDbContext _context;

    public CreateTripCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<TripDetailDto> Handle(CreateTripCommand request, CancellationToken cancellationToken)
    {
        var routeCode = request.RouteCode.Trim().ToUpperInvariant();

        var route = await _context.Set<Route>()
            .Include(r => r.RouteStops.OrderBy(rs => rs.StopOrder))
                .ThenInclude(rs => rs.Station)
            .SingleOrDefaultAsync(r => r.RouteCode == routeCode && r.Status == "Active", cancellationToken)
            ?? throw new NotFoundException($"Route '{routeCode}' not found or inactive.");

        if (route.RouteStops.Count < 2)
            throw new ValidationException([new ValidationFailure(nameof(request.RouteCode), "Route must have at least 2 stops.")]);

        var tripCode = $"TR-{request.OperatingDate:yyyyMMdd}-{route.RouteCode}-{Random.Shared.Next(1000, 9999)}";

        var tripStops = new List<TripStop>();
        var currentTime = request.DepartureTime;

        foreach (var routeStop in route.RouteStops.OrderBy(rs => rs.StopOrder))
        {
            var scheduledArrival = routeStop.StopOrder == route.RouteStops.Min(rs => rs.StopOrder)
                ? request.DepartureTime
                : currentTime;

            var scheduledDeparture = scheduledArrival.AddMinutes(routeStop.StandardDwellMin ?? 2);

            tripStops.Add(new TripStop
            {
                RouteStopId = routeStop.Id,
                RouteStop = routeStop,
                StopOrder = routeStop.StopOrder,
                ScheduledArrival = scheduledArrival,
                ScheduledDeparture = scheduledDeparture,
                StopStatus = "Scheduled"
            });

            currentTime = scheduledDeparture.AddMinutes(routeStop.StandardTravelMin ?? 15);
        }

        var arrivalTime = tripStops.Max(ts => ts.ScheduledArrival ?? request.DepartureTime);

        await EnsureNoStationDepartureConflictAsync(tripStops, cancellationToken);

        var trip = new Trip
        {
            RouteId = route.Id,
            TripCode = tripCode,
            OperatingDate = request.OperatingDate,
            DepartureTime = request.DepartureTime,
            ArrivalTime = arrivalTime,
            CapacitySnapshot = request.Capacity,
            TripStatus = TripStatus.Scheduled
        };

        foreach (var ts in tripStops)
            ts.Trip = trip;

        trip.TripStops = tripStops;

        _context.Set<Trip>().Add(trip);
        await _context.SaveChangesAsync(cancellationToken);

        return new TripDetailDto(
            trip.Id, trip.TripCode,
            route.Id, route.RouteName,
            trip.DepartureTime, trip.ArrivalTime,
            trip.CapacitySnapshot, trip.TripStatus.ToString(), trip.StatusNote,
            tripStops.OrderBy(ts => ts.StopOrder).Select(ts =>
            {
                var rs = route.RouteStops.Single(r => r.Id == ts.RouteStopId);
                return new TripStopDto(ts.Id, rs.Station.Id, rs.Station.StationName,
                    rs.Station.StationCode, ts.StopOrder,
                    ts.ScheduledArrival, ts.ScheduledDeparture,
                    ts.ActualArrival, ts.ActualDeparture, ts.StopStatus);
            }).ToList());
    }

    private async Task EnsureNoStationDepartureConflictAsync(
        IReadOnlyCollection<TripStop> tripStops,
        CancellationToken cancellationToken)
    {
        var stationDepartures = tripStops
            .Where(x => x.ScheduledDeparture.HasValue)
            .Select(x => new
            {
                StationId = x.RouteStop.StationId,
                ScheduledDeparture = x.ScheduledDeparture!.Value
            })
            .ToArray();

        foreach (var stationDeparture in stationDepartures)
        {
            var hasConflict = await _context.Set<TripStop>()
                .AsNoTracking()
                .AnyAsync(x =>
                    x.ScheduledDeparture == stationDeparture.ScheduledDeparture
                    && x.Trip.TripStatus != TripStatus.Cancelled
                    && x.RouteStop.StationId == stationDeparture.StationId,
                    cancellationToken);

            if (hasConflict)
            {
                throw new ValidationException(
                [
                    new ValidationFailure(
                        nameof(CreateTripCommand.DepartureTime),
                        "Bến đã có chuyến tàu xuất phát trong cùng thời điểm.")
                ]);
            }
        }
    }
}
