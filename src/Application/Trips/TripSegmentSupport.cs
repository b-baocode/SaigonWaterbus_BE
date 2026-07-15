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

        var fromCode = fromStationCode!.Trim().ToUpperInvariant();
        var toCode = toStationCode!.Trim().ToUpperInvariant();

        var fromStop = routeStops.FirstOrDefault(rs =>
            rs.Station.StationCode.Equals(fromCode, StringComparison.OrdinalIgnoreCase));
        if (fromStop is null)
        {
            throw new ValidationException([new ValidationFailure(fromPropertyName,
                $"Station '{fromCode}' is not a stop on this trip.")]);
        }

        var toStop = routeStops.FirstOrDefault(rs =>
            rs.Station.StationCode.Equals(toCode, StringComparison.OrdinalIgnoreCase));
        if (toStop is null)
        {
            throw new ValidationException([new ValidationFailure(toPropertyName,
                $"Station '{toCode}' is not a stop on this trip.")]);
        }

        if (fromStop.StopOrder >= toStop.StopOrder)
        {
            throw new ValidationException([new ValidationFailure(fromPropertyName,
                $"Station '{fromCode}' must come before '{toCode}' on the route.")]);
        }

        return new Segment(fromStop.StopOrder, toStop.StopOrder, fromStop, toStop);
    }
}
