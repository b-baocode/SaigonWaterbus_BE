using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Routes;

[Authorize(Roles = "Admin")]
public sealed record AddRouteStopCommand(
    Guid RouteId,
    Guid? StationId,
    string? StationCode,
    int StopOrder,
    int? StandardTravelMin,
    bool IsPickupAllowed,
    bool IsDropoffAllowed) : IRequest<RouteStopDto>;

public sealed class AddRouteStopCommandValidator : AbstractValidator<AddRouteStopCommand>
{
    public AddRouteStopCommandValidator()
    {
        RuleFor(x => x.RouteId).NotEmpty();
        RuleFor(x => x.StationId)
            .NotEmpty()
            .When(x => x.StationId.HasValue);
        RuleFor(x => x.StationCode)
            .MaximumLength(50)
            .When(x => !string.IsNullOrWhiteSpace(x.StationCode));
        RuleFor(x => x)
            .Must(x => x.StationId.HasValue || !string.IsNullOrWhiteSpace(x.StationCode))
            .WithMessage("Can gui stationId hoac stationCode.");
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

        var station = request.StationId.HasValue
            ? await _context.Set<Station>()
                .SingleOrDefaultAsync(s => s.Id == request.StationId.Value, cancellationToken)
            : await _context.Set<Station>()
                .SingleOrDefaultAsync(
                    s => s.StationCode == request.StationCode!.Trim().ToUpperInvariant(),
                    cancellationToken);

        if (station is null)
        {
            throw new NotFoundException(request.StationId.HasValue
                ? "Station not found."
                : $"Station '{request.StationCode!.Trim().ToUpperInvariant()}' not found.");
        }

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
