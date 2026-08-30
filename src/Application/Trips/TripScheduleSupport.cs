namespace SaigonWaterbus.Application.Trips;

using SaigonWaterbus.Domain.Entities;

/// <summary>
/// Kiem tra lich chay cua tau: mot tau khong duoc gan cho 2 chuyen co thoi gian chong lan,
/// va giua 2 chuyen lien tiep phai co thoi gian quay dau toi thieu (BoatTurnaroundBuffer).
/// </summary>
internal static class TripScheduleSupport
{
    public sealed record BoatScheduleWindow(
        string TripCode,
        DateTimeOffset DepartureTime,
        DateTimeOffset ArrivalTime,
        Guid StartStationId,
        Guid EndStationId,
        IReadOnlyList<RouteStop> RouteStops);

    public sealed record BoatScheduleConflict(
        BoatScheduleWindow Existing,
        DateTimeOffset EarliestAllowedDeparture,
        TimeSpan RepositionDuration);

    public sealed record StationDepartureWindow(
        string TripCode,
        Guid StationId,
        string? StationName,
        string RouteType,
        DateTimeOffset DepartureTime);

    public sealed record StationDepartureConflict(
        StationDepartureWindow Existing,
        DateTimeOffset EarliestAllowedDeparture);

    /// <summary>Thoi gian quay dau toi thieu giua 2 chuyen cua cung mot tau.</summary>
    public static readonly TimeSpan BoatTurnaroundBuffer = TimeSpan.FromMinutes(5);

    /// <summary>Cac chuyen xuat phat cung mot ben phai gian cach de staff check ve/len tau.</summary>
    public static readonly TimeSpan StationDepartureBuffer = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Cho phép tạo trip trước giờ khởi hành 10 phút.
    /// </summary>
    public static readonly TimeSpan MinimumCreationLeadTime = TimeSpan.FromMinutes(10);

    /// <summary>Gio khoi hanh qua sat (hoac da troi qua) so voi thoi diem tao chuyen.</summary>
    public static bool IsTooSoonToCreate(DateTimeOffset departureTime, DateTimeOffset now) =>
        departureTime < now.Add(MinimumCreationLeadTime);

    public static string BuildTooSoonMessage() =>
        $"Chuyến phải được tạo trước giờ khởi hành ít nhất {MinimumCreationLeadTime.TotalMinutes:0} phút.";

    /// <summary>
    /// Hai chuyen co dung chung tau bi coi la xung dot khi khoang thoi gian cua chung
    /// giao nhau, HOAC khoang trong giua chung nho hon thoi gian quay dau.
    /// </summary>
    public static bool ConflictsWithBuffer(
        DateTimeOffset existingDeparture,
        DateTimeOffset existingArrival,
        DateTimeOffset newDeparture,
        DateTimeOffset newArrival) =>
        existingDeparture < newArrival.Add(BoatTurnaroundBuffer)
        && newDeparture.Subtract(BoatTurnaroundBuffer) < existingArrival;

    public static BoatScheduleConflict? FindConflict(
        BoatScheduleWindow requested,
        IEnumerable<BoatScheduleWindow> existingWindows)
    {
        foreach (var existing in existingWindows.OrderBy(x => x.DepartureTime))
        {
            if (existing.DepartureTime <= requested.DepartureTime)
            {
                var earliestAllowedDeparture = ResolveEarliestDepartureAfter(existing, requested);
                if (requested.DepartureTime < earliestAllowedDeparture)
                {
                    return new BoatScheduleConflict(
                        existing,
                        earliestAllowedDeparture,
                        ResolveRepositionDuration(existing, requested));
                }

                continue;
            }

            // New trip departs BEFORE existing trip — check BOTH directions:
            // 1) Existing trip is blocked by the new trip (already covered above — but for
            //    completeness, we also need to check that the existing trip can START after
            //    the new trip finishes + buffer). This is what the original check does.
            // 2) New trip is blocked by the existing trip (MISSING before this fix).
            //    The new trip cannot start if it overlaps with an already-running existing trip.
            if (requested.DepartureTime < existing.DepartureTime
                && requested.ArrivalTime.Add(BoatTurnaroundBuffer) > existing.DepartureTime)
            {
                // New trip overlaps with existing trip's departure moment.
                return new BoatScheduleConflict(
                    existing,
                    existing.DepartureTime.Add(BoatTurnaroundBuffer),
                    ResolveRepositionDuration(requested, existing));
            }

            var earliestExistingDeparture = ResolveEarliestDepartureAfter(requested, existing);
            if (existing.DepartureTime < earliestExistingDeparture)
            {
                return new BoatScheduleConflict(
                    existing,
                    earliestExistingDeparture,
                    ResolveRepositionDuration(requested, existing));
            }
        }

        return null;
    }

    public static DateTimeOffset ResolveEarliestDepartureAfter(
        BoatScheduleWindow previous,
        BoatScheduleWindow next) =>
        TripStopScheduleSupport.RoundUpToWholeMinute(previous.ArrivalTime
            .Add(BoatTurnaroundBuffer)
            .Add(ResolveRepositionDuration(previous, next)));

    public static TimeSpan ResolveRepositionDuration(
        BoatScheduleWindow previous,
        BoatScheduleWindow next)
    {
        if (previous.EndStationId == next.StartStationId)
        {
            return TimeSpan.Zero;
        }

        var travelMinutes = TryResolveTravelMinutes(previous.RouteStops, previous.EndStationId, next.StartStationId)
            ?? TryResolveTravelMinutes(next.RouteStops, previous.EndStationId, next.StartStationId)
            ?? TripStopScheduleSupport.DefaultTravelMinutes;

        return TimeSpan.FromMinutes((double)travelMinutes);
    }

    public static Guid ResolveStartStationId(IReadOnlyList<RouteStop> routeStops) =>
        routeStops.OrderBy(x => x.StopOrder).First().StationId;

    public static Guid ResolveEndStationId(IReadOnlyList<RouteStop> routeStops) =>
        routeStops.OrderBy(x => x.StopOrder).Last().StationId;

    public static StationDepartureConflict? FindStationDepartureConflict(
        StationDepartureWindow requested,
        IEnumerable<StationDepartureWindow> existingWindows)
    {
        foreach (var existing in existingWindows
            .Where(x => x.StationId == requested.StationId
                && string.Equals(x.RouteType, requested.RouteType, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.DepartureTime))
        {
            var gap = (requested.DepartureTime - existing.DepartureTime).Duration();
            if (gap >= StationDepartureBuffer)
            {
                continue;
            }

            return new StationDepartureConflict(
                existing,
                TripStopScheduleSupport.RoundUpToWholeMinute(existing.DepartureTime.Add(StationDepartureBuffer)));
        }

        return null;
    }

    private static decimal? TryResolveTravelMinutes(
        IReadOnlyList<RouteStop> routeStops,
        Guid fromStationId,
        Guid toStationId)
    {
        if (fromStationId == toStationId)
        {
            return 0;
        }

        var orderedStops = routeStops.OrderBy(x => x.StopOrder).ToList();
        var fromIndex = orderedStops.FindIndex(x => x.StationId == fromStationId);
        var toIndex = orderedStops.FindIndex(x => x.StationId == toStationId);
        if (fromIndex < 0 || toIndex < 0)
        {
            return null;
        }

        var start = Math.Min(fromIndex, toIndex) + 1;
        var end = Math.Max(fromIndex, toIndex);
        decimal total = 0;
        for (var i = start; i <= end; i++)
        {
            total += orderedStops[i].StandardTravelMin ?? TripStopScheduleSupport.DefaultTravelMinutes;
        }

        return total;
    }

    public static string BuildConflictMessage(
        string tripCode,
        DateTimeOffset departure,
        DateTimeOffset arrival)
    {
        var localDeparture = departure.ToOffset(TimeSpan.FromHours(7));
        var localArrival = arrival.ToOffset(TimeSpan.FromHours(7));

        return $"Tàu đã có chuyến {tripCode} chạy từ {localDeparture:HH:mm dd/MM/yyyy} đến {localArrival:HH:mm dd/MM/yyyy}. "
            + $"Hai chuyến của cùng một tàu không được chồng giờ và phải cách nhau tối thiểu "
            + $"{BoatTurnaroundBuffer.TotalMinutes:0} phút để quay đầu.";
    }

    public static string BuildLocationAwareConflictMessage(
        string tripCode,
        DateTimeOffset departure,
        DateTimeOffset arrival,
        DateTimeOffset earliestAllowedDeparture,
        TimeSpan repositionDuration)
    {
        var localDeparture = departure.ToOffset(TimeSpan.FromHours(7));
        var localArrival = arrival.ToOffset(TimeSpan.FromHours(7));
        var localEarliest = earliestAllowedDeparture.ToOffset(TimeSpan.FromHours(7));

        return $"Tàu đã có chuyến {tripCode} chạy từ {localDeparture:HH:mm dd/MM/yyyy} đến {localArrival:HH:mm dd/MM/yyyy}. "
            + $"Chuyến mới chỉ được khởi hành sớm nhất lúc {localEarliest:HH:mm dd/MM/yyyy} "
            + $"sau khi tính {BoatTurnaroundBuffer.TotalMinutes:0} phút quay đầu"
            + (repositionDuration > TimeSpan.Zero
                ? $" và {repositionDuration.TotalMinutes:0} phút để tàu di chuyển về bến xuất phát."
                : ". ");
    }

    public static string BuildStationDepartureConflictMessage(
        string? stationName,
        string tripCode,
        DateTimeOffset conflictDeparture,
        DateTimeOffset earliestAllowedDeparture)
    {
        var localConflictDeparture = conflictDeparture.ToOffset(TimeSpan.FromHours(7));
        var localEarliest = earliestAllowedDeparture.ToOffset(TimeSpan.FromHours(7));
        var stationLabel = string.IsNullOrWhiteSpace(stationName) ? "bến này" : stationName;

        return $"Bến {stationLabel} đã có chuyến {tripCode} xuất phát lúc {localConflictDeparture:HH:mm dd/MM/yyyy}. "
            + $"Các chuyến cùng bến và cùng loại tuyến phải cách nhau tối thiểu {StationDepartureBuffer.TotalMinutes:0} phút để check vé/lên tàu. "
            + $"Chuyến mới chỉ được khởi hành sớm nhất lúc {localEarliest:HH:mm dd/MM/yyyy}.";
    }
}
