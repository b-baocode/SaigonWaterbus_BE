using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Fares;

public sealed record CreateFareCommand(
    string RouteCode,
    string FromStationCode,
    string ToStationCode,
    decimal BasePrice) : IRequest<FareMatrixDto>;

public sealed class CreateFareCommandValidator : AbstractValidator<CreateFareCommand>
{
    public CreateFareCommandValidator()
    {
        RuleFor(x => x.RouteCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.FromStationCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ToStationCode).NotEmpty().MaximumLength(50)
            .NotEqual(x => x.FromStationCode).WithMessage("From and To stations must be different.");
        RuleFor(x => x.BasePrice).GreaterThan(0);
    }
}

public sealed class CreateFareCommandHandler : IRequestHandler<CreateFareCommand, FareMatrixDto>
{
    private readonly IApplicationDbContext _context;

    public CreateFareCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<FareMatrixDto> Handle(CreateFareCommand request, CancellationToken cancellationToken)
    {
        var routeCode = request.RouteCode.Trim().ToUpperInvariant();
        var route = await _context.Set<Route>()
            .SingleOrDefaultAsync(r => r.RouteCode == routeCode, cancellationToken)
            ?? throw new NotFoundException($"Route '{routeCode}' not found.");

        var fromCode = request.FromStationCode.Trim().ToUpperInvariant();
        var fromStation = await _context.Set<Station>()
            .SingleOrDefaultAsync(s => s.StationCode == fromCode, cancellationToken)
            ?? throw new NotFoundException($"Station '{fromCode}' not found.");

        var toCode = request.ToStationCode.Trim().ToUpperInvariant();
        var toStation = await _context.Set<Station>()
            .SingleOrDefaultAsync(s => s.StationCode == toCode, cancellationToken)
            ?? throw new NotFoundException($"Station '{toCode}' not found.");

        var exists = await _context.Set<FareMatrix>().AnyAsync(f =>
            f.RouteId == route.Id &&
            f.FromStationId == fromStation.Id &&
            f.ToStationId == toStation.Id &&
            f.IsActive, cancellationToken);

        if (exists)
            throw new ValidationException([new ValidationFailure(nameof(request.FromStationCode), "An active fare already exists for this station pair on this route.")]);

        var fare = new FareMatrix
        {
            RouteId = route.Id,
            FromStationId = fromStation.Id,
            ToStationId = toStation.Id,
            BasePrice = request.BasePrice,
            IsActive = true
        };

        _context.Set<FareMatrix>().Add(fare);
        await _context.SaveChangesAsync(cancellationToken);

        return new FareMatrixDto(fare.Id, fare.RouteId,
            fromStation.Id, fromStation.StationName,
            toStation.Id, toStation.StationName,
            fare.BasePrice, fare.IsActive);
    }
}
