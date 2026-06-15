using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Fares;

public sealed record GetFareByServiceSeatTypeQuery(
    string RouteCode,
    string FromStationCode,
    string ToStationCode,
    Guid ServiceId,
    string SeatTypeCode) : IRequest<IReadOnlyList<FareByServiceSeatTypeDto>>;

public sealed class GetFareByServiceSeatTypeQueryValidator
    : AbstractValidator<GetFareByServiceSeatTypeQuery>
{
    public GetFareByServiceSeatTypeQueryValidator()
    {
        RuleFor(x => x.RouteCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.FromStationCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ToStationCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ServiceId).NotEmpty();
        RuleFor(x => x.SeatTypeCode).NotEmpty().MaximumLength(30);
    }
}

public sealed class GetFareByServiceSeatTypeQueryHandler
    : IRequestHandler<GetFareByServiceSeatTypeQuery, IReadOnlyList<FareByServiceSeatTypeDto>>
{
    private readonly IApplicationDbContext _context;

    public GetFareByServiceSeatTypeQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<FareByServiceSeatTypeDto>> Handle(
        GetFareByServiceSeatTypeQuery request,
        CancellationToken cancellationToken)
    {
        var routeCode = request.RouteCode.Trim().ToUpperInvariant();
        var route = await _context.Set<Route>()
            .SingleOrDefaultAsync(x => x.RouteCode == routeCode, cancellationToken)
            ?? throw new NotFoundException($"Route '{routeCode}' not found.");

        var fromCode = request.FromStationCode.Trim().ToUpperInvariant();
        var fromStation = await _context.Set<Station>()
            .SingleOrDefaultAsync(x => x.StationCode == fromCode, cancellationToken)
            ?? throw new NotFoundException($"Station '{fromCode}' not found.");

        var toCode = request.ToStationCode.Trim().ToUpperInvariant();
        var toStation = await _context.Set<Station>()
            .SingleOrDefaultAsync(x => x.StationCode == toCode, cancellationToken)
            ?? throw new NotFoundException($"Station '{toCode}' not found.");

        var basePrice = await _context.Set<FareMatrix>()
            .Where(x => x.RouteId == route.Id
                     && x.FromStationId == fromStation.Id
                     && x.ToStationId == toStation.Id
                     && x.IsActive)
            .Select(x => (decimal?)x.BasePrice)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("No active fare defined for this station pair.");

        var normalizedSeatTypeCode = request.SeatTypeCode.Trim().ToUpperInvariant();
        var serviceSeatPrice = await _context.ServiceSeatTypePrices
            .AsNoTracking()
            .Include(x => x.WaterbusService)
            .Include(x => x.SeatType)
            .SingleOrDefaultAsync(
                x => x.WaterbusServiceId == request.ServiceId
                  && x.SeatType.Code == normalizedSeatTypeCode
                  && x.IsActive
                  && x.SeatType.IsActive
                  && x.WaterbusService.IsActive,
                cancellationToken)
            ?? throw new NotFoundException(
                $"Service does not support active seat type '{normalizedSeatTypeCode}'.");

        var ticketTypes = await _context.Set<TicketType>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.TicketTypeCode)
            .ToArrayAsync(cancellationToken);

        return ticketTypes
            .Select(ticketType => new FareByServiceSeatTypeDto(
                ticketType.Id,
                ticketType.TicketTypeName,
                serviceSeatPrice.WaterbusServiceId,
                serviceSeatPrice.WaterbusService.Code,
                serviceSeatPrice.SeatTypeId,
                serviceSeatPrice.SeatType.Code,
                basePrice,
                ticketType.PriceModifier,
                serviceSeatPrice.PriceModifier,
                basePrice * ticketType.PriceModifier * serviceSeatPrice.PriceModifier))
            .ToArray();
    }
}
