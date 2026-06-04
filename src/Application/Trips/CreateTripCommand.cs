using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Trips;

public sealed record CreateTripCommand(
    string RouteCode,
    string BoatCode,
    DateOnly OperatingDate,
    DateTimeOffset DepartureTime) : IRequest<TripDetailDto>;

public sealed class CreateTripCommandValidator : AbstractValidator<CreateTripCommand>
{
    public CreateTripCommandValidator()
    {
        RuleFor(x => x.RouteCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.BoatCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DepartureTime).GreaterThan(DateTimeOffset.UtcNow);
    }
}

public sealed class CreateTripCommandHandler : IRequestHandler<CreateTripCommand, TripDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public CreateTripCommandHandler(IApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<TripDetailDto> Handle(CreateTripCommand request, CancellationToken cancellationToken)
    {
        var routeCode = request.RouteCode.Trim().ToUpperInvariant();
        var boatCode = request.BoatCode.Trim().ToUpperInvariant();

        var route = await _context.Set<Route>()
            .Include(r => r.RouteStops.OrderBy(rs => rs.StopOrder))
                .ThenInclude(rs => rs.Station)
            .SingleOrDefaultAsync(r => r.RouteCode == routeCode && r.Status == "Active", cancellationToken)
            ?? throw new NotFoundException($"Route '{routeCode}' not found or inactive.");

        if (route.RouteStops.Count < 2)
            throw new ValidationException([new ValidationFailure(nameof(request.RouteCode), "Route must have at least 2 stops.")]);

        var boat = await _context.Set<Boat>()
            .SingleOrDefaultAsync(b => b.BoatCode == boatCode && b.BoatStatus == "Active", cancellationToken)
            ?? throw new NotFoundException($"Boat '{boatCode}' not found or inactive.");

        // Tạo trip_code: TR-{yyyyMMdd}-{route_code}-{random}
        var tripCode = $"TR-{request.OperatingDate:yyyyMMdd}-{route.RouteCode}-{Random.Shared.Next(1000, 9999)}";

        // Tính thời gian các stop từ departure_time
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
                StopOrder = routeStop.StopOrder,
                ScheduledArrival = scheduledArrival,
                ScheduledDeparture = scheduledDeparture,
                StopStatus = "Scheduled"
            });

            currentTime = scheduledDeparture.AddMinutes(routeStop.StandardTravelMin ?? 15);
        }

        var arrivalTime = tripStops.Max(ts => ts.ScheduledArrival ?? request.DepartureTime);

        var trip = new Trip
        {
            RouteId = route.Id,
            BoatId = boat.Id,
            TripCode = tripCode,
            OperatingDate = request.OperatingDate,
            DepartureTime = request.DepartureTime,
            ArrivalTime = arrivalTime,
            CapacitySnapshot = boat.Capacity,
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
            boat.Id, boat.BoatName,
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
}
