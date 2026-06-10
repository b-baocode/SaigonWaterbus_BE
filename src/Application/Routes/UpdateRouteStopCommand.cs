using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Routes;

public sealed record UpdateRouteStopCommand(
    Guid RouteId,
    Guid RouteStopId,
    int? StandardTravelMin,
    int? StandardDwellMin,
    bool IsPickupAllowed,
    bool IsDropoffAllowed) : IRequest<RouteStopDto>;

public sealed class UpdateRouteStopCommandValidator : AbstractValidator<UpdateRouteStopCommand>
{
    public UpdateRouteStopCommandValidator()
    {
        RuleFor(x => x.RouteId).NotEmpty();
        RuleFor(x => x.RouteStopId).NotEmpty();
    }
}

public sealed class UpdateRouteStopCommandHandler : IRequestHandler<UpdateRouteStopCommand, RouteStopDto>
{
    private readonly IApplicationDbContext _context;

    public UpdateRouteStopCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<RouteStopDto> Handle(UpdateRouteStopCommand request, CancellationToken cancellationToken)
    {
        var stop = await _context.Set<RouteStop>()
            .Include(rs => rs.Station)
            .SingleOrDefaultAsync(rs => rs.Id == request.RouteStopId && rs.RouteId == request.RouteId, cancellationToken)
            ?? throw new NotFoundException("Route stop not found.");

        stop.StandardTravelMin = request.StandardTravelMin;
        stop.StandardDwellMin = request.StandardDwellMin;
        stop.IsPickupAllowed = request.IsPickupAllowed;
        stop.IsDropoffAllowed = request.IsDropoffAllowed;

        await _context.SaveChangesAsync(cancellationToken);

        return new RouteStopDto(stop.Id, stop.Station.Id, stop.Station.StationCode, stop.Station.StationName,
            stop.StopOrder, stop.StandardTravelMin, stop.StandardDwellMin,
            stop.IsPickupAllowed, stop.IsDropoffAllowed);
    }
}
