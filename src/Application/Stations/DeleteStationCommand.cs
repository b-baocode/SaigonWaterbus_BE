using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

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

        _context.Set<Station>().Remove(station);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
