using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Stations;

[Authorize(Roles = "Admin")]
public sealed record DeleteStationCommand(Guid StationId) : IRequest;

public sealed class DeleteStationCommandValidator : AbstractValidator<DeleteStationCommand>
{
    public DeleteStationCommandValidator()
    {
        RuleFor(x => x.StationId).NotEmpty();
    }
}

public sealed class DeleteStationCommandHandler : IRequestHandler<DeleteStationCommand>
{
    private readonly IApplicationDbContext _context;

    public DeleteStationCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task Handle(DeleteStationCommand request, CancellationToken cancellationToken)
    {
        var station = await _context.Set<Station>()
            .SingleOrDefaultAsync(s => s.Id == request.StationId, cancellationToken)
            ?? throw new NotFoundException("Station not found.");

        var dependencies = new List<string>();

        if (await _context.Set<RouteStop>().AnyAsync(rs => rs.StationId == station.Id, cancellationToken))
        {
            dependencies.Add("route");
        }

        if (await _context.Set<TripStop>().AnyAsync(ts => ts.StationId == station.Id, cancellationToken))
        {
            dependencies.Add("trip stop");
        }

        if (await _context.Set<Booking>().AnyAsync(
                b => b.FromStationId == station.Id || b.ToStationId == station.Id,
                cancellationToken))
        {
            dependencies.Add("booking");
        }

        if (await _context.Set<BookingPassenger>().AnyAsync(
                p => p.FromStationId == station.Id || p.ToStationId == station.Id,
                cancellationToken))
        {
            dependencies.Add("booking passenger");
        }

        if (await _context.Set<BookingItineraryStop>().AnyAsync(s => s.StationId == station.Id, cancellationToken))
        {
            dependencies.Add("booking itinerary");
        }

        if (await _context.Set<CharterRouteDrawRequestStop>().AnyAsync(s => s.StationId == station.Id, cancellationToken))
        {
            dependencies.Add("charter route draw request");
        }

        if (await _context.Set<UserStationAssignment>().AnyAsync(a => a.StationId == station.Id, cancellationToken))
        {
            dependencies.Add("user station assignment");
        }

        if (await _context.Set<StaffWorkAssignment>().AnyAsync(a => a.StationId == station.Id, cancellationToken))
        {
            dependencies.Add("staff work assignment");
        }

        if (await _context.Set<Landmark>().AnyAsync(l => l.StationId == station.Id, cancellationToken))
        {
            dependencies.Add("landmark");
        }

        if (await _context.Set<GpsTrackingSession>().AnyAsync(
                s => s.StartStationId == station.Id || s.EndStationId == station.Id,
                cancellationToken))
        {
            dependencies.Add("GPS tracking session");
        }

        if (await _context.Set<BoatLatestLocation>().AnyAsync(l => l.NextStationId == station.Id, cancellationToken))
        {
            dependencies.Add("boat latest location");
        }

        if (await _context.Set<TicketScanEvent>().AnyAsync(e => e.StationId == station.Id, cancellationToken))
        {
            dependencies.Add("ticket scan event");
        }

        if (await _context.Set<Incident>().AnyAsync(i => i.ReplacementTargetStationId == station.Id, cancellationToken))
        {
            dependencies.Add("incident");
        }

        if (dependencies.Count > 0)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.StationId),
                $"Station cannot be deleted because it is being used by: {string.Join(", ", dependencies)}. Set it to Inactive instead if you want to hide it from operations.")]);
        }

        _context.Set<Station>().Remove(station);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
