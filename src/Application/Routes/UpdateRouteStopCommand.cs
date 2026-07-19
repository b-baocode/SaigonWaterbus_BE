using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Routes;

[Authorize(Roles = "Admin")]
public sealed record UpdateRouteStopCommand(
    Guid RouteId,
    Guid RouteStopId,
    decimal? StandardTravelMin,
    decimal? DistanceFromPreviousKm,
    bool IsPickupAllowed,
    bool IsDropoffAllowed) : IRequest<RouteStopDto>;

public sealed class UpdateRouteStopCommandValidator : AbstractValidator<UpdateRouteStopCommand>
{
    public UpdateRouteStopCommandValidator()
    {
        RuleFor(x => x.RouteId).NotEmpty();
        RuleFor(x => x.RouteStopId).NotEmpty();
        RuleFor(x => x.StandardTravelMin)
            .Must(x => x is null or > 0)
            .WithMessage("standardTravelMin phai lon hon 0 neu duoc gui.");
        RuleFor(x => x.DistanceFromPreviousKm)
            .Must(x => x is null or (> 0 and <= 999))
            .WithMessage("distanceFromPreviousKm phai lon hon 0 va toi da 999 km.");
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
        stop.DistanceFromPreviousKm = request.DistanceFromPreviousKm;
        stop.IsPickupAllowed = request.IsPickupAllowed;
        stop.IsDropoffAllowed = request.IsDropoffAllowed;

        await _context.SaveChangesAsync(cancellationToken);

        return new RouteStopDto(stop.Id, stop.Station.Id, stop.Station.StationCode, stop.Station.StationName,
            stop.StopOrder, stop.StandardTravelMin, stop.DistanceFromPreviousKm,
            stop.IsPickupAllowed, stop.IsDropoffAllowed);
    }
}
