using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Stations;

public sealed record UpdateStationCommand(
    Guid StationId,
    string StationName,
    string? Address,
    string? Description,
    decimal? Latitude,
    decimal? Longitude,
    StationStatus Status,
    string? PhoneNumber,
    bool? HasWaitingArea,
    bool? HasParking,
    bool? HasTicketCounter) : IRequest<StationDto>;

public sealed class UpdateStationCommandValidator : AbstractValidator<UpdateStationCommand>
{
    public UpdateStationCommandValidator()
    {
        RuleFor(x => x.StationId).NotEmpty();
        RuleFor(x => x.StationName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Address).MaximumLength(300).When(x => x.Address is not null);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
        RuleFor(x => x.PhoneNumber).MaximumLength(20).When(x => x.PhoneNumber is not null);
        RuleFor(x => x.Status).IsInEnum();
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
        station.Address = request.Address?.Trim() ?? station.Address;
        station.Description = request.Description?.Trim() ?? station.Description;
        station.Latitude = request.Latitude ?? station.Latitude;
        station.Longitude = request.Longitude ?? station.Longitude;
        station.Status = request.Status;
        station.PhoneNumber = request.PhoneNumber?.Trim() ?? station.PhoneNumber;
        station.HasWaitingArea = request.HasWaitingArea ?? station.HasWaitingArea;
        station.HasParking = request.HasParking ?? station.HasParking;
        station.HasTicketCounter = request.HasTicketCounter ?? station.HasTicketCounter;

        await _context.SaveChangesAsync(cancellationToken);
        return StationDto.From(station);
    }
}
