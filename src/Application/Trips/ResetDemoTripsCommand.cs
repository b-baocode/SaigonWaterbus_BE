using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Trips;

[Authorize(Roles = "Admin")]
public sealed record ResetDemoTripsCommand(
    string RouteCode,
    string BoatCode,
    DateOnly OperatingDate,
    IReadOnlyList<TimeOnly> DepartureTimes,
    IReadOnlyList<CreateTripStopScheduleInput>? Stops = null) : IRequest<ResetDemoTripsResult>;

public sealed record ResetDemoTripsResult(
    int Deleted,
    int Created,
    int Skipped,
    IReadOnlyList<TripDetailDto> Trips,
    IReadOnlyList<KeptActiveTripDto> KeptActiveTrips);

public sealed record KeptActiveTripDto(
    Guid TripId,
    string TripCode,
    DateTimeOffset DepartureTime,
    DateTimeOffset ArrivalTime,
    string Status,
    string Reason);

public sealed class ResetDemoTripsCommandValidator : AbstractValidator<ResetDemoTripsCommand>
{
    public ResetDemoTripsCommandValidator()
    {
        RuleFor(x => x.RouteCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.BoatCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DepartureTimes)
            .NotEmpty()
            .WithMessage("departureTimes phải có ít nhất một giờ khởi hành.");
        RuleFor(x => x.OperatingDate).NotEmpty();
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

public sealed class ResetDemoTripsCommandHandler : IRequestHandler<ResetDemoTripsCommand, ResetDemoTripsResult>
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private readonly IApplicationDbContext _context;
    private readonly IMediator _mediator;
    private readonly ITripsResetRealtimeNotifier _tripsResetRealtimeNotifier;

    public ResetDemoTripsCommandHandler(
        IApplicationDbContext context,
        IMediator mediator,
        ITripsResetRealtimeNotifier? tripsResetRealtimeNotifier = null)
    {
        _context = context;
        _mediator = mediator;
        _tripsResetRealtimeNotifier = tripsResetRealtimeNotifier ?? NullTripsResetRealtimeNotifier.Instance;
    }

    public async Task<ResetDemoTripsResult> Handle(
        ResetDemoTripsCommand request,
        CancellationToken cancellationToken)
    {
        var routeCode = request.RouteCode.Trim().ToUpperInvariant();
        var boatCode = request.BoatCode.Trim().ToUpperInvariant();

        var route = await _context.Set<Route>()
            .Include(r => r.RouteStops.OrderBy(rs => rs.StopOrder))
                .ThenInclude(rs => rs.Station)
            .SingleOrDefaultAsync(r => r.RouteCode == routeCode && r.Status == "Active", cancellationToken)
            ?? throw new NotFoundException($"Route '{routeCode}' not found or inactive.");

        var boat = await _context.Set<Boat>()
            .SingleOrDefaultAsync(x => x.Code == boatCode, cancellationToken)
            ?? throw new NotFoundException($"Boat '{boatCode}' not found.");

        var routeStopsOrdered = route.RouteStops.OrderBy(x => x.StopOrder).ToList();
        var routeStartStation = routeStopsOrdered.FirstOrDefault();
        var routeEndStation = routeStopsOrdered.LastOrDefault();

        // Xoá TẤT CẢ trip của route+boat trong ngày đúng, bất kể status.
        var candidates = await _context.Set<Trip>()
            .Include(t => t.Route).ThenInclude(r => r.RouteStops).ThenInclude(rs => rs.Station)
            .Where(t => t.RouteId == route.Id
                     && t.BoatId == boat.Id
                     && t.OperatingDate == request.OperatingDate)
            .ToListAsync(cancellationToken);

        // Xoá toàn bộ, không giữ lại trip nào.
        var removedEvents = candidates.Select(trip =>
        {
            var orderedStops = trip.Route.RouteStops.OrderBy(x => x.StopOrder).ToList();
            var endStation = orderedStops.LastOrDefault()?.Station;
            return new TripResetRemovedRealtimeEvent(
                trip.Id,
                trip.TripCode,
                trip.DepartureTime,
                trip.ArrivalTime,
                endStation?.StationCode,
                endStation?.StationName);
        }).ToList();

        var deleted = 0;
        if (candidates.Count > 0)
        {
            var oldTripIds = candidates.Select(t => t.Id).ToList();

            var orphanSeats = await _context.Set<TripSeat>()
                .Where(ts => oldTripIds.Contains(ts.TripId))
                .ToListAsync(cancellationToken);
            if (orphanSeats.Count > 0)
                _context.Set<TripSeat>().RemoveRange(orphanSeats);

            var tripStops = await _context.Set<TripStop>()
                .Where(ts => oldTripIds.Contains(ts.TripId))
                .ToListAsync(cancellationToken);
            if (tripStops.Count > 0)
                _context.Set<TripStop>().RemoveRange(tripStops);

            _context.Set<Trip>().RemoveRange(candidates);
            await _context.SaveChangesAsync(cancellationToken);
            deleted = candidates.Count;
        }

        // Tạo lại theo từng departureTime — chạy qua handler CreateTripCommand có sẵn để tái sử dụng toàn bộ logic validate.
        var created = new List<TripDetailDto>();
        var addedEvents = new List<TripResetAddedRealtimeEvent>();
        var skipped = 0;
        var distinctTimes = request.DepartureTimes.Distinct().OrderBy(x => x).ToList();
        foreach (var time in distinctTimes)
        {
            var localDeparture = request.OperatingDate.ToDateTime(time);
            var departureAt = new DateTimeOffset(localDeparture, VietnamOffset);

            try
            {
                var trip = await _mediator.Send(new CreateTripCommand(
                    routeCode,
                    boatCode,
                    request.OperatingDate,
                    departureAt,
                    Stops: request.Stops), cancellationToken);
                created.Add(trip);

                addedEvents.Add(new TripResetAddedRealtimeEvent(
                    trip.TripId,
                    trip.TripCode,
                    route.RouteCode,
                    departureAt,
                    trip.ArrivalTime,
                    routeStartStation?.Station?.StationCode,
                    routeStartStation?.Station?.StationName,
                    routeEndStation?.Station?.StationCode,
                    routeEndStation?.Station?.StationName));
            }
            catch (ValidationException)
            {
                // Bỏ qua chuyến không tạo được (ví dụ đè lịch tàu) nhưng vẫn tiếp tục các chuyến còn lại.
                skipped++;
            }
        }

        // Bắn realtime để GPS / dashboard đồng bộ lịch tàu ngay lập tức.
        await _tripsResetRealtimeNotifier.PublishResetAsync(
            new TripsResetRealtimeEvent(
                boat.Id,
                boat.Code,
                request.OperatingDate,
                removedEvents,
                addedEvents,
                []),
            cancellationToken);

        return new ResetDemoTripsResult(deleted, created.Count, skipped, created, []);
    }
}