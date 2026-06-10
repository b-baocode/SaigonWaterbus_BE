using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Stations;

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

        var belongsToRoute = await _context.Set<RouteStop>()
            .AnyAsync(rs => rs.StationId == station.Id, cancellationToken);

        if (belongsToRoute)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.StationId),
                "Station cannot be deleted because it is assigned to one or more routes.")]);
        }

        _context.Set<Station>().Remove(station);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
