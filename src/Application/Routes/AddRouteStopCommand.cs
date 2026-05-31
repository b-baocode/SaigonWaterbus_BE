using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Routes;

public sealed record AddRouteStopCommand(
    Guid RouteId,
    Guid StationId,
    int StopOrder,
    int? StandardTravelMin,
    int? StandardDwellMin,
    bool IsPickupAllowed,
    bool IsDropoffAllowed) : IRequest<RouteStopDto>;

public sealed class AddRouteStopCommandValidator : AbstractValidator<AddRouteStopCommand>
{
    public AddRouteStopCommandValidator()
    {
        RuleFor(x => x.RouteId).NotEmpty();
        RuleFor(x => x.StationId).NotEmpty();
        RuleFor(x => x.StopOrder).GreaterThan(0);
    }
}

public sealed class AddRouteStopCommandHandler : IRequestHandler<AddRouteStopCommand, RouteStopDto>
{
    private readonly IApplicationDbContext _context;

    public AddRouteStopCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<RouteStopDto> Handle(AddRouteStopCommand request, CancellationToken cancellationToken)
    {
        if (!await _context.Set<Route>().AnyAsync(r => r.Id == request.RouteId, cancellationToken))
            throw new NotFoundException("Route not found.");

        if (!await _context.Set<Station>().AnyAsync(s => s.Id == request.StationId, cancellationToken))
            throw new NotFoundException("Station not found.");

        var duplicate = await _context.Set<RouteStop>().AnyAsync(
            rs => rs.RouteId == request.RouteId && rs.StopOrder == request.StopOrder, cancellationToken);
        if (duplicate)
            throw new ValidationException([new ValidationFailure(nameof(request.StopOrder), "Stop order already exists on this route.")]);

        var stop = new RouteStop
        {
            RouteId = request.RouteId,
            StationId = request.StationId,
            StopOrder = request.StopOrder,
            StandardTravelMin = request.StandardTravelMin,
            StandardDwellMin = request.StandardDwellMin,
            IsPickupAllowed = request.IsPickupAllowed,
            IsDropoffAllowed = request.IsDropoffAllowed
        };

        _context.Set<RouteStop>().Add(stop);
        await _context.SaveChangesAsync(cancellationToken);

        var station = await _context.Set<Station>()
            .SingleAsync(s => s.Id == request.StationId, cancellationToken);

        return new RouteStopDto(stop.Id, station.Id, station.StationCode, station.StationName,
            stop.StopOrder, stop.StandardTravelMin, stop.StandardDwellMin,
            stop.IsPickupAllowed, stop.IsDropoffAllowed);
    }
}
