using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Routes;

public sealed record RemoveRouteSegmentCommand(Guid RouteId, Guid RouteSegmentId) : IRequest;

public sealed class RemoveRouteSegmentCommandValidator : AbstractValidator<RemoveRouteSegmentCommand>
{
    public RemoveRouteSegmentCommandValidator()
    {
        RuleFor(x => x.RouteId).NotEmpty();
        RuleFor(x => x.RouteSegmentId).NotEmpty();
    }
}

public sealed class RemoveRouteSegmentCommandHandler : IRequestHandler<RemoveRouteSegmentCommand>
{
    private readonly IApplicationDbContext _context;

    public RemoveRouteSegmentCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task Handle(RemoveRouteSegmentCommand request, CancellationToken cancellationToken)
    {
        var segment = await _context.Set<RouteSegment>()
            .SingleOrDefaultAsync(x => x.Id == request.RouteSegmentId && x.RouteId == request.RouteId, cancellationToken)
            ?? throw new NotFoundException("Route segment not found.");

        _context.Set<RouteSegment>().Remove(segment);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
