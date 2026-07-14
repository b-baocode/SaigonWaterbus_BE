using SaigonWaterbus.Application.Common.Interfaces;
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
    public const int DefaultTravelMinutes = 15;

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
                    orderedStops[i].StandardTravelMin ?? DefaultTravelMinutes);
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

    /// <summary>Tao trip_stops cho trip tu drafts (gan luon nav Station de dung dung cho DTO).</summary>
    public static List<TripStop> CreateTripStops(
        IApplicationDbContext context,
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

            context.Set<TripStop>().Add(tripStop);
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
                    null,
                    null,
                    trip.TripStatus.ToString(),
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
                trip.TripStatus.ToString(),
                draft.StayDurationMinutes,
                draft.Note))
            .ToList();
    }
}
