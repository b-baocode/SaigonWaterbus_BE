using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.TripSchedules;

// Kết quả mỗi lần chạy job
public sealed record GenerateScheduledTripsResult(int TripsCreated, int SchedulesProcessed, List<string> Errors);

// Command này được gọi bởi:
// 1. TripGenerationBackgroundService mỗi ngày 00:05
// 2. Admin trigger thủ công qua POST /api/trip-schedules/generate-now
public sealed record GenerateScheduledTripsCommand(
    DateOnly? ForDate = null  // null = dùng ngày hôm nay làm điểm bắt đầu
) : IRequest<GenerateScheduledTripsResult>;

public sealed class GenerateScheduledTripsCommandHandler
    : IRequestHandler<GenerateScheduledTripsCommand, GenerateScheduledTripsResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<GenerateScheduledTripsCommandHandler> _logger;

    public GenerateScheduledTripsCommandHandler(
        IApplicationDbContext context,
        ILogger<GenerateScheduledTripsCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<GenerateScheduledTripsResult> Handle(
        GenerateScheduledTripsCommand request,
        CancellationToken cancellationToken)
    {
        var today = request.ForDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)); // UTC+7

        var schedules = await _context.Set<TripSchedule>()
            .Include(s => s.Route)
                .ThenInclude(r => r.RouteStops.OrderBy(rs => rs.StopOrder))
                    .ThenInclude(rs => rs.Station)
            .Include(s => s.Vessel)
                .ThenInclude(v => v.Seats.Where(seat => seat.IsActive))
                    .ThenInclude(seat => seat.SeatType)
            .Where(s => s.IsActive
                && s.ValidFrom <= today
                && (s.ValidTo == null || s.ValidTo >= today))
            .ToListAsync(cancellationToken);

        var tripsCreated = 0;
        var errors = new List<string>();

        foreach (var schedule in schedules)
        {
            try
            {
                var created = await ProcessScheduleAsync(schedule, today, cancellationToken);
                tripsCreated += created;
            }
            catch (Exception ex)
            {
                var msg = $"Schedule {schedule.Id} ({schedule.Route.RouteCode} {schedule.DepartureTime}): {ex.Message}";
                _logger.LogError(ex, "Failed to process schedule {ScheduleId}", schedule.Id);
                errors.Add(msg);
            }
        }

        return new GenerateScheduledTripsResult(tripsCreated, schedules.Count, errors);
    }

    private async Task<int> ProcessScheduleAsync(
        TripSchedule schedule,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        // Lấy danh sách ngày đã sinh của schedule này
        var horizon = today.AddDays(schedule.HorizonDays);
        var existingDates = await _context.Set<GeneratedTrip>()
            .Where(g => g.TripScheduleId == schedule.Id
                && g.OperatingDate >= today
                && g.OperatingDate <= horizon)
            .Select(g => g.OperatingDate)
            .ToHashSetAsync(cancellationToken);

        var targetDates = ComputeTargetDates(schedule, today, horizon);

        var created = 0;
        foreach (var date in targetDates)
        {
            if (existingDates.Contains(date))
                continue;

            await CreateTripForDateAsync(schedule, date, cancellationToken);
            created++;
        }

        return created;
    }

    private async Task CreateTripForDateAsync(
        TripSchedule schedule,
        DateOnly operatingDate,
        CancellationToken cancellationToken)
    {
        // Validate vessel seats có SeatType chưa
        var seatsWithoutType = schedule.Vessel.Seats
            .Where(s => s.IsActive && s.SeatTypeId == null)
            .ToList();

        if (seatsWithoutType.Count > 0)
            throw new ValidationException([new ValidationFailure("Vessel",
                $"Tàu '{schedule.Vessel.Name}' có {seatsWithoutType.Count} ghế chưa gán SeatType: " +
                string.Join(", ", seatsWithoutType.Select(s => s.Code)))]);

        if (schedule.Route.RouteStops.Count < 2)
            throw new ValidationException([new ValidationFailure("Route",
                $"Tuyến '{schedule.Route.RouteCode}' phải có ít nhất 2 trạm dừng.")]);

        // Xây dựng DepartureTime từ ngày + giờ (UTC+7)
        var localDate = operatingDate.ToDateTime(schedule.DepartureTime, DateTimeKind.Unspecified);
        var departureTime = new DateTimeOffset(localDate, TimeSpan.FromHours(7));

        var tripCode = $"TR-{operatingDate:yyyyMMdd}-{schedule.Route.RouteCode}-{Random.Shared.Next(1000, 9999)}";

        // Tính TripStops
        var tripStops = BuildTripStops(schedule.Route.RouteStops.OrderBy(rs => rs.StopOrder).ToList(), departureTime);
        var arrivalTime = tripStops.Max(ts => ts.ScheduledArrival ?? departureTime);

        await _context.ExecuteInTransactionAsync(async ct =>
        {
            var trip = new Trip
            {
                RouteId = schedule.RouteId,
                VesselId = schedule.VesselId,
                TripCode = tripCode,
                OperatingDate = operatingDate,
                DepartureTime = departureTime,
                ArrivalTime = arrivalTime,
                CapacitySnapshot = schedule.Vessel.Seats.Count(s => s.IsActive),
                TripStatus = TripStatus.Scheduled
            };

            foreach (var ts in tripStops)
                ts.Trip = trip;

            trip.TripStops = tripStops;

            _context.Set<Trip>().Add(trip);

            // Tạo TripSeat instance cho từng ghế active của tàu
            var tripSeats = schedule.Vessel.Seats
                .Where(s => s.IsActive)
                .Select(seat => new TripSeat
                {
                    TripId = trip.Id,
                    SeatId = seat.Id,
                    Trip = trip
                }).ToList();

            _context.Set<TripSeat>().AddRange(tripSeats);

            // Ghi nhận đã sinh trip cho ngày này
            _context.Set<GeneratedTrip>().Add(new GeneratedTrip
            {
                TripScheduleId = schedule.Id,
                TripId = trip.Id,
                OperatingDate = operatingDate,
                Trip = trip
            });

            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Generated trip {TripCode} for schedule {ScheduleId} on {Date} with {SeatCount} seats",
                tripCode, schedule.Id, operatingDate, tripSeats.Count);

        }, cancellationToken);
    }

    private static List<TripStop> BuildTripStops(
        IReadOnlyList<RouteStop> orderedStops,
        DateTimeOffset departureTime)
    {
        var tripStops = new List<TripStop>();
        var currentTime = departureTime;

        foreach (var routeStop in orderedStops)
        {
            var scheduledArrival = routeStop.StopOrder == orderedStops.Min(rs => rs.StopOrder)
                ? departureTime
                : currentTime;

            var scheduledDeparture = scheduledArrival.AddMinutes(routeStop.StandardDwellMin ?? 2);

            tripStops.Add(new TripStop
            {
                RouteStopId = routeStop.Id,
                StopOrder = routeStop.StopOrder,
                ScheduledArrival = scheduledArrival,
                ScheduledDeparture = scheduledDeparture,
                StopStatus = "Scheduled"
            });

            currentTime = scheduledDeparture.AddMinutes(routeStop.StandardTravelMin ?? 15);
        }

        return tripStops;
    }

    // Tính danh sách ngày cần sinh trip theo pattern của lịch
    internal static IEnumerable<DateOnly> ComputeTargetDates(
        TripSchedule schedule,
        DateOnly from,
        DateOnly to)
    {
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (!IsScheduledOnDate(schedule, date))
                continue;

            // Chỉ sinh nếu ngày nằm trong ValidFrom..ValidTo
            if (date < schedule.ValidFrom)
                continue;
            if (schedule.ValidTo.HasValue && date > schedule.ValidTo.Value)
                break;

            yield return date;
        }
    }

    private static bool IsScheduledOnDate(TripSchedule schedule, DateOnly date) =>
        schedule.RecurrenceType switch
        {
            RecurrenceType.Daily => true,
            RecurrenceType.Weekly => IsWeeklyMatch(schedule.WeeklyDays, date.DayOfWeek),
            RecurrenceType.Monthly => schedule.MonthlyDay.HasValue && date.Day == schedule.MonthlyDay.Value,
            _ => false
        };

    private static bool IsWeeklyMatch(string? weeklyDays, DayOfWeek dayOfWeek)
    {
        if (string.IsNullOrWhiteSpace(weeklyDays))
            return false;

        // DayOfWeek: Sunday=0, Monday=1, ..., Saturday=6
        var targetDay = (int)dayOfWeek;
        return weeklyDays.Split(',')
            .Select(d => d.Trim())
            .Any(d => int.TryParse(d, out var v) && v == targetDay);
    }
}
