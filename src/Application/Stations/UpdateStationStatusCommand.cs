using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Stations;

[Authorize(Roles = "Admin")]
public sealed record UpdateStationStatusCommand(
    Guid StationId,
    StationStatus Status) : IRequest<StationDto>;

public sealed class UpdateStationStatusCommandValidator : AbstractValidator<UpdateStationStatusCommand>
{
    public UpdateStationStatusCommandValidator()
    {
        RuleFor(x => x.StationId).NotEmpty();
        RuleFor(x => x.Status).IsInEnum();
    }
}

public sealed class UpdateStationStatusCommandHandler : IRequestHandler<UpdateStationStatusCommand, StationDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateStationStatusCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<StationDto> Handle(UpdateStationStatusCommand request, CancellationToken cancellationToken)
    {
        var station = await _context.Set<Station>()
            .Include(s => s.UserAssignments)
                .ThenInclude(a => a.User)
                    .ThenInclude(u => u.Role)
            .SingleOrDefaultAsync(s => s.Id == request.StationId, cancellationToken)
            ?? throw new NotFoundException("Station not found.");

        station.Status = request.Status;

        await _context.SaveChangesAsync(cancellationToken);
        return StationDto.From(station);
    }
}
