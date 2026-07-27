using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.StaffWorkAssignments;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Trips;

[Authorize(Roles = "Admin")]
public sealed record PreviewRoundTripScheduleCommand(
    string BoatCode,
    string OutboundRouteCode,
    string InboundRouteCode,
    DateOnly FromDate,
    DateOnly ToDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    IReadOnlyList<int>? DaysOfWeek = null,
    IReadOnlyList<CreateTripStopScheduleInput>? OutboundStops = null,
    IReadOnlyList<CreateTripStopScheduleInput>? InboundStops = null) : IRequest<RoundTripSchedulePreviewResult>;

public sealed record RoundTripSchedulePreviewResult(
    int Suggested,
    int SkippedBoatBusy,
    int SkippedDuplicateRouteTime,
    int SkippedMissingOnBoardStaff,
    int SkippedStationBusy,
    IReadOnlyList<RoundTripSchedulePreviewItemDto> Items);

public sealed record RoundTripSchedulePreviewItemDto(
    DateOnly OperatingDate,
    string Direction,
    Guid RouteId,
    string RouteCode,
    string RouteName,
    DateTimeOffset DepartureTime,
    DateTimeOffset ArrivalTime,
    Guid FromStationId,
    Guid ToStationId,
    string? FromStationName,
    string? ToStationName,
    bool CanCreate,
    string? Reason = null,
    DateTimeOffset? SuggestedNextDepartureTime = null);

public sealed class PreviewRoundTripScheduleCommandValidator : AbstractValidator<PreviewRoundTripScheduleCommand>
{
    public PreviewRoundTripScheduleCommandValidator()
    {
        RuleFor(x => x.BoatCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.OutboundRouteCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.InboundRouteCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x)
            .Must(x => !string.Equals(x.OutboundRouteCode, x.InboundRouteCode, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Route lượt đi và route lượt về phải khác nhau.")
            .OverridePropertyName(nameof(PreviewRoundTripScheduleCommand.InboundRouteCode));
        RuleFor(x => x.FromDate).NotEmpty();
        RuleFor(x => x.ToDate)
            .NotEmpty()
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("ToDate must be on or after FromDate.")
            .Must((cmd, to) => to.DayNumber - cmd.FromDate.DayNumber <= 365)
            .WithMessage("Cannot preview trips for more than 365 days at once.");
        RuleFor(x => x)
            .Must(x => x.StartTime < x.EndTime)
            .WithMessage("EndTime phải lớn hơn StartTime.")
            .OverridePropertyName(nameof(PreviewRoundTripScheduleCommand.EndTime));
        RuleFor(x => x.DaysOfWeek)
            .Must(days => days == null || (days.Count > 0 && days.All(d => d is >= 0 and <= 6)))
            .WithMessage("DaysOfWeek values must be 0-6 (0=Sunday, 6=Saturday).")
            .When(x => x.DaysOfWeek is not null);
        RuleFor(x => x.OutboundStops!)
            .Must(stops => stops.Select(x => x.StopOrder).Distinct().Count() == stops.Count)
            .WithMessage("outboundStops không được trùng stopOrder.")
            .When(x => x.OutboundStops is not null);
        RuleFor(x => x.InboundStops!)
            .Must(stops => stops.Select(x => x.StopOrder).Distinct().Count() == stops.Count)
            .WithMessage("inboundStops không được trùng stopOrder.")
            .When(x => x.InboundStops is not null);
        RuleForEach(x => x.OutboundStops).SetValidator(new PreviewRoundTripStopInputValidator());
        RuleForEach(x => x.InboundStops).SetValidator(new PreviewRoundTripStopInputValidator());
    }
}

internal sealed class PreviewRoundTripStopInputValidator : AbstractValidator<CreateTripStopScheduleInput>
{
    public PreviewRoundTripStopInputValidator()
    {
        RuleFor(x => x.StopOrder)
            .GreaterThan(0)
            .WithMessage("stopOrder phải lớn hơn 0.");
        RuleFor(x => x.StayDurationMinutes)
            .InclusiveBetween(0, 24 * 60)
            .WithMessage("stayDurationMinutes phải từ 0 đến 1440 phút.");
    }
}

public sealed class PreviewRoundTripScheduleCommandHandler
    : IRequestHandler<PreviewRoundTripScheduleCommand, RoundTripSchedulePreviewResult>
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private const int MaxAttemptsPerDate = 500;

    private readonly IApplicationDbContext _context;

    public PreviewRoundTripScheduleCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<RoundTripSchedulePreviewResult> Handle(
        PreviewRoundTripScheduleCommand request,
        CancellationToken cancellationToken)
    {
        var boatCode = request.BoatCode.Trim().ToUpperInvariant();
        var outboundRouteCode = request.OutboundRouteCode.Trim().ToUpperInvariant();
        var inboundRouteCode = request.InboundRouteCode.Trim().ToUpperInvariant();

        var boat = await _context.Set<Boat>()
            .SingleOrDefaultAsync(x => x.Code == boatCode, cancellationToken)
            ?? throw new NotFoundException($"Boat '{boatCode}' not found.");

        if (boat.Status != BoatStatus.Active || !boat.SeatsConfigured)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.BoatCode),
                "Boat must be active and have configured seats.")]);
        }

        var outboundRoute = await LoadRouteAsync(outboundRouteCode, nameof(request.OutboundRouteCode), cancellationToken);
        var inboundRoute = await LoadRouteAsync(inboundRouteCode, nameof(request.InboundRouteCode), cancellationToken);
        EnsureRoutePairIsRoundTrip(outboundRoute, inboundRoute);

        EnsureBoatCompatible(boat, outboundRoute, nameof(request.OutboundRouteCode));
        EnsureBoatCompatible(boat, inboundRoute, nameof(request.InboundRouteCode));

        var outboundStops = outboundRoute.RouteStops.OrderBy(x => x.StopOrder).ToList();
        var inboundStops = inboundRoute.RouteStops.OrderBy(x => x.StopOrder).ToList();
        var outboundStayDurationMinutesByStopOrder = TripStopScheduleSupport.ResolveStayDurationMinutesByStopOrder(
            outboundRoute,
            request.OutboundStops,
            nameof(request.OutboundStops));
        var inboundStayDurationMinutesByStopOrder = TripStopScheduleSupport.ResolveStayDurationMinutesByStopOrder(
            inboundRoute,
            request.InboundStops,
            nameof(request.InboundStops));

        var existingDepartures = await _context.Set<Trip>()
            .AsNoTracking()
            .Where(t => (t.RouteId == outboundRoute.Id || t.RouteId == inboundRoute.Id)
                && t.OperatingDate >= request.FromDate
                && t.OperatingDate <= request.ToDate
                && t.TripStatus != TripStatus.Cancelled)
            .Select(t => new { t.RouteId, t.DepartureTime })
            .ToListAsync(cancellationToken);
        var existingDepartureKeys = existingDepartures
            .Select(x => (x.RouteId, x.DepartureTime))
            .ToHashSet();

        var rangeStart = ToUtc(request.FromDate, new TimeOnly(0, 0));
        var rangeEnd = ToUtc(request.ToDate, new TimeOnly(23, 59, 59));
        var boatSchedule = await LoadBoatScheduleAsync(boat.Id, rangeStart, rangeEnd, cancellationToken);
        var stationDepartureSchedule = await LoadStationDepartureScheduleAsync(rangeStart, rangeEnd, cancellationToken);
        var allowedDays = request.DaysOfWeek is { Count: > 0 }
            ? request.DaysOfWeek.Select(d => (DayOfWeek)d).ToHashSet()
            : null;

        var items = new List<RoundTripSchedulePreviewItemDto>();
        var skippedBoatBusy = 0;
        var skippedDuplicateRouteTime = 0;
        var skippedMissingOnBoardStaff = 0;
        var skippedStationBusy = 0;

        for (var date = request.FromDate; date <= request.ToDate; date = date.AddDays(1))
        {
            if (allowedDays is not null && !allowedDays.Contains(date.DayOfWeek))
            {
                continue;
            }

            var departureTime = ToUtc(date, request.StartTime);
            var endTime = ToUtc(date, request.EndTime);
            var direction = RoundTripDirection.Outbound;
            var attempts = 0;

            while (departureTime <= endTime && attempts++ < MaxAttemptsPerDate)
            {
                var plan = direction == RoundTripDirection.Outbound
                    ? new RoutePreviewPlan("Outbound", outboundRoute, outboundStops, outboundStayDurationMinutesByStopOrder)
                    : new RoutePreviewPlan("Inbound", inboundRoute, inboundStops, inboundStayDurationMinutesByStopOrder);

                var item = await PreviewOneTripAsync(
                    date,
                    boat.Id,
                    departureTime,
                    plan,
                    existingDepartureKeys,
                    boatSchedule,
                    stationDepartureSchedule,
                    cancellationToken);
                items.Add(item);

                if (item.CanCreate)
                {
                    boatSchedule.Add(ToScheduleWindow(plan, item.DepartureTime, item.ArrivalTime, "(preview)"));
                    stationDepartureSchedule.Add(ToStationDepartureWindow(plan, item.DepartureTime, "(preview)"));
                    existingDepartureKeys.Add((plan.Route.Id, item.DepartureTime));
                    departureTime = item.ArrivalTime.Add(TripScheduleSupport.BoatTurnaroundBuffer);
                    direction = direction == RoundTripDirection.Outbound
                        ? RoundTripDirection.Inbound
                        : RoundTripDirection.Outbound;
                    continue;
                }

                if (item.Reason?.Contains("đã có chuyến cùng tuyến", StringComparison.OrdinalIgnoreCase) == true)
                {
                    skippedDuplicateRouteTime++;
                }
                else if (item.Reason?.Contains("thiếu nhân viên OnBoard", StringComparison.OrdinalIgnoreCase) == true)
                {
                    skippedMissingOnBoardStaff++;
                }
                else if (item.Reason?.Contains("Các chuyến cùng bến", StringComparison.OrdinalIgnoreCase) == true)
                {
                    skippedStationBusy++;
                }
                else
                {
                    skippedBoatBusy++;
                }

                departureTime = item.SuggestedNextDepartureTime.HasValue && item.SuggestedNextDepartureTime.Value > departureTime
                    ? item.SuggestedNextDepartureTime.Value
                    : departureTime.AddMinutes(5);
            }
        }

        return new RoundTripSchedulePreviewResult(
            items.Count(x => x.CanCreate),
            skippedBoatBusy,
            skippedDuplicateRouteTime,
            skippedMissingOnBoardStaff,
            skippedStationBusy,
            items);
    }

    private async Task<Route> LoadRouteAsync(
        string routeCode,
        string propertyName,
        CancellationToken cancellationToken)
    {
        var route = await _context.Set<Route>()
            .Include(x => x.RouteStops.OrderBy(stop => stop.StopOrder))
                .ThenInclude(x => x.Station)
            .SingleOrDefaultAsync(x => x.RouteCode == routeCode && x.Status == "Active" && x.IsBookable, cancellationToken)
            ?? throw new NotFoundException($"Route '{routeCode}' not found, inactive, or not bookable.");

        if (route.RouteStops.Count < 2)
        {
            throw new ValidationException([new ValidationFailure(propertyName,
                "Route must have at least 2 stops.")]);
        }

        return route;
    }

    private static void EnsureRoutePairIsRoundTrip(Route outboundRoute, Route inboundRoute)
    {
        var outboundStops = outboundRoute.RouteStops.OrderBy(x => x.StopOrder).ToList();
        var inboundStops = inboundRoute.RouteStops.OrderBy(x => x.StopOrder).ToList();
        var outboundStart = TripScheduleSupport.ResolveStartStationId(outboundStops);
        var outboundEnd = TripScheduleSupport.ResolveEndStationId(outboundStops);
        var inboundStart = TripScheduleSupport.ResolveStartStationId(inboundStops);
        var inboundEnd = TripScheduleSupport.ResolveEndStationId(inboundStops);

        if (outboundEnd != inboundStart || inboundEnd != outboundStart)
        {
            throw new ValidationException([new ValidationFailure(nameof(PreviewRoundTripScheduleCommand.InboundRouteCode),
                "Route lượt về phải bắt đầu tại bến cuối của lượt đi và kết thúc tại bến đầu của lượt đi.")]);
        }
    }

    private static void EnsureBoatCompatible(Boat boat, Route route, string propertyName)
    {
        if (!BoatRouteCompatibilitySupport.IsCompatible(route.RouteType, boat.SeatSetupType))
        {
            throw new ValidationException([new ValidationFailure(propertyName,
                BoatRouteCompatibilitySupport.BuildIncompatibleMessage(route.RouteType, boat.SeatSetupType))]);
        }
    }

    private async Task<List<TripScheduleSupport.BoatScheduleWindow>> LoadBoatScheduleAsync(
        Guid boatId,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd,
        CancellationToken cancellationToken)
    {
        return (await _context.Set<Trip>()
                .AsNoTracking()
                .Include(x => x.Route).ThenInclude(x => x.RouteStops)
                .Where(x => x.BoatId == boatId
                    && x.TripStatus != TripStatus.Cancelled
                    && x.ArrivalTime >= rangeStart.AddDays(-1)
                    && x.DepartureTime <= rangeEnd.AddDays(1))
                .ToListAsync(cancellationToken))
            .Where(x => x.Route.RouteStops.Count >= 2)
            .Select(x =>
            {
                var routeStops = x.Route.RouteStops.OrderBy(stop => stop.StopOrder).ToList();
                return new TripScheduleSupport.BoatScheduleWindow(
                    x.TripCode,
                    x.DepartureTime,
                    x.ArrivalTime,
                    TripScheduleSupport.ResolveStartStationId(routeStops),
                    TripScheduleSupport.ResolveEndStationId(routeStops),
                    routeStops);
            })
            .ToList();
    }

    private async Task<List<TripScheduleSupport.StationDepartureWindow>> LoadStationDepartureScheduleAsync(
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd,
        CancellationToken cancellationToken)
    {
        return (await _context.Set<Trip>()
                .AsNoTracking()
                .Include(x => x.Route).ThenInclude(x => x.RouteStops).ThenInclude(x => x.Station)
                .Where(x => x.TripStatus != TripStatus.Cancelled
                    && x.DepartureTime >= rangeStart.Subtract(TripScheduleSupport.StationDepartureBuffer)
                    && x.DepartureTime <= rangeEnd.Add(TripScheduleSupport.StationDepartureBuffer))
                .ToListAsync(cancellationToken))
            .Where(x => x.Route.RouteStops.Count >= 2)
            .Select(x =>
            {
                var firstStop = x.Route.RouteStops.OrderBy(stop => stop.StopOrder).First();
                return new TripScheduleSupport.StationDepartureWindow(
                    x.TripCode,
                    firstStop.StationId,
                    firstStop.Station?.StationName,
                    x.DepartureTime);
            })
            .ToList();
    }

    private async Task<RoundTripSchedulePreviewItemDto> PreviewOneTripAsync(
        DateOnly date,
        Guid boatId,
        DateTimeOffset departureTime,
        RoutePreviewPlan plan,
        HashSet<(Guid RouteId, DateTimeOffset DepartureTime)> existingDepartureKeys,
        IReadOnlyList<TripScheduleSupport.BoatScheduleWindow> boatSchedule,
        IReadOnlyList<TripScheduleSupport.StationDepartureWindow> stationDepartureSchedule,
        CancellationToken cancellationToken)
    {
        var stopDrafts = TripStopScheduleSupport.BuildFromRouteStops(
            plan.RouteStops,
            departureTime,
            plan.StayDurationMinutesByStopOrder);
        var arrivalTime = stopDrafts[^1].PlannedArrivalTime ?? departureTime;

        if (existingDepartureKeys.Contains((plan.Route.Id, departureTime)))
        {
            return ToItem(
                date,
                plan,
                departureTime,
                arrivalTime,
                canCreate: false,
                reason: "Route đã có chuyến cùng tuyến cùng giờ.",
                suggestedNextDepartureTime: departureTime.AddMinutes(5));
        }

        var requestedWindow = ToScheduleWindow(plan, departureTime, arrivalTime, "(preview)");
        var stationConflict = TripScheduleSupport.FindStationDepartureConflict(
            ToStationDepartureWindow(plan, departureTime, "(preview)"),
            stationDepartureSchedule);
        if (stationConflict is not null)
        {
            return ToItem(
                date,
                plan,
                departureTime,
                arrivalTime,
                canCreate: false,
                reason: TripScheduleSupport.BuildStationDepartureConflictMessage(
                    stationConflict.Existing.StationName,
                    stationConflict.Existing.TripCode,
                    stationConflict.Existing.DepartureTime,
                    stationConflict.EarliestAllowedDeparture),
                suggestedNextDepartureTime: stationConflict.EarliestAllowedDeparture);
        }

        var conflict = TripScheduleSupport.FindConflict(requestedWindow, boatSchedule);
        if (conflict is not null)
        {
            return ToItem(
                date,
                plan,
                departureTime,
                arrivalTime,
                canCreate: false,
                reason: TripScheduleSupport.BuildLocationAwareConflictMessage(
                    conflict.Existing.TripCode,
                    conflict.Existing.DepartureTime,
                    conflict.Existing.ArrivalTime,
                    conflict.EarliestAllowedDeparture,
                    conflict.RepositionDuration),
                suggestedNextDepartureTime: conflict.EarliestAllowedDeparture);
        }

        if (!await OnBoardStaffTripSupport.HasRequiredOnBoardStaffAsync(
                _context, boatId, departureTime, arrivalTime, cancellationToken))
        {
            return ToItem(
                date,
                plan,
                departureTime,
                arrivalTime,
                canCreate: false,
                reason: "Tàu thiếu nhân viên OnBoard trong khung giờ này.",
                suggestedNextDepartureTime: arrivalTime.Add(TripScheduleSupport.BoatTurnaroundBuffer));
        }

        return ToItem(date, plan, departureTime, arrivalTime, canCreate: true);
    }

    private static TripScheduleSupport.BoatScheduleWindow ToScheduleWindow(
        RoutePreviewPlan plan,
        DateTimeOffset departureTime,
        DateTimeOffset arrivalTime,
        string tripCode)
    {
        return new TripScheduleSupport.BoatScheduleWindow(
            tripCode,
            departureTime,
            arrivalTime,
            TripScheduleSupport.ResolveStartStationId(plan.RouteStops),
            TripScheduleSupport.ResolveEndStationId(plan.RouteStops),
            plan.RouteStops);
    }

    private static TripScheduleSupport.StationDepartureWindow ToStationDepartureWindow(
        RoutePreviewPlan plan,
        DateTimeOffset departureTime,
        string tripCode)
    {
        var firstStop = plan.RouteStops.OrderBy(x => x.StopOrder).First();
        return new TripScheduleSupport.StationDepartureWindow(
            tripCode,
            firstStop.StationId,
            firstStop.Station?.StationName,
            departureTime);
    }

    private static RoundTripSchedulePreviewItemDto ToItem(
        DateOnly date,
        RoutePreviewPlan plan,
        DateTimeOffset departureTime,
        DateTimeOffset arrivalTime,
        bool canCreate,
        string? reason = null,
        DateTimeOffset? suggestedNextDepartureTime = null)
    {
        var orderedStops = plan.RouteStops.OrderBy(x => x.StopOrder).ToList();
        var fromStop = orderedStops[0];
        var toStop = orderedStops[^1];
        return new RoundTripSchedulePreviewItemDto(
            date,
            plan.Direction,
            plan.Route.Id,
            plan.Route.RouteCode,
            plan.Route.RouteName,
            departureTime,
            arrivalTime,
            fromStop.StationId,
            toStop.StationId,
            fromStop.Station?.StationName,
            toStop.Station?.StationName,
            canCreate,
            reason,
            suggestedNextDepartureTime);
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time) =>
        new DateTimeOffset(
            date.Year,
            date.Month,
            date.Day,
            time.Hour,
            time.Minute,
            time.Second,
            VietnamOffset).ToUniversalTime();

    private enum RoundTripDirection
    {
        Outbound,
        Inbound
    }

    private sealed record RoutePreviewPlan(
        string Direction,
        Route Route,
        IReadOnlyList<RouteStop> RouteStops,
        IReadOnlyDictionary<int, int>? StayDurationMinutesByStopOrder);
}
