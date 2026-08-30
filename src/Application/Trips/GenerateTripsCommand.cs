using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.StaffWorkAssignments;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Trips;

public interface IGenerateTripsScheduleRequest
{
    string RouteCode { get; }
    string BoatCode { get; }
    IReadOnlyList<TimeOnly>? DepartureTimes { get; }
    DateOnly FromDate { get; }
    DateOnly ToDate { get; }
    IReadOnlyList<int>? DaysOfWeek { get; }
    TimeOnly? StartTime { get; }
    TimeOnly? EndTime { get; }
    int? IntervalMinutes { get; }
    IReadOnlyList<CreateTripStopScheduleInput>? Stops { get; }
}

[Authorize(Roles = "Admin")]
public sealed record GenerateTripsCommand(
    string RouteCode,
    string BoatCode,
    IReadOnlyList<TimeOnly>? DepartureTimes,
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<int>? DaysOfWeek = null,
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null,
    int? IntervalMinutes = null,
    IReadOnlyList<CreateTripStopScheduleInput>? Stops = null) : IRequest<GenerateTripsResult>, IGenerateTripsScheduleRequest;

[Authorize(Roles = "Admin")]
public sealed record PreviewTripsScheduleCommand(
    string RouteCode,
    string BoatCode,
    IReadOnlyList<TimeOnly>? DepartureTimes,
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<int>? DaysOfWeek = null,
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null,
    int? IntervalMinutes = null,
    IReadOnlyList<CreateTripStopScheduleInput>? Stops = null) : IRequest<PreviewTripsScheduleResult>, IGenerateTripsScheduleRequest;

public sealed record GenerateTripsResult(
    int Created,
    int Skipped,
    int SkippedBoatBusy,
    /// <summary>Chuyến bị bỏ qua vì giờ chạy đã trôi qua hoặc cách hiện tại chưa đủ lead time tối thiểu.</summary>
    int SkippedPast,
    IReadOnlyList<string> CreatedTripCodes,
    int SkippedMissingOnBoardStaff = 0,
    int SkippedStationBusy = 0,
    IReadOnlyList<GenerateTripsSkippedItemDto>? SkippedItems = null);

public sealed record GenerateTripsSkippedItemDto(
    DateOnly OperatingDate,
    string RouteCode,
    DateTimeOffset RequestedDepartureTime,
    DateTimeOffset? RequestedArrivalTime,
    string Reason,
    DateTimeOffset? EarliestAllowedDepartureTime = null,
    string? ConflictTripCode = null,
    DateTimeOffset? ConflictDepartureTime = null,
    DateTimeOffset? ConflictArrivalTime = null);

public sealed record PreviewTripsScheduleResult(
    int WouldCreate,
    int WouldSkip,
    bool HasWarnings,
    int Skipped,
    int SkippedBoatBusy,
    int SkippedPast,
    int SkippedMissingOnBoardStaff,
    int SkippedStationBusy,
    IReadOnlyList<PreviewTripsScheduleItemDto> Items);

public sealed record PreviewTripsScheduleItemDto(
    DateOnly OperatingDate,
    string RouteCode,
    DateTimeOffset RequestedDepartureTime,
    DateTimeOffset? RequestedArrivalTime,
    bool CanCreate,
    string? TripCode = null,
    string? Reason = null,
    DateTimeOffset? EarliestAllowedDepartureTime = null,
    string? ConflictTripCode = null,
    DateTimeOffset? ConflictDepartureTime = null,
    DateTimeOffset? ConflictArrivalTime = null);

public sealed class GenerateTripsCommandValidator : GenerateTripsScheduleValidator<GenerateTripsCommand>
{
}

public sealed class PreviewTripsScheduleCommandValidator : GenerateTripsScheduleValidator<PreviewTripsScheduleCommand>
{
}

public abstract class GenerateTripsScheduleValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : IGenerateTripsScheduleRequest
{
    protected GenerateTripsScheduleValidator()
    {
        RuleFor(x => x.RouteCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.BoatCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x)
            .Must(HasOneScheduleInput)
            .WithMessage("Gui departureTimes hoac startTime/endTime/intervalMinutes.")
            .OverridePropertyName(nameof(IGenerateTripsScheduleRequest.DepartureTimes));
        RuleFor(x => x)
            .Must(x => !HasExplicitDepartureTimes(x) || !HasTimeRange(x))
            .WithMessage("Chi duoc gui mot trong hai cach: departureTimes hoac startTime/endTime/intervalMinutes.")
            .OverridePropertyName(nameof(IGenerateTripsScheduleRequest.DepartureTimes));
        RuleFor(x => x.IntervalMinutes)
            .GreaterThanOrEqualTo(5)
            .When(x => x.IntervalMinutes.HasValue)
            .WithMessage("IntervalMinutes phai >= 5.");
        RuleFor(x => x)
            .Must(x => !HasTimeRange(x) || x.StartTime!.Value < x.EndTime!.Value)
            .WithMessage("EndTime phai lon hon StartTime.")
            .OverridePropertyName(nameof(IGenerateTripsScheduleRequest.EndTime));
        RuleFor(x => x.FromDate).NotEmpty();
        RuleFor(x => x.ToDate)
            .NotEmpty()
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("ToDate must be on or after FromDate.")
            .Must((cmd, to) => to.DayNumber - cmd.FromDate.DayNumber <= 365)
            .WithMessage("Cannot generate trips for more than 365 days at once.");
        RuleFor(x => x.DaysOfWeek)
            .Must(days => days == null || (days.Count > 0 && days.All(d => d is >= 0 and <= 6)))
            .WithMessage("DaysOfWeek values must be 0–6 (0=Sunday, 6=Saturday).")
            .When(x => x.DaysOfWeek is not null);
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

    private static bool HasExplicitDepartureTimes(TRequest command) =>
        command.DepartureTimes is { Count: > 0 };

    private static bool HasContinuousSchedule(TRequest command) =>
        command.StartTime.HasValue
        && command.EndTime.HasValue
        && command.IntervalMinutes.HasValue;

    private static bool HasTimeRange(TRequest command) =>
        command.StartTime.HasValue
        && command.EndTime.HasValue;

    private static bool HasOneScheduleInput(TRequest command) =>
        HasExplicitDepartureTimes(command) || HasContinuousSchedule(command);
}

public sealed class GenerateTripsCommandHandler : IRequestHandler<GenerateTripsCommand, GenerateTripsResult>
{
    private readonly IApplicationDbContext _context;

    public GenerateTripsCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<GenerateTripsResult> Handle(GenerateTripsCommand request, CancellationToken cancellationToken)
    {
        var plan = await new GenerateTripsSchedulePlanner(_context).BuildPlanAsync(request, cancellationToken);

        if (plan.TripsToAdd.Count > 0)
        {
            _context.Set<Trip>().AddRange(plan.TripsToAdd);
            _context.Set<TripSeat>().AddRange(plan.TripSeatsToAdd);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return plan.ToGenerateTripsResult();
    }
}

public sealed class PreviewTripsScheduleCommandHandler
    : IRequestHandler<PreviewTripsScheduleCommand, PreviewTripsScheduleResult>
{
    private readonly IApplicationDbContext _context;

    public PreviewTripsScheduleCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<PreviewTripsScheduleResult> Handle(
        PreviewTripsScheduleCommand request,
        CancellationToken cancellationToken)
    {
        var plan = await new GenerateTripsSchedulePlanner(_context).BuildPlanAsync(request, cancellationToken);
        return plan.ToPreviewResult();
    }
}

internal sealed record GenerateTripsSchedulePlan(
    List<Trip> TripsToAdd,
    List<TripSeat> TripSeatsToAdd,
    int Skipped,
    int SkippedBoatBusy,
    int SkippedPast,
    int SkippedMissingOnBoardStaff,
    int SkippedStationBusy,
    IReadOnlyList<string> CreatedTripCodes,
    IReadOnlyList<GenerateTripsSkippedItemDto> SkippedItems,
    IReadOnlyList<PreviewTripsScheduleItemDto> PreviewItems)
{
    public GenerateTripsResult ToGenerateTripsResult() =>
        new(
            TripsToAdd.Count,
            Skipped,
            SkippedBoatBusy,
            SkippedPast,
            CreatedTripCodes,
            SkippedMissingOnBoardStaff,
            SkippedStationBusy,
            SkippedItems);

    public PreviewTripsScheduleResult ToPreviewResult()
    {
        var wouldSkip = PreviewItems.Count(x => !x.CanCreate);
        return new PreviewTripsScheduleResult(
            TripsToAdd.Count,
            wouldSkip,
            wouldSkip > 0,
            Skipped,
            SkippedBoatBusy,
            SkippedPast,
            SkippedMissingOnBoardStaff,
            SkippedStationBusy,
            PreviewItems);
    }
}

internal sealed record TripScheduleAttempt(
    DateTimeOffset RequestedDepartureTime,
    DateTimeOffset RequestedArrivalTime,
    DateTimeOffset? EarliestAllowedDepartureTime);

internal sealed class GenerateTripsSchedulePlanner
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private readonly IApplicationDbContext _context;

    public GenerateTripsSchedulePlanner(IApplicationDbContext context) => _context = context;

    public async Task<GenerateTripsSchedulePlan> BuildPlanAsync(
        IGenerateTripsScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var routeCode = request.RouteCode.Trim().ToUpperInvariant();
        var boatCode = request.BoatCode.Trim().ToUpperInvariant();

        var route = await _context.Set<Route>()
            .Include(r => r.RouteStops.OrderBy(rs => rs.StopOrder))
                .ThenInclude(rs => rs.Station)
            .SingleOrDefaultAsync(r => r.RouteCode == routeCode && r.Status == "Active" && r.IsBookable, cancellationToken)
            ?? throw new NotFoundException($"Route '{routeCode}' not found, inactive, or not bookable.");

        if (route.RouteStops.Count < 2)
            throw new ValidationException([new ValidationFailure(nameof(request.RouteCode),
                "Route must have at least 2 stops.")]);
        TripRouteDistanceValidationSupport.EnsureCompleteRegularRouteDistance(route, nameof(request.RouteCode));
        var routeStops = route.RouteStops.OrderBy(x => x.StopOrder).ToList();
        var routeStartStationId = TripScheduleSupport.ResolveStartStationId(routeStops);
        var routeEndStationId = TripScheduleSupport.ResolveEndStationId(routeStops);

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

        if (activeSeats.Count == 0)
            throw new ValidationException([new ValidationFailure(nameof(request.BoatCode),
                "Boat has no active seats.")]);

        var existingDepartures = await _context.Set<Trip>()
            .AsNoTracking()
            .Where(t => t.RouteId == route.Id
                     && t.OperatingDate >= request.FromDate
                     && t.OperatingDate <= request.ToDate
                     && t.TripStatus != TripStatus.Cancelled)
            .Select(t => t.DepartureTime)
            .ToHashSetAsync(cancellationToken);

        var departureTimes = ResolveDepartureTimes(request);
        var stayDurationMinutesByStopOrder = TripStopScheduleSupport.ResolveStayDurationMinutesByStopOrder(
            route,
            request.Stops,
            nameof(request.Stops));
        var allowedDays = request.DaysOfWeek is { Count: > 0 }
            ? request.DaysOfWeek.Select(d => (DayOfWeek)d).ToHashSet()
            : null;

        // Lich chay hien co cua TAU (moi tuyen, khong chi tuyen nay) de chan double-booking.
        var rangeStart = new DateTimeOffset(
            request.FromDate.Year, request.FromDate.Month, request.FromDate.Day,
            0, 0, 0, VietnamOffset).ToUniversalTime();
        var rangeEnd = new DateTimeOffset(
            request.ToDate.Year, request.ToDate.Month, request.ToDate.Day,
            23, 59, 59, VietnamOffset).ToUniversalTime();

        var boatSchedule = (await _context.Set<Trip>()
                .AsNoTracking()
                .Include(t => t.Route).ThenInclude(r => r.RouteStops).ThenInclude(x => x.Station)
                .Where(t => t.BoatId == boat.Id
                    && t.TripStatus != TripStatus.Cancelled
                    && t.ArrivalTime >= rangeStart.AddDays(-1)
                    && t.DepartureTime <= rangeEnd.AddDays(1))
                .ToListAsync(cancellationToken))
            .Where(t => t.Route.RouteStops.Count >= 2)
            .Select(t =>
            {
                var existingRouteStops = t.Route.RouteStops.OrderBy(stop => stop.StopOrder).ToList();
                return new TripScheduleSupport.BoatScheduleWindow(
                    t.TripCode,
                    t.DepartureTime,
                    t.ArrivalTime,
                    TripScheduleSupport.ResolveStartStationId(existingRouteStops),
                    TripScheduleSupport.ResolveEndStationId(existingRouteStops),
                    existingRouteStops);
            })
            .ToList();

        var stationDepartureSchedule = (await _context.Set<Trip>()
                .AsNoTracking()
                .Include(t => t.Route).ThenInclude(r => r.RouteStops).ThenInclude(x => x.Station)
                .Where(t => t.TripStatus != TripStatus.Cancelled
                    && t.DepartureTime >= rangeStart.Subtract(TripScheduleSupport.StationDepartureBuffer)
                    && t.DepartureTime <= rangeEnd.Add(TripScheduleSupport.StationDepartureBuffer))
                .ToListAsync(cancellationToken))
            .Where(t => t.Route.RouteStops.Count >= 2)
            .Select(t =>
            {
                var firstStop = t.Route.RouteStops.OrderBy(stop => stop.StopOrder).First();
                return new TripScheduleSupport.StationDepartureWindow(
                    t.TripCode,
                    firstStop.StationId,
                    firstStop.Station?.StationName,
                    t.Route.RouteType,
                    t.DepartureTime);
            })
            .ToList();

        var skippedBoatBusy = 0;

        var tripsToAdd = new List<Trip>();
        var tripSeatsToAdd = new List<TripSeat>();
        var createdCodes = new List<string>();
        var skippedItems = new List<GenerateTripsSkippedItemDto>();
        var previewItems = new List<PreviewTripsScheduleItemDto>();
        int skipped = 0;
        int skippedPast = 0;
        int skippedMissingOnBoardStaff = 0;
        int skippedStationBusy = 0;
        var now = DateTimeOffset.UtcNow;

        void AddSkippedItem(GenerateTripsSkippedItemDto item)
        {
            skippedItems.Add(item);
            previewItems.Add(new PreviewTripsScheduleItemDto(
                item.OperatingDate,
                item.RouteCode,
                item.RequestedDepartureTime,
                item.RequestedArrivalTime,
                CanCreate: false,
                Reason: item.Reason,
                EarliestAllowedDepartureTime: item.EarliestAllowedDepartureTime,
                ConflictTripCode: item.ConflictTripCode,
                ConflictDepartureTime: item.ConflictDepartureTime,
                ConflictArrivalTime: item.ConflictArrivalTime));
        }

        async Task<TripScheduleAttempt> TryCreateTripAsync(DateOnly date, DateTimeOffset departureTime)
        {
            var stopDrafts = TripStopScheduleSupport.BuildFromRouteStops(
                routeStops,
                departureTime,
                stayDurationMinutesByStopOrder,
                route.RouteType,
                route.EstimatedDurationMin);
            var arrivalTime = stopDrafts[^1].PlannedArrivalTime ?? departureTime;

            // Cùng chuẩn với tạo lẻ (phải trước giờ chạy tối thiểu theo TripScheduleSupport): không sinh chuyến
            // đã trôi qua hoặc quá sát giờ, nhưng batch thì skip đếm riêng thay vì fail cả lô.
            if (TripScheduleSupport.IsTooSoonToCreate(departureTime, now))
            {
                skippedPast++;
                var minimumDeparture = now.Add(TripScheduleSupport.MinimumCreationLeadTime);
                AddSkippedItem(new GenerateTripsSkippedItemDto(
                    date,
                    route.RouteCode,
                    departureTime,
                    arrivalTime,
                    TripScheduleSupport.BuildTooSoonMessage(),
                    minimumDeparture));
                return new TripScheduleAttempt(departureTime, arrivalTime, minimumDeparture);
            }

            if (existingDepartures.Contains(departureTime))
            {
                skipped++;
                AddSkippedItem(new GenerateTripsSkippedItemDto(
                    date,
                    route.RouteCode,
                    departureTime,
                    arrivalTime,
                    "Tuyến đã có chuyến cùng giờ.",
                    departureTime.Add(TripScheduleSupport.BoatTurnaroundBuffer)));
                return new TripScheduleAttempt(
                    departureTime,
                    arrivalTime,
                    departureTime.Add(TripScheduleSupport.BoatTurnaroundBuffer));
            }

            var requestedWindow = new TripScheduleSupport.BoatScheduleWindow(
                "(new)",
                departureTime,
                arrivalTime,
                routeStartStationId,
                routeEndStationId,
                routeStops);
            var requestedStationWindow = new TripScheduleSupport.StationDepartureWindow(
                "(new)",
                routeStartStationId,
                routeStops[0].Station?.StationName,
                route.RouteType,
                departureTime);

            var stationConflict = TripScheduleSupport.FindStationDepartureConflict(
                requestedStationWindow,
                stationDepartureSchedule);
            if (stationConflict is not null)
            {
                skippedStationBusy++;
                AddSkippedItem(new GenerateTripsSkippedItemDto(
                    date,
                    route.RouteCode,
                    departureTime,
                    arrivalTime,
                    TripScheduleSupport.BuildStationDepartureConflictMessage(
                        stationConflict.Existing.StationName,
                        stationConflict.Existing.TripCode,
                        stationConflict.Existing.DepartureTime,
                        stationConflict.EarliestAllowedDeparture),
                    stationConflict.EarliestAllowedDeparture,
                    stationConflict.Existing.TripCode,
                    stationConflict.Existing.DepartureTime,
                    ConflictArrivalTime: null));
                return new TripScheduleAttempt(
                    departureTime,
                    arrivalTime,
                    stationConflict.EarliestAllowedDeparture);
            }

            // Tau da ban trong khung gio nay (ke ca chuyen vua sinh trong lo nay) -> bo qua.
            var conflict = TripScheduleSupport.FindConflict(requestedWindow, boatSchedule);
            if (conflict is not null)
            {
                skippedBoatBusy++;
                var earliestAllowedDepartureTime = conflict.Existing.DepartureTime > departureTime
                    ? TripScheduleSupport.ResolveEarliestDepartureAfter(conflict.Existing, requestedWindow)
                    : conflict.EarliestAllowedDeparture;
                AddSkippedItem(new GenerateTripsSkippedItemDto(
                    date,
                    route.RouteCode,
                    departureTime,
                    arrivalTime,
                    TripScheduleSupport.BuildLocationAwareConflictMessage(
                        conflict.Existing.TripCode,
                        conflict.Existing.DepartureTime,
                        conflict.Existing.ArrivalTime,
                        earliestAllowedDepartureTime,
                        conflict.RepositionDuration),
                    earliestAllowedDepartureTime,
                    conflict.Existing.TripCode,
                    conflict.Existing.DepartureTime,
                    conflict.Existing.ArrivalTime));
                return new TripScheduleAttempt(
                    departureTime,
                    arrivalTime,
                    earliestAllowedDepartureTime);
            }

            if (!await OnBoardStaffTripSupport.HasRequiredOnBoardStaffAsync(
                    _context, boat.Id, departureTime, arrivalTime, cancellationToken))
            {
                skippedMissingOnBoardStaff++;
                AddSkippedItem(new GenerateTripsSkippedItemDto(
                    date,
                    route.RouteCode,
                    departureTime,
                    arrivalTime,
                    "Tàu thiếu nhân viên OnBoard trong khung giờ này.",
                    arrivalTime.Add(TripScheduleSupport.BoatTurnaroundBuffer)));
                return new TripScheduleAttempt(
                    departureTime,
                    arrivalTime,
                    arrivalTime.Add(TripScheduleSupport.BoatTurnaroundBuffer));
            }

            var localDeparture = departureTime.ToOffset(VietnamOffset);
            var tripCode = TripCodeSupport.BuildRegularOrSightseeingTripCode(
                route,
                date,
                localDeparture.ToString("HHmm"));

            var trip = new Trip
            {
                RouteId = route.Id,
                BoatId = boat.Id,
                TripCode = tripCode,
                OperatingDate = date,
                DepartureTime = departureTime,
                ArrivalTime = arrivalTime,
                CapacitySnapshot = activeSeats.Count,
                TripStatus = TripStatus.Scheduled
            };

            tripsToAdd.Add(trip);
            TripStopScheduleSupport.CreateTripStops(trip, stopDrafts);
            existingDepartures.Add(departureTime);
            boatSchedule.Add(requestedWindow with { TripCode = tripCode });
            stationDepartureSchedule.Add(requestedStationWindow with { TripCode = tripCode });

            foreach (var seat in activeSeats)
                tripSeatsToAdd.Add(new TripSeat
                {
                    TripId = trip.Id,
                    SeatId = seat.Id,
                    Price = null
                });

            createdCodes.Add(tripCode);
            previewItems.Add(new PreviewTripsScheduleItemDto(
                date,
                route.RouteCode,
                departureTime,
                arrivalTime,
                CanCreate: true,
                TripCode: tripCode));

            return new TripScheduleAttempt(departureTime, arrivalTime, null);
        }

        DateTimeOffset ResolveNextContinuousDeparture(TripScheduleAttempt attempt)
        {
            var requestedGap = TimeSpan.FromMinutes(request.IntervalMinutes!.Value);
            var layover = requestedGap > TripScheduleSupport.BoatTurnaroundBuffer
                ? requestedGap
                : TripScheduleSupport.BoatTurnaroundBuffer;
            var previousWindow = new TripScheduleSupport.BoatScheduleWindow(
                "(continuous)",
                attempt.RequestedDepartureTime,
                attempt.RequestedArrivalTime,
                routeStartStationId,
                routeEndStationId,
                routeStops);
            var nextWindow = new TripScheduleSupport.BoatScheduleWindow(
                "(continuous-next)",
                attempt.RequestedArrivalTime,
                attempt.RequestedArrivalTime,
                routeStartStationId,
                routeEndStationId,
                routeStops);
            var nextDeparture = TripStopScheduleSupport.RoundUpToWholeMinute(
                attempt.RequestedArrivalTime
                    .Add(layover)
                    .Add(TripScheduleSupport.ResolveRepositionDuration(previousWindow, nextWindow)));

            if (attempt.EarliestAllowedDepartureTime.HasValue
                && attempt.EarliestAllowedDepartureTime.Value > nextDeparture)
            {
                return TripStopScheduleSupport.RoundUpToWholeMinute(attempt.EarliestAllowedDepartureTime.Value);
            }

            return nextDeparture;
        }

        for (var date = request.FromDate; date <= request.ToDate; date = date.AddDays(1))
        {
            if (allowedDays is not null && !allowedDays.Contains(date.DayOfWeek))
                continue;

            if (departureTimes is not null)
            {
                foreach (var time in departureTimes)
                {
                    var departureTime = new DateTimeOffset(
                        date.Year, date.Month, date.Day,
                        time.Hour, time.Minute, 0,
                        VietnamOffset).ToUniversalTime();

                    await TryCreateTripAsync(date, departureTime);
                }

                continue;
            }

            var localCursor = new DateTimeOffset(
                date.Year, date.Month, date.Day,
                request.StartTime!.Value.Hour, request.StartTime.Value.Minute, 0,
                VietnamOffset);
            var localEnd = new DateTimeOffset(
                date.Year, date.Month, date.Day,
                request.EndTime!.Value.Hour, request.EndTime.Value.Minute, 0,
                VietnamOffset);
            while (localCursor <= localEnd)
            {
                var departureTime = localCursor.ToUniversalTime();
                var attempt = await TryCreateTripAsync(date, departureTime);
                localCursor = ResolveNextContinuousDeparture(attempt).ToOffset(VietnamOffset);
            }
        }

        return new GenerateTripsSchedulePlan(
            tripsToAdd,
            tripSeatsToAdd,
            skipped,
            skippedBoatBusy,
            skippedPast,
            skippedMissingOnBoardStaff,
            skippedStationBusy,
            createdCodes,
            skippedItems,
            previewItems);
    }

    private static IReadOnlyList<TimeOnly>? ResolveDepartureTimes(IGenerateTripsScheduleRequest request)
    {
        if (request.DepartureTimes is { Count: > 0 })
        {
            return request.DepartureTimes
                .Distinct()
                .Order()
                .ToList();
        }

        return null;
    }
}
