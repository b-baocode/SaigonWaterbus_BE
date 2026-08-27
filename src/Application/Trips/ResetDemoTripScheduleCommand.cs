using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Trips;

/// <summary>
/// Kịch bản của một tàu trong lần reset demo. Mỗi giờ tạo một trip riêng.
/// </summary>
public sealed record DemoTripScheduleItem(
    string BoatCode,
    string RouteCode,
    IReadOnlyList<TimeOnly> DepartureTimes,
    IReadOnlyList<CreateTripStopScheduleInput>? Stops = null);

/// <summary>
/// Tự sinh các lượt theo khung giờ. Tàu được chia vòng theo BoatCodes:
/// lượt 1 dùng tàu 1, lượt 2 dùng tàu 2, ... rồi quay lại tàu 1.
/// </summary>
public sealed record DemoTripSchedulePlan(
    string RouteCode,
    IReadOnlyList<string> BoatCodes,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int IntervalMinutes,
    IReadOnlyList<CreateTripStopScheduleInput>? Stops = null);

/// <summary>
/// Reset trip vận hành (Regular/Sightseeing) của một ngày rồi tạo lại theo nhiều tàu.
/// Trip Charter không thuộc phạm vi reset để không làm hỏng charter booking.
/// </summary>
[Authorize(Roles = "Admin")]
public sealed record ResetDemoTripScheduleCommand(
    DateOnly OperatingDate,
    bool ConfirmReset,
    IReadOnlyList<DemoTripScheduleItem>? Trips = null,
    IReadOnlyList<DemoTripSchedulePlan>? Plans = null) : IRequest<ResetDemoTripScheduleResult>;

public sealed record ResetDemoTripScheduleResult(
    DateOnly OperatingDate,
    int DeletedTripCount,
    int CreatedTripCount,
    IReadOnlyList<TripDetailDto> CreatedTrips);

public sealed class ResetDemoTripScheduleCommandValidator
    : AbstractValidator<ResetDemoTripScheduleCommand>
{
    public ResetDemoTripScheduleCommandValidator()
    {
        RuleFor(x => x.ConfirmReset)
            .Equal(true)
            .WithMessage("confirmReset phải là true để tránh xóa nhầm dữ liệu demo.");
        RuleFor(x => x)
            .Must(x => (x.Trips?.Count ?? 0) > 0 || (x.Plans?.Count ?? 0) > 0)
            .WithMessage("Cần gửi ít nhất một item trong trips hoặc plans.");
        RuleForEach(x => x.Trips).ChildRules(item =>
        {
            item.RuleFor(x => x.BoatCode).NotEmpty().MaximumLength(50);
            item.RuleFor(x => x.RouteCode).NotEmpty().MaximumLength(50);
            item.RuleFor(x => x.DepartureTimes).NotEmpty();
            item.RuleFor(x => x.DepartureTimes)
                .Must(times => times.Distinct().Count() == times.Count)
                .WithMessage("departureTimes của một tàu không được trùng nhau.");
        });
        RuleForEach(x => x.Plans).ChildRules(plan =>
        {
            plan.RuleFor(x => x.RouteCode).NotEmpty().MaximumLength(50);
            plan.RuleFor(x => x.BoatCodes).NotEmpty();
            plan.RuleFor(x => x.BoatCodes)
                .Must(codes => codes.Distinct(StringComparer.OrdinalIgnoreCase).Count() == codes.Count)
                .WithMessage("boatCodes không được trùng nhau trong cùng một plan.");
            plan.RuleForEach(x => x.BoatCodes).NotEmpty().MaximumLength(50);
            plan.RuleFor(x => x.EndTime)
                .GreaterThan(x => x.StartTime)
                .WithMessage("endTime phải sau startTime.");
            plan.RuleFor(x => x.IntervalMinutes)
                .GreaterThanOrEqualTo(TripScheduleSupport.StationDepartureBuffer.Minutes)
                .WithMessage("intervalMinutes phải tối thiểu 5 phút.");
        });
    }
}

public sealed class ResetDemoTripScheduleCommandHandler
    : IRequestHandler<ResetDemoTripScheduleCommand, ResetDemoTripScheduleResult>
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private readonly IApplicationDbContext _context;
    private readonly ISender _sender;

    public ResetDemoTripScheduleCommandHandler(IApplicationDbContext context, ISender sender)
    {
        _context = context;
        _sender = sender;
    }

    public Task<ResetDemoTripScheduleResult> Handle(
        ResetDemoTripScheduleCommand request,
        CancellationToken cancellationToken) =>
        _context.ExecuteInTransactionAsync(async ct =>
        {
            // Không xóa trip sinh từ charter. Các FK booking/ticket liên quan trip thường
            // được database set null/cascade theo cấu hình hiện có khi trip bị xóa.
            var deletedTripCount = await _context.Set<Trip>()
                .Where(x => x.OperatingDate == request.OperatingDate
                    && x.TripType != TripTypes.Charter
                    && !x.SourceBookingId.HasValue)
                .ExecuteDeleteAsync(ct);

            var departures = BuildPlannedDepartures(request)
                .OrderBy(x => x.Time)
                .ThenBy(x => x.Item.BoatCode, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var duplicateBoatSlots = departures
                .GroupBy(x => new { BoatCode = x.Item.BoatCode.Trim().ToUpperInvariant(), x.Time })
                .FirstOrDefault(x => x.Count() > 1);
            if (duplicateBoatSlots is not null)
            {
                throw new ValidationException([new ValidationFailure(nameof(request.Trips),
                    $"Tàu {duplicateBoatSlots.Key.BoatCode} bị trùng giờ {duplicateBoatSlots.Key.Time:HH\\:mm} trong script demo.")]);
            }

            var createdTrips = new List<TripDetailDto>(departures.Length);
            foreach (var departure in departures)
            {
                var localDeparture = request.OperatingDate.ToDateTime(departure.Time);
                createdTrips.Add(await _sender.Send(new CreateTripCommand(
                    departure.Item.RouteCode,
                    departure.Item.BoatCode,
                    request.OperatingDate,
                    new DateTimeOffset(localDeparture, VietnamOffset),
                    Stops: departure.Item.Stops), ct));
            }

            return new ResetDemoTripScheduleResult(
                request.OperatingDate,
                deletedTripCount,
                createdTrips.Count,
                createdTrips);
        }, cancellationToken);

    private sealed record PlannedDemoDeparture(DemoTripScheduleItem Item, TimeOnly Time);

    private static IEnumerable<PlannedDemoDeparture> BuildPlannedDepartures(
        ResetDemoTripScheduleCommand request)
    {
        foreach (var item in request.Trips ?? [])
        {
            foreach (var time in item.DepartureTimes)
            {
                yield return new PlannedDemoDeparture(item, time);
            }
        }

        foreach (var plan in request.Plans ?? [])
        {
            var slotIndex = 0;
            var start = DateTime.UnixEpoch.Date.Add(plan.StartTime.ToTimeSpan());
            var end = DateTime.UnixEpoch.Date.Add(plan.EndTime.ToTimeSpan());
            for (var slot = start; slot <= end; slot = slot.AddMinutes(plan.IntervalMinutes))
            {
                var time = TimeOnly.FromDateTime(slot);
                yield return new PlannedDemoDeparture(
                    new DemoTripScheduleItem(
                        plan.BoatCodes[slotIndex % plan.BoatCodes.Count],
                        plan.RouteCode,
                        [time],
                        plan.Stops),
                    time);
                slotIndex++;
            }
        }
    }
}
