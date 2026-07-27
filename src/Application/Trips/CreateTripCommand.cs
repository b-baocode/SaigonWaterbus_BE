using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Fares;
using SaigonWaterbus.Application.StaffWorkAssignments;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Trips;

/// <summary>Override thời gian dừng tại một bến khi tạo trip từ route stops.</summary>
public sealed record CreateTripStopScheduleInput(int StopOrder, int StayDurationMinutes);

/// <summary>
/// Tao chuyen tau. BoatCode BAT BUOC: trip luon gan tau de sinh trip_seats (khong co ghe thi khong ban ve duoc).
/// CapacitySnapshot tu dong lay theo so ghe active cua tau, khong cho nhap tay.
/// </summary>
[Authorize(Roles = "Admin")]
public sealed record CreateTripCommand(
    string? RouteCode,
    string BoatCode,
    DateOnly OperatingDate,
    DateTimeOffset DepartureTime,
    Guid? RouteId = null,
    IReadOnlyList<CreateTripStopScheduleInput>? Stops = null) : IRequest<TripDetailDto>;

public sealed class CreateTripCommandValidator : AbstractValidator<CreateTripCommand>
{
    public CreateTripCommandValidator()
    {
        RuleFor(x => x.RouteCode).MaximumLength(50).When(x => x.RouteCode is not null);
        RuleFor(x => x.RouteId).NotEmpty().When(x => x.RouteId.HasValue);
        RuleFor(x => x)
            .Must(x => x.RouteId.HasValue || !string.IsNullOrWhiteSpace(x.RouteCode))
            .WithMessage("routeId hoặc routeCode là bắt buộc.")
            .OverridePropertyName(nameof(CreateTripCommand.RouteId));
        RuleFor(x => x.BoatCode).NotEmpty().MaximumLength(50);
        // UtcNow lay trong lambda (khong phai luc dung validator) de moc lead time luon tinh theo
        // thoi diem goi API, ke ca khi validator duoc cache lai.
        RuleFor(x => x.DepartureTime)
            .Must(departureTime =>
                !TripScheduleSupport.IsTooSoonToCreate(departureTime, DateTimeOffset.UtcNow))
            .WithMessage(TripScheduleSupport.BuildTooSoonMessage());
        RuleFor(x => x.Stops!)
            .Must(stops => stops.Select(x => x.StopOrder).Distinct().Count() == stops.Count)
            .WithMessage("stops không được trùng stopOrder.")
            .When(x => x.Stops is not null);
        RuleForEach(x => x.Stops).ChildRules(stop =>
        {
            stop.RuleFor(x => x.StopOrder)
                .GreaterThan(0)
                .WithMessage("stopOrder phải lớn hơn 0.");
            stop.RuleFor(x => x.StayDurationMinutes)
                .InclusiveBetween(0, 24 * 60)
                .WithMessage("stayDurationMinutes phải từ 0 đến 1440 phút.");
        });
    }
}

public sealed class CreateTripCommandHandler : IRequestHandler<CreateTripCommand, TripDetailDto>
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private readonly IApplicationDbContext _context;

    public CreateTripCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<TripDetailDto> Handle(CreateTripCommand request, CancellationToken cancellationToken)
    {
        // operatingDate nhập tay riêng với departureTime nên phải check khớp ngày (theo giờ VN),
        // không thì trip nằm sai ngày trong các query lọc theo operating_date.
        var vietnamDepartureDate = DateOnly.FromDateTime(
            request.DepartureTime.ToOffset(VietnamOffset).Date);
        if (vietnamDepartureDate != request.OperatingDate)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.OperatingDate),
                $"operatingDate ({request.OperatingDate:dd/MM/yyyy}) không khớp ngày khởi hành theo giờ Việt Nam ({vietnamDepartureDate:dd/MM/yyyy}).")]);
        }

        var route = await ResolveRouteAsync(request, cancellationToken);

        if (route.RouteStops.Count < 2)
            throw new ValidationException([new ValidationFailure(nameof(request.RouteId), "Route must have at least 2 stops.")]);

        var tripCode = TripCodeSupport.BuildRegularOrSightseeingTripCode(
            route,
            request.OperatingDate,
            Random.Shared.Next(1000, 9999).ToString());

        var departureTime = request.DepartureTime.ToUniversalTime();
        var stopDrafts = TripStopScheduleSupport.BuildFromRouteStops(
            route.RouteStops,
            departureTime,
            TripStopScheduleSupport.ResolveStayDurationMinutesByStopOrder(
                route,
                request.Stops,
                nameof(request.Stops)));
        var arrivalTime = stopDrafts[^1].PlannedArrivalTime ?? departureTime;

        await EnsureNoRouteDepartureConflictAsync(route.Id, departureTime, cancellationToken);

        var boatCode = request.BoatCode.Trim().ToUpperInvariant();
        var boat = await _context.Set<Boat>()
            .SingleOrDefaultAsync(x => x.Code == boatCode, cancellationToken)
            ?? throw new NotFoundException($"Boat '{boatCode}' not found.");

        if (boat.Status != BoatStatus.Active || !boat.SeatsConfigured)
            throw new ValidationException([new ValidationFailure(nameof(request.BoatCode),
                "Boat must be active and have configured seats.")]);

        if (!BoatRouteCompatibilitySupport.IsCompatible(route.RouteType, boat.SeatSetupType))
            throw new ValidationException([new ValidationFailure(nameof(request.BoatCode),
                BoatRouteCompatibilitySupport.BuildIncompatibleMessage(route.RouteType, boat.SeatSetupType))]);

        var activeSeats = await _context.Set<Seat>()
            .Where(x => x.BoatId == boat.Id && x.IsActive)
            .ToListAsync(cancellationToken);

        // Suc chua = so ghe active cua tau (khong cho nhap tay).
        var capacity = activeSeats.Count;
        if (capacity <= 0)
            throw new ValidationException([new ValidationFailure(nameof(request.BoatCode),
                "Boat has no active seats.")]);

        await EnsureBoatIsFreeAsync(boat.Id, route, departureTime, arrivalTime, cancellationToken);
        await OnBoardStaffTripSupport.EnsureBoatHasRequiredOnBoardStaffAsync(
            _context, boat.Id, departureTime, arrivalTime, nameof(request.BoatCode), cancellationToken);

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
        trip.Route = route;
        TripStopScheduleSupport.CreateTripStops(trip, stopDrafts);

        _context.Set<TripSeat>().AddRange(activeSeats.Select(s => new TripSeat
        {
            TripId = trip.Id,
            SeatId = s.Id,
            Price = null
        }));

        await _context.SaveChangesAsync(cancellationToken);
        var onBoardStaff = await OnBoardStaffTripSupport.LoadTripOnBoardStaffAsync(
            _context, trip, DateTimeOffset.UtcNow, cancellationToken);

        return new TripDetailDto(
            trip.Id, trip.TripCode,
            route.Id, route.RouteName,
            route.RouteType,
            DistanceFareSupport.UsesDistanceFare(trip.TripType, route.RouteType),
            trip.DepartureTime, trip.ArrivalTime,
            trip.CapacitySnapshot, trip.TripStatus.ToString(), trip.StatusNote,
            TripStopScheduleSupport.BuildStopDtos(trip),
            Boat: TripMediaSupport.ToBoatDto(boat, trip.CapacitySnapshot),
            OnBoardStaff: onBoardStaff,
            RouteCode: route.RouteCode,
            FromStation: TripMediaSupport.ResolveFromStation(trip),
            ToStation: TripMediaSupport.ResolveToStation(trip),
            StopCount: trip.TripStops.Count);
    }

    private async Task<Route> ResolveRouteAsync(
        CreateTripCommand request,
        CancellationToken cancellationToken)
    {
        var query = _context.Set<Route>()
            .Include(r => r.RouteStops.OrderBy(rs => rs.StopOrder))
                .ThenInclude(rs => rs.Station)
            .Where(r => r.Status == "Active" && r.IsBookable);

        if (request.RouteId.HasValue)
        {
            return await query.SingleOrDefaultAsync(r => r.Id == request.RouteId.Value, cancellationToken)
                ?? throw new NotFoundException($"Route '{request.RouteId.Value}' not found, inactive, or not bookable.");
        }

        var routeCode = request.RouteCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(routeCode))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.RouteId),
                "routeId hoặc routeCode là bắt buộc.")]);
        }

        return await query.SingleOrDefaultAsync(r => r.RouteCode == routeCode, cancellationToken)
            ?? throw new NotFoundException($"Route '{routeCode}' not found, inactive, or not bookable.");
    }

    /// <summary>
    /// Mot tau khong the chay 2 chuyen chong gio (ke ca khac tuyen), va giua 2 chuyen
    /// phai co thoi gian quay dau toi thieu.
    /// </summary>
    private async Task EnsureBoatIsFreeAsync(
        Guid boatId,
        Route route,
        DateTimeOffset departureTime,
        DateTimeOffset arrivalTime,
        CancellationToken cancellationToken)
    {
        var routeStops = route.RouteStops.OrderBy(x => x.StopOrder).ToList();
        var requestedWindow = new TripScheduleSupport.BoatScheduleWindow(
            "(new)",
            departureTime,
            arrivalTime,
            TripScheduleSupport.ResolveStartStationId(routeStops),
            TripScheduleSupport.ResolveEndStationId(routeStops),
            routeStops);

        var existingWindows = (await _context.Set<Trip>()
            .AsNoTracking()
            .Include(x => x.Route).ThenInclude(x => x.RouteStops)
            .Where(x => x.BoatId == boatId
                && x.TripStatus != TripStatus.Cancelled
                && x.ArrivalTime >= departureTime.AddHours(-24)
                && x.DepartureTime <= arrivalTime.AddHours(24))
            .OrderBy(x => x.DepartureTime)
            .ToListAsync(cancellationToken))
            .Where(x => x.Route.RouteStops.Count >= 2)
            .Select(x =>
            {
                var existingRouteStops = x.Route.RouteStops.OrderBy(stop => stop.StopOrder).ToList();
                return new TripScheduleSupport.BoatScheduleWindow(
                    x.TripCode,
                    x.DepartureTime,
                    x.ArrivalTime,
                    TripScheduleSupport.ResolveStartStationId(existingRouteStops),
                    TripScheduleSupport.ResolveEndStationId(existingRouteStops),
                    existingRouteStops);
            })
            .ToList();

        var conflict = TripScheduleSupport.FindConflict(requestedWindow, existingWindows);
        if (conflict is not null)
        {
            throw new ValidationException(
            [
                new ValidationFailure(
                    nameof(CreateTripCommand.BoatCode),
                    TripScheduleSupport.BuildLocationAwareConflictMessage(
                        conflict.Existing.TripCode,
                        conflict.Existing.DepartureTime,
                        conflict.Existing.ArrivalTime,
                        conflict.EarliestAllowedDeparture,
                        conflict.RepositionDuration))
            ]);
        }
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
