using NetTopologySuite.Geometries;

namespace SaigonWaterbus.Application.Routes;

/// <summary>
/// Canonical API representation for route geometry. Coordinates are always GeoJSON order:
/// [longitude, latitude]. Both route detail and GPS trip schedule responses use this helper.
/// </summary>
public static class RouteGeometryResponseSupport
{
    public static IReadOnlyList<double[]>? ToCoordinates(LineString? geometry) =>
        geometry is null
            ? null
            : geometry.Coordinates
                .Select(coordinate => new[] { coordinate.X, coordinate.Y })
                .ToArray();
}
