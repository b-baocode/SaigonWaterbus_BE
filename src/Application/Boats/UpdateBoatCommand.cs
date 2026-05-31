using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Boats;

public sealed record UpdateBoatCommand(
    Guid BoatId,
    string BoatName,
    int Capacity,
    string BoatStatus,
    string? Description) : IRequest<BoatDto>;

public sealed class UpdateBoatCommandValidator : AbstractValidator<UpdateBoatCommand>
{
    public UpdateBoatCommandValidator()
    {
        RuleFor(x => x.BoatId).NotEmpty();
        RuleFor(x => x.BoatName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Capacity).GreaterThan(0);
        RuleFor(x => x.BoatStatus).NotEmpty();
    }
}

public sealed class UpdateBoatCommandHandler : IRequestHandler<UpdateBoatCommand, BoatDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateBoatCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<BoatDto> Handle(UpdateBoatCommand request, CancellationToken cancellationToken)
    {
        var boat = await _context.Set<Boat>()
            .SingleOrDefaultAsync(b => b.Id == request.BoatId, cancellationToken)
            ?? throw new NotFoundException("Boat not found.");

        boat.BoatName = request.BoatName.Trim();
        boat.Capacity = request.Capacity;
        boat.BoatStatus = request.BoatStatus;
        boat.Description = request.Description?.Trim();

        await _context.SaveChangesAsync(cancellationToken);

        return new BoatDto(boat.Id, boat.BoatCode, boat.BoatName, boat.Capacity, boat.BoatStatus, boat.Description);
    }
}
