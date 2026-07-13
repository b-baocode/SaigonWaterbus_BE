using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Trips;

[Authorize(Roles = "Admin,Manager,Staff")]
public sealed record UpdateTripStatusCommand(
    Guid TripId,
    TripStatus TripStatus,
    string? StatusNote) : IRequest<TripDetailDto>;

public sealed class UpdateTripStatusCommandValidator : AbstractValidator<UpdateTripStatusCommand>
{
    public UpdateTripStatusCommandValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
    }
}

public sealed class UpdateTripStatusCommandHandler : IRequestHandler<UpdateTripStatusCommand, TripDetailDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateTripStatusCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<TripDetailDto> Handle(UpdateTripStatusCommand request, CancellationToken cancellationToken)
    {
        var trip = await _context.Set<Trip>()
            .Include(t => t.Route)
                .ThenInclude(r => r.RouteStops)
                    .ThenInclude(rs => rs.Station)
            .SingleOrDefaultAsync(t => t.Id == request.TripId, cancellationToken)
            ?? throw new NotFoundException("Trip not found.");

        trip.TripStatus = request.TripStatus;
        trip.StatusNote = request.StatusNote;

        await _context.SaveChangesAsync(cancellationToken);

        return ToDetailDto(trip);
    }

    internal static TripDetailDto ToDetailDto(Trip trip) => new(
        trip.Id, trip.TripCode,
        trip.Route.Id, trip.Route.RouteName,
        trip.DepartureTime, trip.ArrivalTime,
        trip.CapacitySnapshot, trip.TripStatus.ToString(), trip.StatusNote,
        BuildStopDtos(trip).ToList());

    private static IEnumerable<TripStopDto> BuildStopDtos(Trip trip)
    {
        var current = trip.DepartureTime;
        foreach (var routeStop in trip.Route.RouteStops.OrderBy(x => x.StopOrder))
        {
            var arrival = routeStop.StopOrder == trip.Route.RouteStops.Min(x => x.StopOrder)
                ? trip.DepartureTime
                : current;
            var departure = arrival;
            current = departure.AddMinutes(routeStop.StandardTravelMin ?? 15);

            yield return new TripStopDto(
                routeStop.Id,
                routeStop.Station.Id,
                routeStop.Station.StationName,
                routeStop.Station.StationCode,
                routeStop.StopOrder,
                arrival,
                departure,
                null,
                null,
                trip.TripStatus.ToString());
        }
    }
}
