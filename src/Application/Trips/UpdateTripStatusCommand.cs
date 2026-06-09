using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Trips;

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
            .Include(t => t.TripStops)
                .ThenInclude(ts => ts.RouteStop)
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
        trip.TripStops.OrderBy(ts => ts.StopOrder).Select(ts => new TripStopDto(
            ts.Id, ts.RouteStop.Station.Id, ts.RouteStop.Station.StationName,
            ts.RouteStop.Station.StationCode, ts.StopOrder,
            ts.ScheduledArrival, ts.ScheduledDeparture,
            ts.ActualArrival, ts.ActualDeparture, ts.StopStatus)).ToList());
}
