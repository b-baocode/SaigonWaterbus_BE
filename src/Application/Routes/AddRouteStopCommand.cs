using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Routes;

[Authorize(Roles = "Admin")]
public sealed record AddRouteStopCommand(
    Guid RouteId,
    string StationCode,
    int StopOrder,
    int? StandardTravelMin,
    bool IsPickupAllowed,
    bool IsDropoffAllowed) : IRequest<RouteStopDto>;

public sealed class AddRouteStopCommandValidator : AbstractValidator<AddRouteStopCommand>
{
    public AddRouteStopCommandValidator()
    {
        RuleFor(x => x.RouteId).NotEmpty();
        RuleFor(x => x.StationCode).NotEmpty().MaximumLength(50);
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

        var stationCode = request.StationCode.Trim().ToUpperInvariant();
        var station = await _context.Set<Station>()
            .SingleOrDefaultAsync(s => s.StationCode == stationCode, cancellationToken)
            ?? throw new NotFoundException($"Station '{stationCode}' not found.");

        var duplicate = await _context.Set<RouteStop>().AnyAsync(
            rs => rs.RouteId == request.RouteId && rs.StopOrder == request.StopOrder, cancellationToken);
        if (duplicate)
            throw new ValidationException([new ValidationFailure(nameof(request.StopOrder), "Stop order already exists on this route.")]);

        var stop = new RouteStop
        {
            RouteId = request.RouteId,
            StationId = station.Id,
            StopOrder = request.StopOrder,
            StandardTravelMin = request.StandardTravelMin,
            IsPickupAllowed = request.IsPickupAllowed,
            IsDropoffAllowed = request.IsDropoffAllowed
        };

        _context.Set<RouteStop>().Add(stop);
        await _context.SaveChangesAsync(cancellationToken);

        return new RouteStopDto(stop.Id, station.Id, station.StationCode, station.StationName,
            stop.StopOrder, stop.StandardTravelMin,
            stop.IsPickupAllowed, stop.IsDropoffAllowed);
    }
}
