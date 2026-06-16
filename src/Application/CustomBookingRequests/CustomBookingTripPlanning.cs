using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.CustomBookingRequests;

public interface ICustomBookingTripRequest
{
    int RequestedNumberOfDecks { get; }
    SeatSetupType RequestedSeatSetupType { get; }
    DateOnly DepartureDate { get; }
    TimeOnly? PreferredStartTime { get; }
    Guid FromStationId { get; }
    Guid ToStationId { get; }
    int AdultCount { get; }
    int ChildCount { get; }
    string? SpecialRequests { get; }
    IReadOnlyCollection<CreateCustomBookingItineraryStopRequest>? ItineraryStops { get; }
}

internal sealed class CustomBookingTripRequestValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : ICustomBookingTripRequest
{
    public CustomBookingTripRequestValidator()
    {
        RuleFor(x => x.RequestedNumberOfDecks)
            .InclusiveBetween(1, 10)
            .WithMessage("Số tầng tàu yêu cầu phải từ 1 đến 10.");

        RuleFor(x => x.RequestedSeatSetupType)
            .IsInEnum()
            .WithMessage("Kiểu ghế yêu cầu chỉ nhận FullStandard hoặc StandardAndVip.");

        RuleFor(x => x.DepartureDate)
            .Must(x => x != default)
            .WithMessage("Ngày đi là bắt buộc.");

        RuleFor(x => x.PreferredStartTime)
            .NotNull()
            .WithMessage("Giờ bắt đầu là bắt buộc.");

        RuleFor(x => x.FromStationId)
            .NotEmpty()
            .WithMessage("Bến bắt đầu là bắt buộc.");

        RuleFor(x => x.ToStationId)
            .NotEmpty()
            .WithMessage("Bến kết thúc là bắt buộc.");

        RuleFor(x => x.AdultCount)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Số người lớn phải lớn hơn hoặc bằng 1.");

        RuleFor(x => x.ChildCount)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Số trẻ em không được âm.");

        RuleFor(x => x)
            .Must(x => x.AdultCount + x.ChildCount <= 500)
            .WithMessage("Tổng số khách không được vượt quá 500.")
            .OverridePropertyName(nameof(ICustomBookingTripRequest.AdultCount));

        RuleFor(x => x.SpecialRequests)
            .MaximumLength(1000)
            .WithMessage("Yêu cầu đặc biệt không được vượt quá 1000 ký tự.")
            .When(x => x.SpecialRequests is not null);

        RuleFor(x => x.ItineraryStops)
            .Must(x => x is null || x.Count <= 20)
            .WithMessage("Lịch trình không được vượt quá 20 điểm ghé.")
            .Must(HaveUniqueStopOrders)
            .WithMessage("Thứ tự điểm ghé không được trùng nhau.")
            .Must(HaveSequentialStopOrders)
            .WithMessage("Thứ tự điểm ghé phải bắt đầu từ 1 và tăng liên tục.");

        RuleForEach(x => x.ItineraryStops).ChildRules(stop =>
        {
            stop.RuleFor(x => x.StopOrder)
                .GreaterThan(0)
                .WithMessage("Thứ tự điểm ghé phải lớn hơn 0.");
            stop.RuleFor(x => x.StationId)
                .NotEmpty()
                .WithMessage("Bến/điểm ghé là bắt buộc.");
            stop.RuleFor(x => x.StayDurationMinutes)
                .InclusiveBetween(0, 1440)
                .WithMessage("Thời gian dừng phải từ 0 đến 1440 phút.");
            stop.RuleFor(x => x.Note)
                .MaximumLength(500)
                .WithMessage("Ghi chú điểm ghé không được vượt quá 500 ký tự.")
                .When(x => x.Note is not null);
        });
    }

    private static bool HaveUniqueStopOrders(IReadOnlyCollection<CreateCustomBookingItineraryStopRequest>? stops)
    {
        if (stops is null)
        {
            return true;
        }

        return stops.Select(x => x.StopOrder).Distinct().Count() == stops.Count;
    }

    private static bool HaveSequentialStopOrders(IReadOnlyCollection<CreateCustomBookingItineraryStopRequest>? stops)
    {
        if (stops is null || stops.Count == 0)
        {
            return true;
        }

        return stops
            .OrderBy(x => x.StopOrder)
            .Select((stop, index) => stop.StopOrder == index + 1)
            .All(x => x);
    }
}

internal sealed record CustomBookingTripPlan(
    Station FromStation,
    Station ToStation,
    IReadOnlyList<CustomBookingItineraryStop> ItineraryStops,
    IReadOnlyList<RouteSegment> RouteSegments,
    CustomBookingRouteEstimate RouteEstimate);

internal static class CustomBookingTripPlanner
{
    public static async Task<CustomBookingTripPlan> BuildAsync(
        IApplicationDbContext context,
        ICustomBookingTripRequest request,
        CancellationToken cancellationToken)
    {
        var fromStation = await ResolveStationAsync(
            context,
            request.FromStationId,
            nameof(request.FromStationId),
            cancellationToken);
        var toStation = await ResolveStationAsync(
            context,
            request.ToStationId,
            nameof(request.ToStationId),
            cancellationToken);
        var requestedStops = (request.ItineraryStops ?? Array.Empty<CreateCustomBookingItineraryStopRequest>())
            .OrderBy(x => x.StopOrder)
            .ToArray();
        var itineraryStations = await ResolveItineraryStationsAsync(context, requestedStops, cancellationToken);

        if (fromStation.Id == toStation.Id && requestedStops.Length == 0)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.ToStationId),
                "Nếu bến bắt đầu và bến kết thúc trùng nhau thì phải có ít nhất một điểm ghé.");
        }

        EnsureNoConsecutiveDuplicateStations(fromStation.Id, requestedStops, toStation.Id);

        var itineraryStops = requestedStops
            .Select(stop => new CustomBookingItineraryStop
            {
                StopOrder = stop.StopOrder,
                StationId = stop.StationId,
                Station = itineraryStations[stop.StationId],
                StayDurationMinutes = stop.StayDurationMinutes,
                Note = string.IsNullOrWhiteSpace(stop.Note) ? null : stop.Note.Trim()
            })
            .ToArray();
        var routeStationIds = new[] { fromStation.Id }
            .Concat(itineraryStops.Select(x => x.StationId))
            .Append(toStation.Id)
            .Distinct()
            .ToArray();
        var routeSegments = await context.Set<RouteSegment>()
            .Where(x => routeStationIds.Contains(x.FromStationId) && routeStationIds.Contains(x.ToStationId))
            .OrderBy(x => x.SegmentOrder)
            .ToArrayAsync(cancellationToken);
        var routeEstimate = CustomBookingRouteEstimator.Estimate(
            fromStation,
            itineraryStops,
            toStation,
            request.DepartureDate,
            request.PreferredStartTime,
            vessel: null,
            routeSegments);

        return new CustomBookingTripPlan(
            fromStation,
            toStation,
            itineraryStops,
            routeSegments,
            routeEstimate);
    }

    private static async Task<Station> ResolveStationAsync(
        IApplicationDbContext context,
        Guid stationId,
        string propertyName,
        CancellationToken cancellationToken)
    {
        var station = await context.Set<Station>()
            .SingleOrDefaultAsync(x => x.Id == stationId, cancellationToken);

        if (station is null)
        {
            throw AuthSupport.CreateValidationException(propertyName, "Bến không tồn tại.");
        }

        if (station.Status != StationStatus.Active)
        {
            throw AuthSupport.CreateValidationException(propertyName, "Bến không hoạt động.");
        }

        return station;
    }

    private static async Task<Dictionary<Guid, Station>> ResolveItineraryStationsAsync(
        IApplicationDbContext context,
        IReadOnlyCollection<CreateCustomBookingItineraryStopRequest> stops,
        CancellationToken cancellationToken)
    {
        if (stops.Count == 0)
        {
            return new Dictionary<Guid, Station>();
        }

        var stationIds = stops.Select(x => x.StationId).Distinct().ToArray();
        var stations = await context.Set<Station>()
            .Where(x => stationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        foreach (var stationId in stationIds)
        {
            if (!stations.TryGetValue(stationId, out var station))
            {
                throw AuthSupport.CreateValidationException(
                    nameof(CreateCustomBookingItineraryStopRequest.StationId),
                    "Điểm ghé không tồn tại.");
            }

            if (station.Status != StationStatus.Active)
            {
                throw AuthSupport.CreateValidationException(
                    nameof(CreateCustomBookingItineraryStopRequest.StationId),
                    "Điểm ghé không hoạt động.");
            }
        }

        return stations;
    }

    private static void EnsureNoConsecutiveDuplicateStations(
        Guid fromStationId,
        IReadOnlyCollection<CreateCustomBookingItineraryStopRequest> stops,
        Guid toStationId)
    {
        var stationIds = new[] { fromStationId }
            .Concat(stops.OrderBy(x => x.StopOrder).Select(x => x.StationId))
            .Append(toStationId)
            .ToArray();

        if (stationIds.Zip(stationIds.Skip(1), (current, next) => current == next).Any(x => x))
        {
            throw AuthSupport.CreateValidationException(
                nameof(ICustomBookingTripRequest.ItineraryStops),
                "Hai điểm liên tiếp trong lịch trình không được trùng nhau.");
        }
    }
}
