using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

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

    /// <summary>
    /// Lich trinh waterbus thuong tu route stops: standardTravelMin cua stop i la phut chay
    /// tu stop i-1 den stop i (null -> 15 phut); khong co thoi gian dung tai ben.
    /// </summary>
    public static IReadOnlyList<TripStopDraft> BuildFromRouteStops(
        IEnumerable<RouteStop> routeStops,
        DateTimeOffset departureTimeUtc)
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
                plannedDeparture = i == orderedStops.Count - 1 ? null : plannedArrival;
                previousDeparture = plannedDeparture ?? plannedArrival.Value;
            }

            drafts.Add(new TripStopDraft(
                orderedStops[i].StationId,
                orderedStops[i].Station,
                i + 1,
                StayDurationMinutes: 0,
                Note: null,
                plannedArrival,
                plannedDeparture));
        }

        return drafts;
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
            fromDeparture = fromStop?.PlannedDepartureTime ?? fromStop?.PlannedArrivalTime;
            toArrival = toStop?.PlannedArrivalTime ?? toStop?.PlannedDepartureTime;
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
    public static List<TripStopDto> BuildStopDtos(Trip trip)
    {
        if (trip.TripStops.Count > 0)
        {
            return trip.TripStops
                .OrderBy(x => x.StopOrder)
                .Select(x => new TripStopDto(
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
                    x.Note))
                .ToList();
        }

        var orderedRouteStops = trip.Route.RouteStops.OrderBy(x => x.StopOrder).ToList();
        return BuildFromRouteStops(orderedRouteStops, trip.DepartureTime)
            .Select((draft, index) => new TripStopDto(
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
                draft.Note))
            .ToList();
    }
}
