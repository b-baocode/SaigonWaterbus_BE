using NetTopologySuite.Geometries;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Routes;

internal static class RouteSegmentSupport
{
    public static LineString? BuildGeometry(IReadOnlyList<RouteSegmentCoordinateDto>? coordinates)
    {
        if (coordinates is null || coordinates.Count == 0)
        {
            return null;
        }

        return new LineString(
            coordinates
                .Select(x => new Coordinate(x.Longitude, x.Latitude))
                .ToArray())
        {
            SRID = 4326
        };
    }

    public static IReadOnlyList<double[]>? GeometryToCoordinates(LineString? geometry) =>
        geometry is null
            ? null
            : geometry.Coordinates
                .Select(c => new[] { c.X, c.Y })
                .ToArray();

    public static RouteSegmentDto ToDto(RouteSegment segment) =>
        new(
            segment.Id,
            segment.RouteId,
            segment.SegmentOrder,
            segment.FromStationId,
            segment.FromStation.StationCode,
            segment.FromStation.StationName,
            segment.ToStationId,
            segment.ToStation.StationCode,
            segment.ToStation.StationName,
            segment.DistanceKm,
            segment.EstimatedTravelMinutes,
            GeometryToCoordinates(segment.Geometry));
}
