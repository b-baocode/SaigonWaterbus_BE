using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Trips;

public sealed record GetTripDetailQuery(Guid TripId) : IRequest<TripDetailDto>;

public sealed class GetTripDetailQueryHandler : IRequestHandler<GetTripDetailQuery, TripDetailDto>
{
    private readonly IApplicationDbContext _context;

    public GetTripDetailQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<TripDetailDto> Handle(GetTripDetailQuery request, CancellationToken cancellationToken)
    {
        var trip = await _context.Set<Trip>()
            .Include(t => t.Route)
                .ThenInclude(r => r.RouteStops)
                    .ThenInclude(rs => rs.Station)
            .Include(t => t.TripStops)
                .ThenInclude(ts => ts.Station)
            .SingleOrDefaultAsync(t => t.Id == request.TripId, cancellationToken)
            ?? throw new NotFoundException("Trip not found.");

        var sourceBooking = trip.SourceBookingId.HasValue
            ? await _context.Set<Booking>()
                .Include(x => x.ItineraryStops)
                .SingleOrDefaultAsync(x => x.Id == trip.SourceBookingId.Value, cancellationToken)
            : null;

        return UpdateTripStatusCommandHandler.ToDetailDto(trip, sourceBooking);
    }
}
