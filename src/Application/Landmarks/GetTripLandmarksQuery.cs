using NetTopologySuite.Geometries;
using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Landmarks;

/// <summary>Một điểm trên lộ trình để FE vẽ đường đi (đã đổi từ toạ độ PostGIS sang lat/lng).</summary>
public sealed record RoutePathPointDto(double Latitude, double Longitude);

/// <summary>Bến của chuyến kèm toạ độ — FE khỏi phải join với danh mục bến.</summary>
public sealed record TripLandmarkStationDto(
    Guid StationId,
    string StationCode,
    string StationName,
    int StopOrder,
    double Latitude,
    double Longitude);

/// <summary>
/// Landmark chuyến đi ngang qua, ở đúng giọng khách chọn.
/// <paramref name="AlongMeters"/> là quãng đường dọc tuyến tới điểm này (đã sắp tăng dần),
/// <paramref name="DistanceToPathMeters"/> là khoảng cách từ điểm tới tim tuyến.
/// </summary>
public sealed record TripLandmarkDto(
    Guid LandmarkId,
    string LandmarkName,
    string? Description,
    double Latitude,
    double Longitude,
    int DisplayOrder,
    int TriggerRadiusMeters,
    Guid? VoiceId,
    string? AudioUrl,
    decimal? DurationSeconds,
    double AlongMeters,
    double DistanceToPathMeters);

/// <summary>
/// Khách này có được hỏi hướng dẫn viên AI của chuyến không, và phiên còn bao lâu.
///
/// ĐI GHÉP VÀO ĐÂY thay vì làm endpoint riêng: app đã gọi màn này đúng một lần lúc mở, nên biết
/// được ngay có hiện nút mic hay không mà không tốn thêm vòng gọi nào. Cửa thật vẫn nằm ở
/// /api/tour-guide/ask — khối này chỉ để vẽ giao diện cho đúng.
/// </summary>
public sealed record TripLandmarkAccessDto(
    bool Allowed,
    string ReasonCode,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ExpiresAt,
    int? RemainingSeconds);

public sealed record TripLandmarksDto(
    Guid TripId,
    string TripCode,
    Guid RouteId,
    string? RouteCode,
    string RouteName,
    Guid? VoiceId,
    string? VoiceName,
    double RouteLengthMeters,
    IReadOnlyList<RoutePathPointDto> RoutePath,
    IReadOnlyList<TripLandmarkStationDto> Stations,
    IReadOnlyList<TripLandmarkDto> Landmarks,
    int MissingAudioCount,
    TripLandmarkAccessDto? TourGuideAccess = null);

/// <summary>
/// Trọn bộ dữ liệu màn hướng dẫn viên của một chuyến: lộ trình, bến, và landmark mà tuyến
/// đi ngang qua — đã lọc theo bán kính kích hoạt và sắp theo thứ tự gặp dọc đường.
/// Landmark không gắn tuyến (định danh bằng toạ độ) nên việc "chuyến này đi qua điểm nào"
/// tính ở đây thay vì để từng client tự chiếu hình học.
/// Bỏ trống <paramref name="VoiceId"/> thì dùng giọng mặc định (display_order nhỏ nhất).
/// </summary>
public sealed record GetTripLandmarksQuery(Guid TripId, Guid? VoiceId) : IRequest<TripLandmarksDto>;

public sealed class GetTripLandmarksQueryHandler
    : IRequestHandler<GetTripLandmarksQuery, TripLandmarksDto>
{
    private const double EarthRadiusMeters = 6371000d;

    private readonly IApplicationDbContext _context;
    private readonly TourGuideAccessSupport _access;
    private readonly TimeProvider _timeProvider;

    public GetTripLandmarksQueryHandler(
        IApplicationDbContext context,
        TourGuideAccessSupport access,
        TimeProvider timeProvider)
    {
        _context = context;
        _access = access;
        _timeProvider = timeProvider;
    }

    public async Task<TripLandmarksDto> Handle(
        GetTripLandmarksQuery request, CancellationToken cancellationToken)
    {
        var trip = await _context.Set<Trip>()
            .AsNoTracking()
            .Include(t => t.Route)
                .ThenInclude(r => r.RouteStops)
                    .ThenInclude(rs => rs.Station)
            .Include(t => t.TripStops)
                .ThenInclude(ts => ts.Station)
            .SingleOrDefaultAsync(t => t.Id == request.TripId, cancellationToken)
            ?? throw new NotFoundException("Trip not found.");

        var access = await _access.EvaluateAsync(request.TripId, cancellationToken);
        var voice = await ResolveVoiceAsync(request.VoiceId, cancellationToken);
        var voiceId = voice?.Id;

        var stations = BuildStations(trip);
        // Tuyến chưa vẽ geometry thì lấy tạm đường nối các bến — vẫn lọc được landmark, chỉ kém mịn.
        var path = BuildPath(trip.Route?.RouteGeometry, stations);

        var landmarks = await _context.Landmarks
            .AsNoTracking()
            .Where(l => l.IsActive)
            .Select(l => new
            {
                Landmark = l,
                Audio = l.Audios.FirstOrDefault(a => a.VoiceId == voiceId),
            })
            .ToListAsync(cancellationToken);

        var onRoute = new List<TripLandmarkDto>();
        foreach (var item in landmarks)
        {
            var l = item.Landmark;
            var projection = ProjectOntoPath(path, (double)l.Latitude, (double)l.Longitude);
            if (projection is null || projection.Value.DistanceMeters > l.TriggerRadiusMeters)
            {
                continue;
            }

            onRoute.Add(new TripLandmarkDto(
                l.Id, l.LandmarkName, l.Description,
                (double)l.Latitude, (double)l.Longitude,
                l.DisplayOrder, l.TriggerRadiusMeters,
                item.Audio?.VoiceId, item.Audio?.AudioUrl, item.Audio?.DurationSeconds,
                Math.Round(projection.Value.AlongMeters, 1),
                Math.Round(projection.Value.DistanceMeters, 1)));
        }

        onRoute.Sort((a, b) => a.AlongMeters.CompareTo(b.AlongMeters));

        return new TripLandmarksDto(
            trip.Id,
            trip.TripCode,
            trip.RouteId,
            trip.Route?.RouteCode,
            trip.Route?.RouteName ?? string.Empty,
            voice?.Id,
            voice?.Name,
            Math.Round(PathLengthMeters(path), 1),
            path,
            stations,
            onRoute,
            onRoute.Count(x => string.IsNullOrWhiteSpace(x.AudioUrl)),
            ToAccessDto(access, _timeProvider.GetUtcNow()));
    }

    /// <summary>Quy đổi sẵn số giây còn lại để client khỏi phải tự trừ giờ máy chủ với giờ máy mình.</summary>
    private static TripLandmarkAccessDto ToAccessDto(TourGuideAccess access, DateTimeOffset now)
    {
        int? remainingSeconds = access.ExpiresAt is DateTimeOffset expiresAt
            ? (int)Math.Max(0, Math.Round((expiresAt - now).TotalSeconds))
            : null;

        return new TripLandmarkAccessDto(
            access.Allowed,
            access.ReasonCode,
            access.StartedAt,
            access.ExpiresAt,
            remainingSeconds);
    }

    private async Task<Voice?> ResolveVoiceAsync(Guid? voiceId, CancellationToken cancellationToken)
    {
        var query = _context.Voices.AsNoTracking();

        // Giọng chỉ định phải còn active, nếu không thì rơi về giọng mặc định thay vì trả rỗng audio.
        var voice = voiceId.HasValue
            ? await query.SingleOrDefaultAsync(v => v.Id == voiceId.Value && v.IsActive, cancellationToken)
            : null;

        return voice ?? await query
            .Where(v => v.IsActive)
            .OrderBy(v => v.DisplayOrder).ThenBy(v => v.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Bến của chuyến theo trip_stops; trip cũ chưa có trip_stops thì lấy từ route stops.</summary>
    private static List<TripLandmarkStationDto> BuildStations(Trip trip)
    {
        var fromTripStops = trip.TripStops
            .Where(ts => ts.Station is not null)
            .OrderBy(ts => ts.StopOrder)
            .Select(ts => new { ts.StopOrder, ts.Station });

        var fromRouteStops = (trip.Route?.RouteStops ?? [])
            .Where(rs => rs.Station is not null)
            .OrderBy(rs => rs.StopOrder)
            .Select(rs => new { rs.StopOrder, rs.Station });

        var source = trip.TripStops.Count > 0 ? fromTripStops : fromRouteStops;

        return source
            .Where(x => x.Station.Latitude.HasValue && x.Station.Longitude.HasValue)
            .Select(x => new TripLandmarkStationDto(
                x.Station.Id,
                x.Station.StationCode,
                x.Station.StationName,
                x.StopOrder,
                (double)x.Station.Latitude!.Value,
                (double)x.Station.Longitude!.Value))
            .ToList();
    }

    private static List<RoutePathPointDto> BuildPath(
        LineString? geometry, List<TripLandmarkStationDto> stations)
    {
        if (geometry is not null && geometry.NumPoints >= 2)
        {
            var points = new List<RoutePathPointDto>(geometry.NumPoints);
            for (var i = 0; i < geometry.NumPoints; i++)
            {
                var coordinate = geometry.GetPointN(i).Coordinate;
                points.Add(new RoutePathPointDto(coordinate.Y, coordinate.X));
            }

            return points;
        }

        return stations.Select(s => new RoutePathPointDto(s.Latitude, s.Longitude)).ToList();
    }

    /// <summary>
    /// Chiếu một điểm lên polyline: khoảng cách ngắn nhất tới tuyến và quãng đường dọc tuyến
    /// tới chân đường vuông góc. Chiếu vuông góc trên từng đoạn trong hệ toạ độ độ — sai số
    /// không đáng kể ở phạm vi một đoạn geometry (vài chục mét).
    /// </summary>
    private static (double DistanceMeters, double AlongMeters)? ProjectOntoPath(
        List<RoutePathPointDto> path, double latitude, double longitude)
    {
        if (path.Count < 2)
        {
            return null;
        }

        var traversedMeters = 0d;
        var bestDistanceMeters = double.MaxValue;
        var bestAlongMeters = 0d;

        for (var i = 1; i < path.Count; i++)
        {
            var start = path[i - 1];
            var end = path[i];
            var segmentMeters = HaversineMeters(
                start.Latitude, start.Longitude, end.Latitude, end.Longitude);

            var dx = end.Longitude - start.Longitude;
            var dy = end.Latitude - start.Latitude;
            var lengthSquared = (dx * dx) + (dy * dy);
            var t = lengthSquared > 0
                ? Math.Clamp(
                    (((longitude - start.Longitude) * dx) + ((latitude - start.Latitude) * dy)) / lengthSquared,
                    0d,
                    1d)
                : 0d;

            var distanceMeters = HaversineMeters(
                latitude, longitude, start.Latitude + (t * dy), start.Longitude + (t * dx));

            if (distanceMeters < bestDistanceMeters)
            {
                bestDistanceMeters = distanceMeters;
                bestAlongMeters = traversedMeters + (segmentMeters * t);
            }

            traversedMeters += segmentMeters;
        }

        return (bestDistanceMeters, bestAlongMeters);
    }

    private static double PathLengthMeters(List<RoutePathPointDto> path)
    {
        var total = 0d;
        for (var i = 1; i < path.Count; i++)
        {
            total += HaversineMeters(
                path[i - 1].Latitude, path[i - 1].Longitude, path[i].Latitude, path[i].Longitude);
        }

        return total;
    }

    private static double HaversineMeters(double lat1, double lng1, double lat2, double lng2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLng = ToRadians(lng2 - lng1);
        var a = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
            + (Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
                * Math.Sin(dLng / 2) * Math.Sin(dLng / 2));

        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1d, Math.Sqrt(a)));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
