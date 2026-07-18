using FluentValidation.Results;
using SaigonWaterbus.Domain.Entities;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Trips;

/// <summary>
/// Resolve chặng (trạm lên → trạm xuống) trên route stops của một trip, dùng chung cho
/// booking, giữ ghế và sơ đồ ghế. Chặng nửa mở [FromStopOrder, ToStopOrder): khách xuống
/// trạm nào thì ghế trống từ trạm đó.
/// </summary>
public static class TripSegmentSupport
{
    public sealed record Segment(int FromStopOrder, int ToStopOrder, RouteStop? FromStop, RouteStop? ToStop)
    {
        public bool IsFullTrip => FromStop is null;
    }

    /// <summary>Chặng "cả trip" — dùng khi client không truyền trạm hoặc trip không bán theo chặng.</summary>
    public static Segment FullTrip { get; } = new(int.MinValue, int.MaxValue, null, null);

    public static Segment Resolve(
        Trip trip,
        string? fromStationCode,
        string? toStationCode,
        string fromPropertyName,
        string toPropertyName)
    {
        var hasFrom = !string.IsNullOrWhiteSpace(fromStationCode);
        var hasTo = !string.IsNullOrWhiteSpace(toStationCode);

        if (!hasFrom && !hasTo)
        {
            return FullTrip;
        }

        if (!hasFrom || !hasTo)
        {
            throw new ValidationException([new ValidationFailure(
                hasFrom ? toPropertyName : fromPropertyName,
                "Phải truyền cả trạm lên và trạm xuống (hoặc bỏ trống cả hai).")]);
        }

        var routeStops = trip.Route?.RouteStops
            ?? throw new InvalidOperationException("Trip.Route.RouteStops must be loaded to resolve a segment.");

        var (fromStop, toStop) = ResolveStops(
            routeStops, fromStationCode!, toStationCode!, fromPropertyName, toPropertyName);

        return new Segment(fromStop.StopOrder, toStop.StopOrder, fromStop, toStop);
    }

    /// <summary>
    /// Trạm lên/xuống trên route stops. Một tuyến có thể ghé CÙNG MỘT BẾN nhiều lần (tuyến đi vòng
    /// về lại bến đầu), nên không index được theo station code. Cả hai đầu đều lấy lần ghé SỚM NHẤT:
    /// trạm lên là lần ghé đầu tiên, trạm xuống là lần ghé đầu tiên SAU trạm lên — khách xuống ngay
    /// lần đầu tàu tới bến đích, nên chặng ngắn nhất, tiền vé thấp nhất và ghế bị chiếm ít nhất.
    /// Tuyến ghé mỗi bến một lần cho kết quả y hệt cách cũ.
    /// </summary>
    public static (RouteStop From, RouteStop To) ResolveStops(
        IEnumerable<RouteStop> routeStops,
        string fromStationCode,
        string toStationCode,
        string fromPropertyName,
        string toPropertyName)
    {
        var ordered = routeStops.OrderBy(rs => rs.StopOrder).ToList();
        var fromCode = fromStationCode.Trim();
        var toCode = toStationCode.Trim();

        var fromStop = ordered.FirstOrDefault(rs => IsStation(rs, fromCode));
        if (fromStop is null)
        {
            throw new ValidationException([new ValidationFailure(fromPropertyName,
                $"Station '{fromCode.ToUpperInvariant()}' is not a stop on this trip.")]);
        }

        var toStop = ordered.FirstOrDefault(rs => IsStation(rs, toCode) && rs.StopOrder > fromStop.StopOrder);
        if (toStop is null)
        {
            if (!ordered.Any(rs => IsStation(rs, toCode)))
            {
                throw new ValidationException([new ValidationFailure(toPropertyName,
                    $"Station '{toCode.ToUpperInvariant()}' is not a stop on this trip.")]);
            }

            throw new ValidationException([new ValidationFailure(fromPropertyName,
                $"Station '{fromCode.ToUpperInvariant()}' must come before "
                + $"'{toCode.ToUpperInvariant()}' on the route.")]);
        }

        return (fromStop, toStop);
    }

    private static bool IsStation(RouteStop routeStop, string stationCode) =>
        routeStop.Station.StationCode.Equals(stationCode, StringComparison.OrdinalIgnoreCase);
}
