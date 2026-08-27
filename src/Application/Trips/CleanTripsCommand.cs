using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Trips;

[Authorize(Roles = "Admin")]
public sealed record CleanTripsCommand(DateOnly OperatingDate) : IRequest<CleanTripsResult>;

public sealed record CleanTripsResult(int Deleted);

public sealed class CleanTripsCommandValidator : AbstractValidator<CleanTripsCommand>
{
    public CleanTripsCommandValidator()
    {
        RuleFor(x => x.OperatingDate).NotEmpty();
    }
}

public sealed class CleanTripsCommandHandler : IRequestHandler<CleanTripsCommand, CleanTripsResult>
{
    private readonly IApplicationDbContext _context;

    public CleanTripsCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CleanTripsResult> Handle(CleanTripsCommand request, CancellationToken cancellationToken)
    {
        var trips = await _context.Set<Trip>()
            .Where(t => t.OperatingDate == request.OperatingDate)
            .ToListAsync(cancellationToken);

        if (trips.Count == 0)
            return new CleanTripsResult(0);

        var tripIds = trips.Select(t => t.Id).ToList();

        var seats = await _context.Set<TripSeat>()
            .Where(ts => tripIds.Contains(ts.TripId))
            .ToListAsync(cancellationToken);
        if (seats.Count > 0)
            _context.Set<TripSeat>().RemoveRange(seats);

        var tripStops = await _context.Set<TripStop>()
            .Where(ts => tripIds.Contains(ts.TripId))
            .ToListAsync(cancellationToken);
        if (tripStops.Count > 0)
            _context.Set<TripStop>().RemoveRange(tripStops);

        _context.Set<Trip>().RemoveRange(trips);
        await _context.SaveChangesAsync(cancellationToken);

        return new CleanTripsResult(trips.Count);
    }
}
