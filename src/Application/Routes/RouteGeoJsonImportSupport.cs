using System.Text.Json;
using FluentValidation.Results;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.Routes;

internal sealed record GeoJsonStationCandidate(
    int FeatureIndex,
    string? OsmId,
    string? StationCode,
    string? Name,
    string? PublicTransport,
    double Latitude,
    double Longitude)
{
    public Point ToPoint() => new(Longitude, Latitude) { SRID = 4326 };
}

internal sealed record GeoJsonWaterwaySegmentCandidate(
    int FeatureIndex,
    string? OsmId,
    string? Name,
    string WaterwayType,
    string? RouteCode,
    string? FromStationCode,
    string? ToStationCode,
    int? EstimatedTravelMinutes,
    int SegmentOrder,
    LineString Geometry);

internal sealed record ParsedNavigationMapGeoJson(
    IReadOnlyList<GeoJsonWaterwaySegmentCandidate> WaterwaySegments,
    IReadOnlyList<GeoJsonStationCandidate> StationCandidates);

internal static class RouteGeoJsonImportSupport
{
    // 500m vi ben nam tren bo con OSM ve tim song giua dong; song rong (Nha Be ~1km) can toi ~350-500m.
    private const double WaypointSnapThresholdMeters = 500;
    private const double CoordinateEqualityThresholdMeters = 1;
    private static readonly GeometryFactory GeometryFactory =
        NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

    public static ParsedNavigationMapGeoJson Parse(string geoJson)
    {
        var waterwaySegments = new List<GeoJsonWaterwaySegmentCandidate>();
        var stationCandidates = new List<GeoJsonStationCandidate>();

        try
        {
            using var doc = JsonDocument.Parse(geoJson);
            if (!doc.RootElement.TryGetProperty("features", out var features)
                || features.ValueKind != JsonValueKind.Array)
            {
                throw CreateValidationException(
                    "GeoJsonContent",
                    "GeoJSON phai la FeatureCollection va co mang features.");
            }

            var featureIndex = 0;

            foreach (var feature in features.EnumerateArray())
            {
                if (!feature.TryGetProperty("geometry", out var geom)
                    || geom.ValueKind != JsonValueKind.Object
                    || !geom.TryGetProperty("type", out var typeElement)
                    || typeElement.ValueKind != JsonValueKind.String)
                {
                    featureIndex++;
                    continue;
                }

                var type = typeElement.GetString();
                var osmId = GetPropString(feature, "@id");
                var name = GetPropString(feature, "name");
                var amenity = GetPropString(feature, "amenity");
                var publicTransport = GetPropString(feature, "public_transport");
                var waterway = GetPropString(feature, "waterway");
                var route = GetPropString(feature, "route");
                var routeCode = GetPropString(feature, "waterbus_route") ?? GetPropString(feature, "route_code");
                var stationCode = GetPropString(feature, "station_code") ?? GetPropString(feature, "stationCode");
                var fromStationCode = GetPropString(feature, "from_station_code");
                var toStationCode = GetPropString(feature, "to_station_code");
                var estimatedTravelMinutes = GetPropInt(feature, "estimated_travel_minutes")
                    ?? GetPropInt(feature, "travel_minutes");

                if (IsLineFeature(type))
                {
                    var segmentOrder = 0;
                    var segmentOsmId = osmId;
                    if (string.IsNullOrWhiteSpace(segmentOsmId) && string.IsNullOrWhiteSpace(name))
                    {
                        segmentOsmId = $"custom-feature/{featureIndex}";
                    }

                    foreach (var line in ParseWaterwayGeometries(geom))
                    {
                        waterwaySegments.Add(new GeoJsonWaterwaySegmentCandidate(
                            featureIndex,
                            segmentOsmId,
                            name,
                            NormalizeWaterwayType(waterway ?? route),
                            routeCode,
                            fromStationCode,
                            toStationCode,
                            estimatedTravelMinutes,
                            segmentOrder++,
                            line));
                    }
                }
                else if (type == "Point" && IsStationPointFeature(amenity, publicTransport, stationCode))
                {
                    if (!geom.TryGetProperty("coordinates", out var coordinates)
                        || coordinates.ValueKind != JsonValueKind.Array
                        || coordinates.GetArrayLength() < 2)
                    {
                        throw CreateValidationException(
                            "GeoJsonContent",
                            $"Feature #{featureIndex} co Point ferry_terminal nhung thieu toa do [longitude, latitude].");
                    }

                    stationCandidates.Add(new GeoJsonStationCandidate(
                        featureIndex,
                        osmId,
                        stationCode,
                        name,
                        publicTransport,
                        coordinates[1].GetDouble(),
                        coordinates[0].GetDouble()));
                }

                featureIndex++;
            }
        }
        catch (JsonException ex)
        {
            throw CreateValidationException("GeoJsonContent", $"Invalid GeoJSON: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            throw CreateValidationException("GeoJsonContent", $"Invalid GeoJSON structure: {ex.Message}");
        }
        catch (KeyNotFoundException ex)
        {
            throw CreateValidationException("GeoJsonContent", $"Invalid GeoJSON structure: {ex.Message}");
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw CreateValidationException("GeoJsonContent", $"Invalid GeoJSON coordinates: {ex.Message}");
        }

        return new ParsedNavigationMapGeoJson(waterwaySegments, stationCandidates);
    }

    public static Point CreateRepresentativePoint(IReadOnlyList<LineString> geometries)
    {
        if (geometries.Count == 0)
        {
            throw CreateValidationException("Waypoints", "Khong co waterway de tao diem dai dien cho via.");
        }

        var primary = geometries
            .OrderByDescending(CalculateLengthKm)
            .First();

        var halfLengthMeters = CalculateLengthKm(primary) * 500;
        double traversedMeters = 0;

        for (var i = 0; i < primary.NumPoints - 1; i++)
        {
            var start = primary.GetPointN(i).Coordinate;
            var end = primary.GetPointN(i + 1).Coordinate;
            var segmentMeters = HaversineMeters(start.Y, start.X, end.Y, end.X);

            if (traversedMeters + segmentMeters >= halfLengthMeters && segmentMeters > 0)
            {
                var ratio = (halfLengthMeters - traversedMeters) / segmentMeters;
                return new Point(
                    start.X + ((end.X - start.X) * ratio),
                    start.Y + ((end.Y - start.Y) * ratio))
                { SRID = 4326 };
            }

            traversedMeters += segmentMeters;
        }

        return primary.EndPoint;
    }

    public static LineString BuildRouteGeometry(
        IReadOnlyList<LineString> waterwayGeometries,
        IReadOnlyList<Point> waypointPoints)
    {
        if (waterwayGeometries.Count == 0)
        {
            throw CreateValidationException(
                "GeoJsonContent",
                "GeoJSON phai chua it nhat mot LineString/MultiLineString co waterway=river hoac canal.");
        }

        if (waypointPoints.Count < 2)
        {
            throw CreateValidationException(
                "Waypoints",
                "Can it nhat 2 waypoint de tao route geometry.");
        }

        var graph = WaterwayGraph.Create(waterwayGeometries);
        var snappedNodeIds = waypointPoints
            .Select(graph.AttachWaypoint)
            .ToList();

        var routeCoordinates = new List<Coordinate>();
        for (var i = 0; i < snappedNodeIds.Count - 1; i++)
        {
            var legCoordinates = graph.FindShortestPathCoordinates(snappedNodeIds[i], snappedNodeIds[i + 1]);

            // Waypoint dau tuyen (vd ben tau) nam ngoai mang song -> bat dau duong tu dung toa do waypoint.
            if (i == 0 && IsAwayFromNetwork(waypointPoints[0], legCoordinates[0]))
            {
                routeCoordinates.Add(new Coordinate(waypointPoints[0].X, waypointPoints[0].Y));
            }

            AppendCoordinates(routeCoordinates, legCoordinates);

            var nextWaypoint = waypointPoints[i + 1];
            var snappedEnd = legCoordinates[^1];
            if (IsAwayFromNetwork(nextWaypoint, snappedEnd))
            {
                // Tat vao dung toa do waypoint (ben tau); neu con chang ke tiep thi quay lai song.
                routeCoordinates.Add(new Coordinate(nextWaypoint.X, nextWaypoint.Y));
                if (i < snappedNodeIds.Count - 2)
                {
                    routeCoordinates.Add(new Coordinate(snappedEnd.X, snappedEnd.Y));
                }
            }
        }

        var normalized = NormalizeCoordinates(routeCoordinates);
        if (normalized.Length < 2)
        {
            throw CreateValidationException(
                "GeoJsonContent",
                "Khong the tao LineString hop le tu cac waypoint da chon.");
        }

        return new LineString(normalized) { SRID = 4326 };
    }

    /// <summary>Waypoint cach diem snap tren mang song hon nguong 1m (via nam san tren song -> false).</summary>
    private static bool IsAwayFromNetwork(Point waypoint, Coordinate snapped) =>
        HaversineMeters(waypoint.Y, waypoint.X, snapped.Y, snapped.X) > CoordinateEqualityThresholdMeters;

    private static bool IsLineFeature(string? geometryType) =>
        geometryType == "LineString" || geometryType == "MultiLineString";

    private static bool IsStationPointFeature(string? amenity, string? publicTransport, string? stationCode) =>
        !string.IsNullOrWhiteSpace(stationCode)
        || string.Equals(amenity, "ferry_terminal", StringComparison.OrdinalIgnoreCase)
        || string.Equals(publicTransport, "station", StringComparison.OrdinalIgnoreCase)
        || string.Equals(publicTransport, "stop_position", StringComparison.OrdinalIgnoreCase)
        || string.Equals(publicTransport, "platform", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeWaterwayType(string? waterway)
    {
        if (string.Equals(waterway, "river", StringComparison.OrdinalIgnoreCase))
        {
            return "river";
        }

        if (string.Equals(waterway, "canal", StringComparison.OrdinalIgnoreCase))
        {
            return "canal";
        }

        return "custom";
    }

    private static IEnumerable<LineString> ParseWaterwayGeometries(JsonElement geometry)
    {
        var type = geometry.GetProperty("type").GetString();
        if (type == "LineString")
        {
            var line = ParseLineStringCoordinates(geometry.GetProperty("coordinates"));
            if (line.NumPoints >= 2)
            {
                yield return line;
            }

            yield break;
        }

        foreach (var coordinates in geometry.GetProperty("coordinates").EnumerateArray())
        {
            var line = ParseLineStringCoordinates(coordinates);
            if (line.NumPoints >= 2)
            {
                yield return line;
            }
        }
    }

    private static LineString ParseLineStringCoordinates(JsonElement coordinates)
    {
        var parsedCoordinates = coordinates.EnumerateArray()
            .Select(value => new Coordinate(value[0].GetDouble(), value[1].GetDouble()))
            .ToArray();

        return new LineString(NormalizeCoordinates(parsedCoordinates)) { SRID = 4326 };
    }

    private static string? GetPropString(JsonElement feature, string key)
    {
        if (!feature.TryGetProperty("properties", out var props))
        {
            return null;
        }

        if (!props.TryGetProperty(key, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static int? GetPropInt(JsonElement feature, string key)
    {
        if (!feature.TryGetProperty("properties", out var props))
        {
            return null;
        }

        if (!props.TryGetProperty(key, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), out var stringNumber))
        {
            return stringNumber;
        }

        return null;
    }

    private static void AppendCoordinates(List<Coordinate> destination, IReadOnlyList<Coordinate> coordinates)
    {
        foreach (var coordinate in coordinates)
        {
            if (destination.Count == 0 || !CoordinatesEqual(destination[^1], coordinate))
            {
                destination.Add(coordinate);
            }
        }
    }

    private static Coordinate[] NormalizeCoordinates(IEnumerable<Coordinate> coordinates)
    {
        var normalized = new List<Coordinate>();
        foreach (var coordinate in coordinates)
        {
            if (normalized.Count == 0 || !CoordinatesEqual(normalized[^1], coordinate))
            {
                normalized.Add(new Coordinate(coordinate.X, coordinate.Y));
            }
        }

        return normalized.ToArray();
    }

    private static bool CoordinatesEqual(Coordinate a, Coordinate b) =>
        HaversineMeters(a.Y, a.X, b.Y, b.X) <= CoordinateEqualityThresholdMeters;

    internal static double CalculateLengthKm(LineString line)
    {
        double total = 0;
        for (var i = 0; i < line.NumPoints - 1; i++)
        {
            total += HaversineMeters(
                line.GetPointN(i).Y, line.GetPointN(i).X,
                line.GetPointN(i + 1).Y, line.GetPointN(i + 1).X);
        }

        return total / 1000.0;
    }

    internal static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusMeters = 6_371_000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
            * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return earthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static ValidationException CreateValidationException(string propertyName, string message) =>
        new([new ValidationFailure(propertyName, message)]);

    private sealed class WaterwayGraph
    {
        private readonly Dictionary<int, GraphEdge> _edges = new();
        private readonly Dictionary<int, List<int>> _adjacentEdges = new();
        private readonly Dictionary<CoordinateKey, int> _nodeIdsByCoordinate = new();
        private readonly Dictionary<int, Coordinate> _nodeCoordinates = new();
        private int _nextEdgeId = 1;
        private int _nextNodeId = 1;

        public static WaterwayGraph Create(IReadOnlyList<LineString> waterways)
        {
            var graph = new WaterwayGraph();
            var nodedGeometry = GeometryFactory.BuildGeometry(waterways.Cast<Geometry>().ToList()).Union();
            foreach (var lineString in ExtractLineStrings(nodedGeometry))
            {
                var coordinates = NormalizeCoordinates(lineString.Coordinates);
                if (coordinates.Length < 2)
                {
                    continue;
                }

                var startNodeId = graph.GetOrCreateNode(coordinates[0]);
                var endNodeId = graph.GetOrCreateNode(coordinates[^1]);
                graph.AddEdge(startNodeId, endNodeId, coordinates);
            }

            return graph;
        }

        public int AttachWaypoint(Point waypointPoint)
        {
            var projection = FindNearestProjection(waypointPoint);
            if (projection.DistanceMeters > WaypointSnapThresholdMeters)
            {
                throw CreateValidationException(
                    "Waypoints",
                    $"Khong the snap waypoint tai ({waypointPoint.Y:F6}, {waypointPoint.X:F6}) vao waterway trong {WaypointSnapThresholdMeters}m.");
            }

            var edge = _edges[projection.EdgeId];
            var projectedCoordinate = projection.ProjectedCoordinate;

            if (CoordinatesEqual(_nodeCoordinates[edge.StartNodeId], projectedCoordinate))
            {
                return edge.StartNodeId;
            }

            if (CoordinatesEqual(_nodeCoordinates[edge.EndNodeId], projectedCoordinate))
            {
                return edge.EndNodeId;
            }

            var waypointNodeId = GetOrCreateNode(projectedCoordinate);
            if (_adjacentEdges.TryGetValue(waypointNodeId, out var connectedEdges)
                && connectedEdges.Count > 0)
            {
                return waypointNodeId;
            }

            SplitEdge(projection.EdgeId, projection.SegmentIndex, projectedCoordinate, waypointNodeId);
            return waypointNodeId;
        }

        public IReadOnlyList<Coordinate> FindShortestPathCoordinates(int startNodeId, int endNodeId)
        {
            if (startNodeId == endNodeId)
            {
                return [_nodeCoordinates[startNodeId]];
            }

            var distances = new Dictionary<int, double> { [startNodeId] = 0 };
            var previousNodes = new Dictionary<int, int>();
            var previousEdges = new Dictionary<int, int>();
            var queue = new PriorityQueue<int, double>();
            queue.Enqueue(startNodeId, 0);

            while (queue.Count > 0)
            {
                queue.TryDequeue(out var currentNodeId, out var currentDistance);
                if (distances.TryGetValue(currentNodeId, out var bestDistance)
                    && currentDistance > bestDistance + double.Epsilon)
                {
                    continue;
                }

                if (currentNodeId == endNodeId)
                {
                    break;
                }

                foreach (var edgeId in _adjacentEdges[currentNodeId])
                {
                    var edge = _edges[edgeId];
                    var nextNodeId = edge.StartNodeId == currentNodeId
                        ? edge.EndNodeId
                        : edge.StartNodeId;
                    var nextDistance = currentDistance + edge.LengthMeters;

                    if (distances.TryGetValue(nextNodeId, out var knownDistance)
                        && knownDistance <= nextDistance)
                    {
                        continue;
                    }

                    distances[nextNodeId] = nextDistance;
                    previousNodes[nextNodeId] = currentNodeId;
                    previousEdges[nextNodeId] = edgeId;
                    queue.Enqueue(nextNodeId, nextDistance);
                }
            }

            if (!distances.ContainsKey(endNodeId))
            {
                throw CreateValidationException(
                    "Waypoints",
                    "Khong tim thay duong noi giua cac waypoint tren mang waterway.");
            }

            var traversedEdges = new List<(GraphEdge Edge, bool Forward)>();
            for (var currentNodeId = endNodeId; currentNodeId != startNodeId; currentNodeId = previousNodes[currentNodeId])
            {
                var previousNodeId = previousNodes[currentNodeId];
                var edge = _edges[previousEdges[currentNodeId]];
                var forward = edge.StartNodeId == previousNodeId && edge.EndNodeId == currentNodeId;
                traversedEdges.Add((edge, forward));
            }

            traversedEdges.Reverse();

            var pathCoordinates = new List<Coordinate>();
            foreach (var (edge, forward) in traversedEdges)
            {
                var coordinates = forward ? edge.Coordinates : edge.Coordinates.Reverse().ToArray();
                AppendCoordinates(pathCoordinates, coordinates);
            }

            return pathCoordinates;
        }

        private NearestProjection FindNearestProjection(Point waypointPoint)
        {
            NearestProjection? bestProjection = null;

            foreach (var edge in _edges.Values)
            {
                for (var i = 0; i < edge.Coordinates.Length - 1; i++)
                {
                    var start = edge.Coordinates[i];
                    var end = edge.Coordinates[i + 1];
                    var dx = end.X - start.X;
                    var dy = end.Y - start.Y;
                    var segmentLengthSquared = dx * dx + dy * dy;
                    var t = segmentLengthSquared > 0
                        ? Math.Clamp(((waypointPoint.X - start.X) * dx + (waypointPoint.Y - start.Y) * dy) / segmentLengthSquared, 0.0, 1.0)
                        : 0.0;

                    var projected = new Coordinate(start.X + t * dx, start.Y + t * dy);
                    var distanceMeters = HaversineMeters(waypointPoint.Y, waypointPoint.X, projected.Y, projected.X);
                    if (bestProjection is null || distanceMeters < bestProjection.DistanceMeters)
                    {
                        bestProjection = new NearestProjection(edge.Id, i, projected, distanceMeters);
                    }
                }
            }

            return bestProjection
                ?? throw CreateValidationException("GeoJsonContent", "Khong co waterway nao de tao route.");
        }

        private void SplitEdge(int edgeId, int segmentIndex, Coordinate projectedCoordinate, int waypointNodeId)
        {
            var edge = _edges[edgeId];
            RemoveEdge(edgeId);

            var leftCoordinates = new List<Coordinate>();
            for (var i = 0; i <= segmentIndex; i++)
            {
                leftCoordinates.Add(new Coordinate(edge.Coordinates[i].X, edge.Coordinates[i].Y));
            }

            if (!CoordinatesEqual(leftCoordinates[^1], projectedCoordinate))
            {
                leftCoordinates.Add(new Coordinate(projectedCoordinate.X, projectedCoordinate.Y));
            }

            var rightCoordinates = new List<Coordinate> { new(projectedCoordinate.X, projectedCoordinate.Y) };
            for (var i = segmentIndex + 1; i < edge.Coordinates.Length; i++)
            {
                if (!CoordinatesEqual(rightCoordinates[^1], edge.Coordinates[i]))
                {
                    rightCoordinates.Add(new Coordinate(edge.Coordinates[i].X, edge.Coordinates[i].Y));
                }
            }

            AddEdge(edge.StartNodeId, waypointNodeId, NormalizeCoordinates(leftCoordinates));
            AddEdge(waypointNodeId, edge.EndNodeId, NormalizeCoordinates(rightCoordinates));
        }

        private int GetOrCreateNode(Coordinate coordinate)
        {
            var key = CoordinateKey.From(coordinate);
            if (_nodeIdsByCoordinate.TryGetValue(key, out var nodeId))
            {
                return nodeId;
            }

            nodeId = _nextNodeId++;
            _nodeIdsByCoordinate[key] = nodeId;
            _nodeCoordinates[nodeId] = new Coordinate(coordinate.X, coordinate.Y);
            _adjacentEdges[nodeId] = [];
            return nodeId;
        }

        private void AddEdge(int startNodeId, int endNodeId, Coordinate[] coordinates)
        {
            if (coordinates.Length < 2)
            {
                return;
            }

            var lengthMeters = 0d;
            for (var i = 0; i < coordinates.Length - 1; i++)
            {
                lengthMeters += HaversineMeters(
                    coordinates[i].Y, coordinates[i].X,
                    coordinates[i + 1].Y, coordinates[i + 1].X);
            }

            if (lengthMeters <= 0)
            {
                return;
            }

            var edge = new GraphEdge(_nextEdgeId++, startNodeId, endNodeId, coordinates, lengthMeters);
            _edges[edge.Id] = edge;
            _adjacentEdges[startNodeId].Add(edge.Id);
            _adjacentEdges[endNodeId].Add(edge.Id);
        }

        private void RemoveEdge(int edgeId)
        {
            var edge = _edges[edgeId];
            _adjacentEdges[edge.StartNodeId].Remove(edgeId);
            _adjacentEdges[edge.EndNodeId].Remove(edgeId);
            _edges.Remove(edgeId);
        }

        private static IEnumerable<LineString> ExtractLineStrings(Geometry geometry)
        {
            if (geometry is LineString lineString)
            {
                yield return lineString;
                yield break;
            }

            if (geometry is MultiLineString multiLineString)
            {
                foreach (var index in Enumerable.Range(0, multiLineString.NumGeometries))
                {
                    if (multiLineString.GetGeometryN(index) is LineString childLine)
                    {
                        yield return childLine;
                    }
                }

                yield break;
            }

            if (geometry is GeometryCollection collection)
            {
                foreach (var index in Enumerable.Range(0, collection.NumGeometries))
                {
                    foreach (var childLine in ExtractLineStrings(collection.GetGeometryN(index)))
                    {
                        yield return childLine;
                    }
                }
            }
        }

        private sealed record GraphEdge(
            int Id,
            int StartNodeId,
            int EndNodeId,
            Coordinate[] Coordinates,
            double LengthMeters);

        private sealed record NearestProjection(
            int EdgeId,
            int SegmentIndex,
            Coordinate ProjectedCoordinate,
            double DistanceMeters);
    }

    private readonly record struct CoordinateKey(double X, double Y)
    {
        public static CoordinateKey From(Coordinate coordinate) =>
            new(Math.Round(coordinate.X, 8), Math.Round(coordinate.Y, 8));
    }
}
