using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Trips;

/// <summary>Lich trinh mot ben cua trip truoc khi luu vao trip_stops.</summary>
internal sealed record TripStopDraft(
    Guid StationId,
    Station? Station,
    int StopOrder,
    int StayDurationMinutes,
    string? Note,
    DateTimeOffset? PlannedArrivalTime,
    DateTimeOffset? PlannedDepartureTime);

/// <summary>
/// Xay va luu lich trinh tung ben (trip_stops) dung chung cho trip waterbus thuong va trip charter.
/// Quy uoc: ben dau chi co gio di (plannedArrival = null), ben cuoi chi co gio den (plannedDeparture = null).
/// </summary>
internal static class TripStopScheduleSupport
{
    /// <summary>Thoi gian chay mac dinh giua 2 ben khi route stop chua co standard_travel_min.</summary>
    public const decimal DefaultTravelMinutes = 15m;

    public static IReadOnlyDictionary<int, int>? ResolveStayDurationMinutesByStopOrder(
        Route route,
        IReadOnlyList<CreateTripStopScheduleInput>? stops,
        string propertyName)
    {
        var orderedStops = route.RouteStops.OrderBy(x => x.StopOrder).ToList();
        if (orderedStops.Count == 0)
        {
            return null;
        }

        var routeStopOrders = orderedStops.Select(x => x.StopOrder).ToHashSet();
        var firstStopOrder = orderedStops[0].StopOrder;
        var lastStopOrder = orderedStops[^1].StopOrder;
        var intermediateStopOrders = orderedStops
            .Where(x => x.StopOrder != firstStopOrder && x.StopOrder != lastStopOrder)
            .Select(x => x.StopOrder)
            .ToList();
        var requiresExplicitStayDurations = route.RouteType == RouteTypes.Regular
            && intermediateStopOrders.Count > 0;

        if (requiresExplicitStayDurations && stops is not { Count: > 0 })
        {
            throw new ValidationException([new ValidationFailure(propertyName,
                "stops là bắt buộc với tuyến thường có bến giữa; nhập stayDurationMinutes cho từng bến giữa tuyến.")]);
        }

        var result = new Dictionary<int, int>();

        foreach (var stop in stops ?? [])
        {
            if (!routeStopOrders.Contains(stop.StopOrder))
            {
                throw new ValidationException([new ValidationFailure(propertyName,
                    $"stopOrder {stop.StopOrder} không thuộc route đã chọn.")]);
            }

            if (stop.StopOrder == firstStopOrder || stop.StopOrder == lastStopOrder)
            {
                if (stop.StayDurationMinutes != 0)
                {
                    throw new ValidationException([new ValidationFailure(propertyName,
                        "stayDurationMinutes chỉ áp dụng cho các bến giữa tuyến.")]);
                }

                continue;
            }

            result[stop.StopOrder] = stop.StayDurationMinutes;
        }

        if (requiresExplicitStayDurations)
        {
            var missingStopOrders = intermediateStopOrders
                .Where(stopOrder => !result.ContainsKey(stopOrder))
                .ToList();
            if (missingStopOrders.Count > 0)
            {
                throw new ValidationException([new ValidationFailure(propertyName,
                    $"Thiếu stayDurationMinutes cho bến giữa stopOrder: {string.Join(", ", missingStopOrders)}.")]);
            }
        }

        return result.Count == 0 ? null : result;
    }

    /// <summary>
    /// Lich trinh waterbus thuong tu route stops: standardTravelMin cua stop i la phut chay
    /// tu stop i-1 den stop i (null -> 15 phut); khong co thoi gian dung tai ben.
    /// </summary>
    public static IReadOnlyList<TripStopDraft> BuildFromRouteStops(
        IEnumerable<RouteStop> routeStops,
        DateTimeOffset departureTimeUtc,
        IReadOnlyDictionary<int, int>? stayDurationMinutesByStopOrder = null)
    {
        var orderedStops = routeStops.OrderBy(x => x.StopOrder).ToList();
        var drafts = new List<TripStopDraft>();
        var previousDeparture = departureTimeUtc;
        for (var i = 0; i < orderedStops.Count; i++)
        {
            DateTimeOffset? plannedArrival;
            DateTimeOffset? plannedDeparture;
            if (i == 0)
            {
                plannedArrival = null;
                plannedDeparture = departureTimeUtc;
            }
            else
            {
                plannedArrival = previousDeparture.AddMinutes(
                    (double)(orderedStops[i].StandardTravelMin ?? DefaultTravelMinutes));
                var stayDurationMinutes = ResolveStayDurationMinutes(
                    orderedStops[i].StopOrder,
                    isFirstStop: false,
                    isLastStop: i == orderedStops.Count - 1,
                    stayDurationMinutesByStopOrder);
                plannedDeparture = i == orderedStops.Count - 1
                    ? null
                    : plannedArrival.Value.AddMinutes(stayDurationMinutes);
                previousDeparture = plannedDeparture ?? plannedArrival.Value;
            }

            var draftStayDurationMinutes = ResolveStayDurationMinutes(
                orderedStops[i].StopOrder,
                isFirstStop: i == 0,
                isLastStop: i == orderedStops.Count - 1,
                stayDurationMinutesByStopOrder);
            drafts.Add(new TripStopDraft(
                orderedStops[i].StationId,
                orderedStops[i].Station,
                orderedStops[i].StopOrder,
                draftStayDurationMinutes,
                Note: null,
                plannedArrival,
                plannedDeparture));
        }

        return drafts;
    }

    private static int ResolveStayDurationMinutes(
        int stopOrder,
        bool isFirstStop,
        bool isLastStop,
        IReadOnlyDictionary<int, int>? stayDurationMinutesByStopOrder)
    {
        if (isFirstStop || isLastStop || stayDurationMinutesByStopOrder is null)
        {
            return 0;
        }

        return stayDurationMinutesByStopOrder.TryGetValue(stopOrder, out var minutes)
            ? minutes
            : 0;
    }

    public static DateTimeOffset ComputeArrivalTimeUtc(
        IEnumerable<RouteStop> routeStops,
        DateTimeOffset departureTimeUtc)
    {
        var drafts = BuildFromRouteStops(routeStops, departureTimeUtc);
        return drafts.Count == 0
            ? departureTimeUtc
            : drafts[^1].PlannedArrivalTime ?? departureTimeUtc;
    }

    /// <summary>
    /// Giờ rời bến LÊN và giờ đến bến XUỐNG theo chặng (stop order) của hành khách — thay vì
    /// giờ đầu/cuối nguyên chuyến. Ưu tiên trip_stops đã lưu; trip cũ chưa có thì suy từ
    /// route stops; thiếu dữ liệu (order null = dữ liệu cũ chiếm cả chuyến) rơi về giờ chuyến.
    /// Yêu cầu caller đã load trip.TripStops (và trip.Route.RouteStops cho nhánh fallback).
    /// </summary>
    public static (DateTimeOffset Departure, DateTimeOffset Arrival) ResolveSegmentTimes(
        Trip trip,
        int? fromStopOrder,
        int? toStopOrder)
    {
        DateTimeOffset? fromDeparture = null;
        DateTimeOffset? toArrival = null;

        if (trip.TripStops.Count > 0)
        {
            var fromStop = fromStopOrder.HasValue
                ? trip.TripStops.FirstOrDefault(x => x.StopOrder == fromStopOrder.Value)
                : null;
            var toStop = toStopOrder.HasValue
                ? trip.TripStops.FirstOrDefault(x => x.StopOrder == toStopOrder.Value)
                : null;
            fromDeparture = fromStop?.AdjustedDepartureTime
                ?? fromStop?.PlannedDepartureTime
                ?? fromStop?.AdjustedArrivalTime
                ?? fromStop?.PlannedArrivalTime;
            toArrival = toStop?.AdjustedArrivalTime
                ?? toStop?.PlannedArrivalTime
                ?? toStop?.AdjustedDepartureTime
                ?? toStop?.PlannedDepartureTime;
        }
        else if (trip.Route is not null && trip.Route.RouteStops.Count > 0)
        {
            var drafts = BuildFromRouteStops(trip.Route.RouteStops, trip.DepartureTime);
            var fromDraft = fromStopOrder.HasValue
                ? drafts.FirstOrDefault(d => d.StopOrder == fromStopOrder.Value)
                : null;
            var toDraft = toStopOrder.HasValue
                ? drafts.FirstOrDefault(d => d.StopOrder == toStopOrder.Value)
                : null;
            fromDeparture = fromDraft?.PlannedDepartureTime ?? fromDraft?.PlannedArrivalTime;
            toArrival = toDraft?.PlannedArrivalTime ?? toDraft?.PlannedDepartureTime;
        }

        return (fromDeparture ?? trip.DepartureTime, toArrival ?? trip.ArrivalTime);
    }

    /// <summary>Tao trip_stops cho trip tu drafts (gan luon nav Station de dung dung cho DTO).</summary>
    public static List<TripStop> CreateTripStops(
        Trip trip,
        IReadOnlyList<TripStopDraft> drafts)
    {
        var tripStops = new List<TripStop>();
        foreach (var draft in drafts)
        {
            var tripStop = new TripStop
            {
                TripId = trip.Id,
                StationId = draft.StationId,
                Station = draft.Station!,
                StopOrder = draft.StopOrder,
                StayDurationMinutes = draft.StayDurationMinutes,
                PlannedArrivalTime = draft.PlannedArrivalTime,
                PlannedDepartureTime = draft.PlannedDepartureTime,
                Note = draft.Note
            };

            // Chỉ add qua navigation — add thêm vào DbSet sẽ khiến EF fixup nhét bản thứ hai
            // vào trip.TripStops (stops lặp đôi trong DTO của lệnh tạo trip). EF tự discover
            // entity mới qua trip khi SaveChanges.
            trip.TripStops.Add(tripStop);
            tripStops.Add(tripStop);
        }

        return tripStops;
    }

    /// <summary>
    /// Stops[] cua TripDetailDto: uu tien trip_stops da luu (co gio den/di va thoi gian dung);
    /// trip cu chua co trip_stops thi suy tu route stops nhu truoc.
    /// </summary>
    public static List<TripStopDto> BuildStopDtos(
        Trip trip,
        IReadOnlyDictionary<Guid, int>? boardingPassengerCountsByTripStopId = null,
        IReadOnlyDictionary<Guid, IReadOnlyList<TripStaffAssignmentDto>>? scanningStaffByTripStopId = null,
        IReadOnlyDictionary<Guid, TripStopPassengerCounts>? passengerCountsByTripStopId = null)
    {
        if (trip.TripStops.Count > 0)
        {
            return trip.TripStops
                .OrderBy(x => x.StopOrder)
                .Select(x =>
                {
                    var scanningStaff = scanningStaffByTripStopId is not null
                        && scanningStaffByTripStopId.TryGetValue(x.Id, out var staff)
                            ? staff
                            : [];
                    var passengerCounts = passengerCountsByTripStopId?.GetValueOrDefault(x.Id);
                    var stationImageUrls = TripMediaSupport.CreateStationImageUrls(x.Station);
                    return new TripStopDto(
                        x.Id,
                        x.StationId,
                        x.Station?.StationName ?? string.Empty,
                        x.Station?.StationCode ?? string.Empty,
                        x.StopOrder,
                        x.PlannedArrivalTime ?? x.PlannedDepartureTime,
                        x.PlannedDepartureTime ?? x.PlannedArrivalTime,
                        x.ActualArrivalTime,
                        x.ActualDepartureTime,
                        x.StopStatus,
                        x.StayDurationMinutes,
                        x.Note,
                        passengerCounts?.BoardingPassengerCount
                            ?? boardingPassengerCountsByTripStopId?.GetValueOrDefault(x.Id)
                            ?? 0,
                        scanningStaff,
                        passengerCounts?.AlightingPassengerCount ?? 0,
                        passengerCounts?.OnboardPassengerCount ?? 0,
                        passengerCounts?.SegmentPassengerCount ?? 0,
                        stationImageUrls.FirstOrDefault(),
                        stationImageUrls,
                        x.Station?.Address,
                        x.Station?.Latitude,
                        x.Station?.Longitude,
                        x.Station?.HasWaitingArea,
                        x.Station?.HasParking,
                        x.Station?.HasTicketCounter,
                        x.PlannedArrivalTime,
                        x.PlannedDepartureTime,
                        x.AdjustedArrivalTime,
                        x.AdjustedDepartureTime);
                })
                .ToList();
        }

        var orderedRouteStops = trip.Route.RouteStops.OrderBy(x => x.StopOrder).ToList();
        return BuildFromRouteStops(orderedRouteStops, trip.DepartureTime)
            .Select((draft, index) =>
            {
                var stationImageUrls = TripMediaSupport.CreateStationImageUrls(draft.Station);
                return new TripStopDto(
                    orderedRouteStops[index].Id,
                    draft.StationId,
                    draft.Station?.StationName ?? string.Empty,
                    draft.Station?.StationCode ?? string.Empty,
                    draft.StopOrder,
                    draft.PlannedArrivalTime ?? draft.PlannedDepartureTime,
                    draft.PlannedDepartureTime ?? draft.PlannedArrivalTime,
                    null,
                    null,
                    TripStopStatuses.Scheduled,
                    draft.StayDurationMinutes,
                    draft.Note,
                    0,
                    [],
                    StationImageUrl: stationImageUrls.FirstOrDefault(),
                    StationImageUrls: stationImageUrls,
                    StationAddress: draft.Station?.Address,
                    Latitude: draft.Station?.Latitude,
                    Longitude: draft.Station?.Longitude,
                    HasWaitingArea: draft.Station?.HasWaitingArea,
                    HasParking: draft.Station?.HasParking,
                    HasTicketCounter: draft.Station?.HasTicketCounter,
                    PlannedArrival: draft.PlannedArrivalTime,
                    PlannedDeparture: draft.PlannedDepartureTime);
            })
            .ToList();
    }
}
