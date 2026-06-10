using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Fares;

public sealed record UpdateFareCommand(Guid FareId, decimal BasePrice, bool IsActive) : IRequest<FareMatrixDto>;

public sealed class UpdateFareCommandValidator : AbstractValidator<UpdateFareCommand>
{
    public UpdateFareCommandValidator()
    {
        RuleFor(x => x.FareId).NotEmpty();
        RuleFor(x => x.BasePrice).GreaterThan(0);
    }
}

public sealed class UpdateFareCommandHandler : IRequestHandler<UpdateFareCommand, FareMatrixDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateFareCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<FareMatrixDto> Handle(UpdateFareCommand request, CancellationToken cancellationToken)
    {
        var fare = await _context.Set<FareMatrix>()
            .Include(f => f.FromStation)
            .Include(f => f.ToStation)
            .SingleOrDefaultAsync(f => f.Id == request.FareId, cancellationToken)
            ?? throw new NotFoundException("Fare not found.");

        fare.BasePrice = request.BasePrice;
        fare.IsActive = request.IsActive;

        await _context.SaveChangesAsync(cancellationToken);

        return new FareMatrixDto(fare.Id, fare.RouteId,
            fare.FromStation.Id, fare.FromStation.StationName,
            fare.ToStation.Id, fare.ToStation.StationName,
            fare.BasePrice, fare.IsActive);
    }
}
