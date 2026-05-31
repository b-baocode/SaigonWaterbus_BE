using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Fares;

public sealed record CreateFareCommand(
    Guid RouteId,
    Guid FromStationId,
    Guid ToStationId,
    decimal BasePrice) : IRequest<FareMatrixDto>;

public sealed class CreateFareCommandValidator : AbstractValidator<CreateFareCommand>
{
    public CreateFareCommandValidator()
    {
        RuleFor(x => x.RouteId).NotEmpty();
        RuleFor(x => x.FromStationId).NotEmpty();
        RuleFor(x => x.ToStationId).NotEmpty()
            .NotEqual(x => x.FromStationId).WithMessage("From and To stations must be different.");
        RuleFor(x => x.BasePrice).GreaterThan(0);
    }
}

public sealed class CreateFareCommandHandler : IRequestHandler<CreateFareCommand, FareMatrixDto>
{
    private readonly IApplicationDbContext _context;

    public CreateFareCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<FareMatrixDto> Handle(CreateFareCommand request, CancellationToken cancellationToken)
    {
        if (!await _context.Set<Route>().AnyAsync(r => r.Id == request.RouteId, cancellationToken))
            throw new NotFoundException("Route not found.");

        var fromStation = await _context.Set<Station>()
            .SingleOrDefaultAsync(s => s.Id == request.FromStationId, cancellationToken)
            ?? throw new NotFoundException("From station not found.");

        var toStation = await _context.Set<Station>()
            .SingleOrDefaultAsync(s => s.Id == request.ToStationId, cancellationToken)
            ?? throw new NotFoundException("To station not found.");

        var exists = await _context.Set<FareMatrix>().AnyAsync(f =>
            f.RouteId == request.RouteId &&
            f.FromStationId == request.FromStationId &&
            f.ToStationId == request.ToStationId &&
            f.IsActive, cancellationToken);

        if (exists)
            throw new ValidationException([new ValidationFailure(nameof(request.FromStationId), "An active fare already exists for this station pair on this route.")]);

        var fare = new FareMatrix
        {
            RouteId = request.RouteId,
            FromStationId = request.FromStationId,
            ToStationId = request.ToStationId,
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
