using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Stations;

public sealed record UpdateStationCommand(
    Guid StationId,
    string StationName,
    string? Address,
    decimal? Latitude,
    decimal? Longitude,
    string Status) : IRequest<StationDto>;

public sealed class UpdateStationCommandValidator : AbstractValidator<UpdateStationCommand>
{
    public UpdateStationCommandValidator()
    {
        RuleFor(x => x.StationId).NotEmpty();
        RuleFor(x => x.StationName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Status).NotEmpty().MaximumLength(20);
    }
}

public sealed class UpdateStationCommandHandler : IRequestHandler<UpdateStationCommand, StationDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateStationCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<StationDto> Handle(UpdateStationCommand request, CancellationToken cancellationToken)
    {
        var station = await _context.Set<Station>()
            .SingleOrDefaultAsync(s => s.Id == request.StationId, cancellationToken)
            ?? throw new NotFoundException("Station not found.");

        station.StationName = request.StationName.Trim();
        station.Address = request.Address?.Trim();
        station.Latitude = request.Latitude;
        station.Longitude = request.Longitude;
        station.Status = request.Status;

        await _context.SaveChangesAsync(cancellationToken);
        return StationDto.From(station);
    }
}
