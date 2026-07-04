using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Routes;

[Authorize(Roles = "Admin")]
public sealed record RemoveRouteStopCommand(Guid RouteId, Guid RouteStopId) : IRequest;

public sealed class RemoveRouteStopCommandValidator : AbstractValidator<RemoveRouteStopCommand>
{
    public RemoveRouteStopCommandValidator()
    {
        RuleFor(x => x.RouteId).NotEmpty();
        RuleFor(x => x.RouteStopId).NotEmpty();
    }
}

public sealed class RemoveRouteStopCommandHandler : IRequestHandler<RemoveRouteStopCommand>
{
    private readonly IApplicationDbContext _context;

    public RemoveRouteStopCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task Handle(RemoveRouteStopCommand request, CancellationToken cancellationToken)
    {
        var stop = await _context.Set<RouteStop>()
            .SingleOrDefaultAsync(rs => rs.Id == request.RouteStopId && rs.RouteId == request.RouteId, cancellationToken)
            ?? throw new NotFoundException("Route stop not found.");

        _context.Set<RouteStop>().Remove(stop);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
