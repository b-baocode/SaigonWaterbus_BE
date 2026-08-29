using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Trips;

public sealed record DailyTripBatchItem(
    string RouteCode,
    string BoatCode,
    IReadOnlyList<TimeOnly> DepartureTimes,
    IReadOnlyList<CreateTripStopScheduleInput>? Stops = null,
    int? StayDurationMinutes = null);

public sealed record DailyTripBatchPlan(
    string RouteCode,
    IReadOnlyList<string> BoatCodes,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int IntervalMinutes,
    IReadOnlyList<CreateTripStopScheduleInput>? Stops = null,
    int? StayDurationMinutes = null);

[Authorize(Roles = "Admin")]
public sealed record CreateDailyTripBatchCommand(
    DateOnly OperatingDate,
    bool ConfirmCreate,
    IReadOnlyList<DailyTripBatchItem>? Items = null,
    IReadOnlyList<DailyTripBatchPlan>? Plans = null) : IRequest<CreateDailyTripBatchResult>;

public sealed record DailyTripBatchItemResult(
    string RouteCode,
    string BoatCode,
    int Created,
    IReadOnlyList<string> CreatedTripCodes);

public sealed record CreateDailyTripBatchResult(
    DateOnly OperatingDate,
    int Created,
    IReadOnlyList<DailyTripBatchItemResult> Items);

public sealed class CreateDailyTripBatchCommandValidator
    : AbstractValidator<CreateDailyTripBatchCommand>
{
    public CreateDailyTripBatchCommandValidator()
    {
        RuleFor(x => x.OperatingDate).NotEmpty();
        RuleFor(x => x.ConfirmCreate)
            .Equal(true)
            .WithMessage("confirmCreate phải là true để xác nhận tạo lịch hàng loạt.");
        RuleFor(x => x)
            .Must(x => x.Items is { Count: > 0 } || x.Plans is { Count: > 0 })
            .WithMessage("Phải gửi ít nhất một items hoặc plans.")
            .OverridePropertyName(nameof(CreateDailyTripBatchCommand.Items));
        RuleFor(x => x)
            .Must(HaveUniqueBoatDepartureSlots)
            .WithMessage("Một tàu không được có hai trip cùng giờ trong batch.")
            .OverridePropertyName(nameof(CreateDailyTripBatchCommand.Items));

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.RouteCode).NotEmpty().MaximumLength(50);
            item.RuleFor(x => x.BoatCode).NotEmpty().MaximumLength(50);
            item.RuleFor(x => x.DepartureTimes)
                .Cascade(CascadeMode.Stop)
                .NotNull()
                .NotEmpty()
                .Must(times => times is not null && times.Distinct().Count() == times.Count)
                .WithMessage("departureTimes trong một item không được trùng nhau.");
            item.RuleFor(x => x.Stops!)
                .Must(stops => stops.Select(x => x.StopOrder).Distinct().Count() == stops.Count)
                .WithMessage("stops không được trùng stopOrder.")
                .When(x => x.Stops is not null);
            item.RuleForEach(x => x.Stops).ChildRules(stop =>
            {
                stop.RuleFor(x => x.StopOrder).GreaterThan(0);
                stop.RuleFor(x => x.StayDurationMinutes).InclusiveBetween(0, 24 * 60);
            });
            item.RuleFor(x => x.StayDurationMinutes)
                .InclusiveBetween(0, 24 * 60)
                .When(x => x.StayDurationMinutes.HasValue);
            item.RuleFor(x => x)
                .Must(x => !x.StayDurationMinutes.HasValue || x.Stops is not { Count: > 0 })
                .WithMessage("Chỉ gửi stayDurationMinutes hoặc stops, không gửi đồng thời cả hai.")
                .OverridePropertyName(nameof(DailyTripBatchItem.Stops));
        });

        RuleForEach(x => x.Plans).ChildRules(plan =>
        {
            plan.RuleFor(x => x.RouteCode).NotEmpty().MaximumLength(50);
            plan.RuleFor(x => x.BoatCodes)
                .Cascade(CascadeMode.Stop)
                .NotNull()
                .NotEmpty()
                .Must(codes => codes is not null
                    && codes.Select(NormalizeCode).Distinct().Count() == codes.Count)
                .WithMessage("boatCodes trong một plan không được trùng nhau.");
            plan.RuleForEach(x => x.BoatCodes)
                .NotEmpty()
                .MaximumLength(50);
            plan.RuleFor(x => x.EndTime)
                .GreaterThanOrEqualTo(x => x.StartTime)
                .WithMessage("endTime phải lớn hơn hoặc bằng startTime.");
            plan.RuleFor(x => x.IntervalMinutes)
                .InclusiveBetween(5, 24 * 60)
                .WithMessage("intervalMinutes phải từ 5 đến 1440 phút.");
            plan.RuleFor(x => x.Stops!)
                .Must(stops => stops.Select(x => x.StopOrder).Distinct().Count() == stops.Count)
                .WithMessage("stops không được trùng stopOrder.")
                .When(x => x.Stops is not null);
            plan.RuleForEach(x => x.Stops).ChildRules(stop =>
            {
                stop.RuleFor(x => x.StopOrder).GreaterThan(0);
                stop.RuleFor(x => x.StayDurationMinutes).InclusiveBetween(0, 24 * 60);
            });
            plan.RuleFor(x => x.StayDurationMinutes)
                .InclusiveBetween(0, 24 * 60)
                .When(x => x.StayDurationMinutes.HasValue);
            plan.RuleFor(x => x)
                .Must(x => !x.StayDurationMinutes.HasValue || x.Stops is not { Count: > 0 })
                .WithMessage("Chỉ gửi stayDurationMinutes hoặc stops, không gửi đồng thời cả hai.")
                .OverridePropertyName(nameof(DailyTripBatchPlan.Stops));
        });
    }

    private static bool HaveUniqueBoatDepartureSlots(CreateDailyTripBatchCommand command) =>
        ExpandSlots(command)
            .GroupBy(x => (x.BoatCode, x.Time))
            .All(x => x.Count() == 1);

    private static IEnumerable<(string BoatCode, TimeOnly Time)> ExpandSlots(
        CreateDailyTripBatchCommand command)
    {
        foreach (var item in command.Items ?? [])
        {
            foreach (var time in item.DepartureTimes ?? [])
            {
                yield return (NormalizeCode(item.BoatCode), time);
            }
        }

        foreach (var plan in command.Plans ?? [])
        {
            if (plan.BoatCodes is not { Count: > 0 }
                || plan.IntervalMinutes < 5
                || plan.EndTime < plan.StartTime)
            {
                continue;
            }

            var slotIndex = 0;
            foreach (var time in EnumerateDepartureTimes(plan))
            {
                yield return (NormalizeCode(plan.BoatCodes[slotIndex % plan.BoatCodes.Count]), time);
                slotIndex++;
            }
        }
    }

    private static IEnumerable<TimeOnly> EnumerateDepartureTimes(DailyTripBatchPlan plan)
    {
        for (var cursor = plan.StartTime.ToTimeSpan();
             cursor <= plan.EndTime.ToTimeSpan();
             cursor = cursor.Add(TimeSpan.FromMinutes(plan.IntervalMinutes)))
        {
            yield return TimeOnly.FromTimeSpan(cursor);
        }
    }

    private static string NormalizeCode(string? code) =>
        code?.Trim().ToUpperInvariant() ?? string.Empty;
}

public sealed class CreateDailyTripBatchCommandHandler
    : IRequestHandler<CreateDailyTripBatchCommand, CreateDailyTripBatchResult>
{
    private readonly IApplicationDbContext _context;

    public CreateDailyTripBatchCommandHandler(IApplicationDbContext context) => _context = context;

    public Task<CreateDailyTripBatchResult> Handle(
        CreateDailyTripBatchCommand request,
        CancellationToken cancellationToken) =>
        _context.ExecuteInTransactionAsync(async ct =>
        {
            var items = ExpandItems(request);
            var itemResults = new List<DailyTripBatchItemResult>(items.Count);
            var generateHandler = new GenerateTripsCommandHandler(_context);
            var routesByCode = new Dictionary<string, SaigonWaterbus.Domain.Entities.Route?>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                var requestedTimes = item.DepartureTimes.Distinct().OrderBy(x => x).ToArray();
                var stops = await ResolveStopsAsync(item, routesByCode, ct);
                var result = await generateHandler.Handle(
                    new GenerateTripsCommand(
                        item.RouteCode,
                        item.BoatCode,
                        requestedTimes,
                        request.OperatingDate,
                        request.OperatingDate,
                        Stops: stops),
                    ct);

                if (result.Created != requestedTimes.Length)
                {
                    var failures = (result.SkippedItems ?? [])
                        .Select((x, index) => new ValidationFailure(
                            $"items[{itemResults.Count}].departureTimes[{index}]",
                            x.Reason))
                        .ToList();
                    if (failures.Count == 0)
                    {
                        failures.Add(new ValidationFailure(
                            $"items[{itemResults.Count}]",
                            $"Chỉ tạo được {result.Created}/{requestedTimes.Length} trip."));
                    }

                    throw new ValidationException(failures);
                }

                itemResults.Add(new DailyTripBatchItemResult(
                    item.RouteCode.Trim().ToUpperInvariant(),
                    item.BoatCode.Trim().ToUpperInvariant(),
                    result.Created,
                    result.CreatedTripCodes));
            }

            return new CreateDailyTripBatchResult(
                request.OperatingDate,
                itemResults.Sum(x => x.Created),
                itemResults);
        }, cancellationToken);

    private static List<DailyTripBatchItem> ExpandItems(CreateDailyTripBatchCommand request)
    {
        var items = new List<DailyTripBatchItem>(request.Items ?? []);

        foreach (var plan in request.Plans ?? [])
        {
            var departuresByBoat = new Dictionary<string, List<TimeOnly>>(StringComparer.OrdinalIgnoreCase);
            var slotIndex = 0;

            foreach (var time in EnumerateDepartureTimes(plan))
            {
                var boatCode = plan.BoatCodes[slotIndex % plan.BoatCodes.Count];
                if (!departuresByBoat.TryGetValue(boatCode, out var departures))
                {
                    departures = [];
                    departuresByBoat.Add(boatCode, departures);
                }

                departures.Add(time);
                slotIndex++;
            }

            items.AddRange(departuresByBoat.Select(x => new DailyTripBatchItem(
                plan.RouteCode,
                x.Key,
                x.Value,
                plan.Stops,
                plan.StayDurationMinutes)));
        }

        return items;
    }

    private async Task<IReadOnlyList<CreateTripStopScheduleInput>?> ResolveStopsAsync(
        DailyTripBatchItem item,
        Dictionary<string, SaigonWaterbus.Domain.Entities.Route?> routesByCode,
        CancellationToken cancellationToken)
    {
        var routeCode = item.RouteCode.Trim().ToUpperInvariant();
        if (!routesByCode.TryGetValue(routeCode, out var route))
        {
            route = await _context.Set<SaigonWaterbus.Domain.Entities.Route>()
                .Include(x => x.RouteStops)
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.RouteCode == routeCode, cancellationToken);
            routesByCode[routeCode] = route;
        }

        if (route is null)
        {
            return item.Stops;
        }

        if (route.RouteType == RouteTypes.SightseeingLoop)
        {
            if (item.StayDurationMinutes.HasValue || item.Stops is { Count: > 0 })
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(item.Stops),
                    "Sightseeing không có điểm dừng; chỉ cấu hình khoảng cách giữa các trip bằng intervalMinutes.")]);
            }

            return null;
        }

        if (!item.StayDurationMinutes.HasValue)
        {
            return item.Stops;
        }

        var stopOrders = route.RouteStops
            .OrderBy(x => x.StopOrder)
            .Select(x => x.StopOrder)
            .ToList();
        var stops = stopOrders.Count <= 2
            ? []
            : stopOrders
                .Skip(1)
                .SkipLast(1)
                .Select(stopOrder => new CreateTripStopScheduleInput(
                    stopOrder,
                    item.StayDurationMinutes.Value))
                .ToArray();

        return stops;
    }

    private static IEnumerable<TimeOnly> EnumerateDepartureTimes(DailyTripBatchPlan plan)
    {
        for (var cursor = plan.StartTime.ToTimeSpan();
             cursor <= plan.EndTime.ToTimeSpan();
             cursor = cursor.Add(TimeSpan.FromMinutes(plan.IntervalMinutes)))
        {
            yield return TimeOnly.FromTimeSpan(cursor);
        }
    }
}
