using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Routes;

public sealed record AddRouteSegmentCommand(
    Guid RouteId,
    string FromStationCode,
    string ToStationCode,
    int SegmentOrder,
    decimal DistanceKm,
    int? EstimatedTravelMinutes,
    IReadOnlyList<RouteSegmentCoordinateDto>? Geometry = null) : IRequest<RouteSegmentDto>;

public sealed class AddRouteSegmentCommandValidator : AbstractValidator<AddRouteSegmentCommand>
{
    public AddRouteSegmentCommandValidator()
    {
        RuleFor(x => x.RouteId).NotEmpty();
        RuleFor(x => x.FromStationCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ToStationCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SegmentOrder).GreaterThan(0);
        RuleFor(x => x.DistanceKm).GreaterThan(0).PrecisionScale(8, 2, false);
        RuleFor(x => x.EstimatedTravelMinutes).GreaterThan(0).When(x => x.EstimatedTravelMinutes.HasValue);
        RuleFor(x => x.Geometry)
            .Must(x => x is null || x.Count == 0 || x.Count >= 2)
            .WithMessage("Geometry phải có ít nhất 2 điểm nếu được truyền.");
        RuleForEach(x => x.Geometry).ChildRules(point =>
        {
            point.RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
            point.RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        }).When(x => x.Geometry is not null);
    }
}

public sealed class AddRouteSegmentCommandHandler : IRequestHandler<AddRouteSegmentCommand, RouteSegmentDto>
{
    private readonly IApplicationDbContext _context;

    public AddRouteSegmentCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<RouteSegmentDto> Handle(AddRouteSegmentCommand request, CancellationToken cancellationToken)
    {
        var routeExists = await _context.Set<Route>()
            .AnyAsync(x => x.Id == request.RouteId, cancellationToken);
        if (!routeExists)
        {
            throw new NotFoundException("Route not found.");
        }

        var fromStation = await ResolveStationAsync(request.FromStationCode, cancellationToken);
        var toStation = await ResolveStationAsync(request.ToStationCode, cancellationToken);
        if (fromStation.Id == toStation.Id)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.ToStationCode), "Điểm đầu và điểm cuối của đoạn không được trùng nhau.")]);
        }

        await EnsureStationsBelongToRouteAsync(request.RouteId, fromStation, toStation, cancellationToken);
        await EnsureSegmentIsUniqueAsync(request, fromStation.Id, toStation.Id, cancellationToken);

        var segment = new RouteSegment
        {
            RouteId = request.RouteId,
            FromStationId = fromStation.Id,
            ToStationId = toStation.Id,
            SegmentOrder = request.SegmentOrder,
            DistanceKm = decimal.Round(request.DistanceKm, 2),
            EstimatedTravelMinutes = CustomBookingRequests.CustomBookingRouteEstimator.EstimateTravelMinutes(request.DistanceKm),
            Geometry = RouteSegmentSupport.BuildGeometry(request.Geometry)
        };

        _context.Set<RouteSegment>().Add(segment);
        await _context.SaveChangesAsync(cancellationToken);

        segment.FromStation = fromStation;
        segment.ToStation = toStation;
        return RouteSegmentSupport.ToDto(segment);
    }

    private async Task<Station> ResolveStationAsync(string stationCode, CancellationToken cancellationToken)
    {
        var code = stationCode.Trim().ToUpperInvariant();
        return await _context.Set<Station>()
            .SingleOrDefaultAsync(x => x.StationCode == code, cancellationToken)
            ?? throw new NotFoundException($"Station '{code}' not found.");
    }

    private async Task EnsureStationsBelongToRouteAsync(
        Guid routeId,
        Station fromStation,
        Station toStation,
        CancellationToken cancellationToken)
    {
        var stationIds = new[] { fromStation.Id, toStation.Id };
        var count = await _context.Set<RouteStop>()
            .CountAsync(x => x.RouteId == routeId && stationIds.Contains(x.StationId), cancellationToken);

        if (count != stationIds.Length)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(AddRouteSegmentCommand.FromStationCode),
                "Cả hai bến của đoạn phải tồn tại trong danh sách stop của route.")]);
        }
    }

    private async Task EnsureSegmentIsUniqueAsync(
        AddRouteSegmentCommand request,
        Guid fromStationId,
        Guid toStationId,
        CancellationToken cancellationToken)
    {
        var duplicatedOrder = await _context.Set<RouteSegment>()
            .AnyAsync(x => x.RouteId == request.RouteId && x.SegmentOrder == request.SegmentOrder, cancellationToken);
        if (duplicatedOrder)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.SegmentOrder), "Segment order đã tồn tại trong route.")]);
        }

        var duplicatedPair = await _context.Set<RouteSegment>()
            .AnyAsync(x =>
                x.RouteId == request.RouteId
                && x.FromStationId == fromStationId
                && x.ToStationId == toStationId,
                cancellationToken);
        if (duplicatedPair)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.FromStationCode), "Đoạn route giữa hai bến này đã tồn tại.")]);
        }
    }
}
