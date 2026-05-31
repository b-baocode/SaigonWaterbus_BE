using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Stations;

public sealed record CreateStationCommand(
    string StationCode,
    string StationName,
    string? Address,
    decimal? Latitude,
    decimal? Longitude) : IRequest<StationDto>;

public sealed class CreateStationCommandValidator : AbstractValidator<CreateStationCommand>
{
    public CreateStationCommandValidator()
    {
        RuleFor(x => x.StationCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.StationName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Address).MaximumLength(500).When(x => x.Address is not null);
    }
}

public sealed class CreateStationCommandHandler : IRequestHandler<CreateStationCommand, StationDto>
{
    private readonly IApplicationDbContext _context;

    public CreateStationCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<StationDto> Handle(CreateStationCommand request, CancellationToken cancellationToken)
    {
        var code = request.StationCode.Trim().ToUpperInvariant();

        if (await _context.Set<Station>().AnyAsync(s => s.StationCode == code, cancellationToken))
            throw new ValidationException([new ValidationFailure(nameof(request.StationCode), "Station code already exists.")]);


        var station = new Station
        {
            StationCode = code,
            StationName = request.StationName.Trim(),
            Address = request.Address?.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Status = "Active"
        };

        _context.Set<Station>().Add(station);
        await _context.SaveChangesAsync(cancellationToken);

        return StationDto.From(station);
    }
}
