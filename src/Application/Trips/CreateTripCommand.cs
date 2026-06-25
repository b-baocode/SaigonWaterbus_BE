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

        var service = await _context.Set<WaterbusService>()
            .Where(x => x.IsActive
                && (x.BookingMode == BookingMode.SeatBased || x.BookingMode == BookingMode.SeatTypeBased))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException(
            [
                new ValidationFailure(
                    nameof(request.RouteCode),
                    "Chưa có waterbus service đang active để tạo chuyến.")
            ]);

        var tripCode = $"TR-{request.OperatingDate:yyyyMMdd}-{route.RouteCode}-{Random.Shared.Next(1000, 9999)}";

        var currentTime = request.DepartureTime;
        var stopDtos = new List<TripStopDto>();

        foreach (var routeStop in route.RouteStops.OrderBy(rs => rs.StopOrder))
        {
            var scheduledArrival = routeStop.StopOrder == route.RouteStops.Min(rs => rs.StopOrder)
                ? request.DepartureTime
                : currentTime;

            var scheduledDeparture = scheduledArrival.AddMinutes(routeStop.StandardDwellMin ?? 2);

            stopDtos.Add(new TripStopDto(
                routeStop.Id,
                routeStop.Station.Id,
                routeStop.Station.StationName,
                routeStop.Station.StationCode,
                routeStop.StopOrder,
                scheduledArrival,
                scheduledDeparture,
                null,
                null,
                "Scheduled"));

            currentTime = scheduledDeparture.AddMinutes(routeStop.StandardTravelMin ?? 15);
        }

        var arrivalTime = stopDtos.Max(ts => ts.ScheduledArrival ?? request.DepartureTime);

        await EnsureNoRouteDepartureConflictAsync(route.Id, request.DepartureTime, cancellationToken);

        var trip = new Trip
        {
            WaterbusServiceId = service.Id,
            RouteId = route.Id,
            TripCode = tripCode,
            OperatingDate = request.OperatingDate,
            DepartureTime = request.DepartureTime,
            ArrivalTime = arrivalTime,
            CapacitySnapshot = request.Capacity,
            TripStatus = TripStatus.Scheduled
        };

        _context.Set<Trip>().Add(trip);
        await _context.SaveChangesAsync(cancellationToken);

        return new TripDetailDto(
            trip.Id, trip.TripCode,
            route.Id, route.RouteName,
            trip.DepartureTime, trip.ArrivalTime,
            trip.CapacitySnapshot, trip.TripStatus.ToString(), trip.StatusNote,
            stopDtos.OrderBy(ts => ts.StopOrder).ToList());
    }

    private async Task EnsureNoRouteDepartureConflictAsync(
        Guid routeId,
        DateTimeOffset departureTime,
        CancellationToken cancellationToken)
    {
        var hasConflict = await _context.Set<Trip>()
            .AsNoTracking()
            .AnyAsync(x =>
                x.RouteId == routeId
                && x.DepartureTime == departureTime
                && x.TripStatus != TripStatus.Cancelled,
                cancellationToken);

        if (hasConflict)
        {
            throw new ValidationException(
            [
                new ValidationFailure(
                    nameof(CreateTripCommand.DepartureTime),
                    "Tuyến đã có chuyến tàu xuất phát trong cùng thời điểm.")
            ]);
        }
    }
}
