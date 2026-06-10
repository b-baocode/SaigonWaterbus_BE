using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Fares;

public sealed record DeleteFareCommand(Guid FareId) : IRequest;

public sealed class DeleteFareCommandValidator : AbstractValidator<DeleteFareCommand>
{
    public DeleteFareCommandValidator()
    {
        RuleFor(x => x.FareId).NotEmpty();
    }
}

public sealed class DeleteFareCommandHandler : IRequestHandler<DeleteFareCommand>
{
    private readonly IApplicationDbContext _context;

    public DeleteFareCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task Handle(DeleteFareCommand request, CancellationToken cancellationToken)
    {
        var fare = await _context.Set<FareMatrix>()
            .SingleOrDefaultAsync(f => f.Id == request.FareId, cancellationToken)
            ?? throw new NotFoundException("Fare not found.");

        fare.IsActive = false;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
