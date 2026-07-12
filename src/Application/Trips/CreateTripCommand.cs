using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Trips;

/// <summary>Giá vé theo loại ghế cho riêng chuyến này (ghi vào trip_seats.price).</summary>
public sealed record TripSeatTypePriceInput(string SeatTypeCode, decimal Price);

/// <summary>
/// Tao chuyen tau. BoatCode BAT BUOC: trip luon gan tau de sinh trip_seats (khong co ghe thi khong ban ve duoc).
/// CapacitySnapshot tu dong lay theo so ghe active cua tau, khong cho nhap tay.
/// </summary>
[Authorize(Roles = "Admin,Manager")]
public sealed record CreateTripCommand(
    string RouteCode,
    string BoatCode,
    DateOnly OperatingDate,
    DateTimeOffset DepartureTime,
    IReadOnlyList<TripSeatTypePriceInput>? SeatTypePrices = null) : IRequest<TripDetailDto>;

public sealed class CreateTripCommandValidator : AbstractValidator<CreateTripCommand>
{
    public CreateTripCommandValidator()
    {
        RuleFor(x => x.RouteCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.BoatCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DepartureTime).GreaterThan(DateTimeOffset.UtcNow);
        RuleFor(x => x.SeatTypePrices!)
            .SetValidator(new TripSeatTypePricesValidator())
            .When(x => x.SeatTypePrices is not null);
    }
}

public sealed class TripSeatTypePricesValidator : AbstractValidator<IReadOnlyList<TripSeatTypePriceInput>>
{
    public TripSeatTypePricesValidator()
    {
        RuleFor(x => x)
            .Must(list => list
                .Select(p => p.SeatTypeCode.Trim().ToUpperInvariant())
                .Distinct()
                .Count() == list.Count)
            .WithMessage("seatTypePrices không được trùng seatTypeCode.");
        RuleForEach(x => x).ChildRules(item =>
        {
            item.RuleFor(p => p.SeatTypeCode).NotEmpty().MaximumLength(50);
            item.RuleFor(p => p.Price)
                .GreaterThan(0).WithMessage("Giá vé phải lớn hơn 0.")
                .LessThanOrEqualTo(100_000_000)
                .Must(p => decimal.Truncate(p) == p).WithMessage("Giá vé phải là số nguyên VND.");
        });
    }
}

internal static class TripSeatPricingSupport
{
    /// <summary>
    /// Tính giá cho từng ghế của chuyến: ưu tiên giá admin nhập theo loại ghế,
    /// không nhập thì lấy giá gốc từ seat type (DB seat_types, fallback bảng cứng).
    /// </summary>
    public static async Task<Func<Seat, decimal?>> BuildSeatPriceResolverAsync(
        IApplicationDbContext context,
        IReadOnlyList<TripSeatTypePriceInput>? seatTypePrices,
        IReadOnlyCollection<Seat> boatSeats,
        CancellationToken cancellationToken)
    {
        var basePrices = await SeatTypeBasePriceSupport.GetBasePricesAsync(context, cancellationToken);

        var overrides = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (seatTypePrices is not null)
        {
            var boatSeatTypeCodes = boatSeats
                .Select(x => x.SeatTypeCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var input in seatTypePrices)
            {
                var code = input.SeatTypeCode.Trim().ToUpperInvariant();
                if (!boatSeatTypeCodes.Contains(code))
                {
                    throw new ValidationException([new ValidationFailure("seatTypePrices",
                        $"Tàu này không có ghế loại '{code}'.")]);
                }

                overrides[code] = input.Price;
            }
        }

        return seat =>
            overrides.TryGetValue(seat.SeatTypeCode, out var overridePrice) ? overridePrice
            : basePrices.TryGetValue(seat.SeatTypeCode, out var basePrice) ? basePrice
            : null;
    }
}

public sealed class CreateTripCommandHandler : IRequestHandler<CreateTripCommand, TripDetailDto>
{
    private readonly IApplicationDbContext _context;

    public CreateTripCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<TripDetailDto> Handle(CreateTripCommand request, CancellationToken cancellationToken)
    {
        var routeCode = request.RouteCode.Trim().ToUpperInvariant();

        var route = await _context.Set<Route>()
            .Include(r => r.RouteStops.OrderBy(rs => rs.StopOrder))
                .ThenInclude(rs => rs.Station)
            .SingleOrDefaultAsync(r => r.RouteCode == routeCode && r.Status == "Active", cancellationToken)
            ?? throw new NotFoundException($"Route '{routeCode}' not found or inactive.");

        if (route.RouteStops.Count < 2)
            throw new ValidationException([new ValidationFailure(nameof(request.RouteCode), "Route must have at least 2 stops.")]);

        var tripCode = $"TR-{request.OperatingDate:yyyyMMdd}-{route.RouteCode}-{Random.Shared.Next(1000, 9999)}";

        var departureTime = request.DepartureTime.ToUniversalTime();
        var currentTime = departureTime;
        var stopDtos = new List<TripStopDto>();

        foreach (var routeStop in route.RouteStops.OrderBy(rs => rs.StopOrder))
        {
            var scheduledArrival = routeStop.StopOrder == route.RouteStops.Min(rs => rs.StopOrder)
                ? departureTime
                : currentTime;

            var scheduledDeparture = scheduledArrival.AddMinutes(routeStop.StandardDwellMin ?? 2);

            stopDtos.Add(new TripStopDto(
                routeStop.Id,
                routeStop.Station.Id,
                routeStop.Station.StationName,
                routeStop.Station.StationCode,
                routeStop.StopOrder,
                scheduledArrival,
                scheduledDeparture,
                null,
                null,
                "Scheduled"));

            currentTime = scheduledDeparture.AddMinutes(routeStop.StandardTravelMin ?? 15);
        }

        var arrivalTime = stopDtos.Max(ts => ts.ScheduledArrival ?? departureTime);

        await EnsureNoRouteDepartureConflictAsync(route.Id, departureTime, cancellationToken);

        var boatCode = request.BoatCode.Trim().ToUpperInvariant();
        var boat = await _context.Set<Boat>()
            .SingleOrDefaultAsync(x => x.Code == boatCode, cancellationToken)
            ?? throw new NotFoundException($"Boat '{boatCode}' not found.");

        if (boat.Status != BoatStatus.Active || !boat.SeatsConfigured)
            throw new ValidationException([new ValidationFailure(nameof(request.BoatCode),
                "Boat must be active and have configured seats.")]);

        var activeSeats = await _context.Set<Seat>()
            .Where(x => x.BoatId == boat.Id && x.IsActive)
            .ToListAsync(cancellationToken);

        // Suc chua = so ghe active cua tau (khong cho nhap tay).
        var capacity = activeSeats.Count;
        if (capacity <= 0)
            throw new ValidationException([new ValidationFailure(nameof(request.BoatCode),
                "Boat has no active seats.")]);

        var trip = new Trip
        {
            RouteId = route.Id,
            BoatId = boat.Id,
            TripCode = tripCode,
            OperatingDate = request.OperatingDate,
            DepartureTime = departureTime,
            ArrivalTime = arrivalTime,
            CapacitySnapshot = capacity,
            TripStatus = TripStatus.Scheduled
        };

        _context.Set<Trip>().Add(trip);

        var resolveSeatPrice = await TripSeatPricingSupport.BuildSeatPriceResolverAsync(
            _context, request.SeatTypePrices, activeSeats, cancellationToken);
        _context.Set<TripSeat>().AddRange(activeSeats.Select(s => new TripSeat
        {
            TripId = trip.Id,
            SeatId = s.Id,
            Price = resolveSeatPrice(s)
        }));

        await _context.SaveChangesAsync(cancellationToken);

        return new TripDetailDto(
            trip.Id, trip.TripCode,
            route.Id, route.RouteName,
            trip.DepartureTime, trip.ArrivalTime,
            trip.CapacitySnapshot, trip.TripStatus.ToString(), trip.StatusNote,
            stopDtos.OrderBy(ts => ts.StopOrder).ToList());
    }

    private async Task EnsureNoRouteDepartureConflictAsync(
        Guid routeId,
        DateTimeOffset departureTime,
        CancellationToken cancellationToken)
    {
        var hasConflict = await _context.Set<Trip>()
            .AsNoTracking()
            .AnyAsync(x =>
                x.RouteId == routeId
                && x.DepartureTime == departureTime
                && x.TripStatus != TripStatus.Cancelled,
                cancellationToken);

        if (hasConflict)
        {
            throw new ValidationException(
            [
                new ValidationFailure(
                    nameof(CreateTripCommand.DepartureTime),
                    "Tuyến đã có chuyến tàu xuất phát trong cùng thời điểm.")
            ]);
        }
    }
}
