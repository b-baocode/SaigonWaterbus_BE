using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Infrastructure.Data;
using SaigonWaterbus.Web.Hubs;
using WaterbusRoute = SaigonWaterbus.Domain.Entities.Route;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Tracking : IEndpointGroup
{
    private const int OnlineThresholdSeconds = 60;
    private const string OpenIncidentStatus = "Open";

    private const string LocationExample =
        """
        {
          "messageId": "e6b4e258-3342-4a02-8546-134b7a6aa4db",
          "deviceId": "gps-wb-01",
          "boatCode": "WB_01",
          "routeCode": "DEMO-WATERBUS",
          "tripId": null,
          "lat": 10.7757162,
          "lng": 106.7086174,
          "speedKmh": 16,
          "heading": 70,
          "accuracyMeters": 15,
          "recordedAt": "2026-07-04T10:23:48+07:00",
          "sequence": 383,
          "batteryPercent": 56,
          "signalStrength": 3,
          "gpsFixQuality": "fair",
          "direction": "forward",
          "status": "moving"
        }
        """;

    public static string RoutePrefix => "/api/tracking";

    public static string OpenApiTag => "Tracking";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost(ReceiveLocation, "locations")
            .AllowAnonymous()
            .WithSummary("Nhận vị trí GPS realtime từ thiết bị")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "GPS device key",
                LocationExample,
                "Header bắt buộc: X-Device-Id, đây là mã định danh/key do phần mềm GPS sinh ra.",
                "deviceId trong body nếu gửi phải trùng với X-Device-Id.",
                "Backend dùng gps_devices để map thiết bị sang tàu, không tin boatId từ client.",
                "sequence phải tăng dần theo từng thiết bị."));

        group.MapGet(GetLatestBoatLocations, "boats/latest")
            .AllowAnonymous()
            .WithSummary("Lấy vị trí mới nhất của tất cả tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "FE dùng API này để vẽ marker ban đầu hoặc polling mỗi 2-5 giây.",
                $"isOnline=true nếu backend nhận GPS trong vòng {OnlineThresholdSeconds} giây gần nhất."));

        group.MapGet(GetLatestBoatLocationByCode, "boats/{boatCode}/latest")
            .AllowAnonymous()
            .WithSummary("Lấy vị trí mới nhất của một tàu theo mã tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "boatCode là tham số động, ví dụ WB_01."));
    }

    private static async Task<IResult> ReceiveLocation(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        IHubContext<TrackingHub> trackingHubContext,
        ILogger<Tracking> logger,
        [FromHeader(Name = "X-Device-Id")] string? headerDeviceId,
        TrackingLocationRequest request,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateLocationRequest(headerDeviceId, request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var deviceId = headerDeviceId!.Trim();
        var gpsDevice = await dbContext.GpsDevices
            .Include(x => x.Boat)
            .FirstOrDefaultAsync(x => x.DeviceId == deviceId && x.IsActive, cancellationToken);
        if (gpsDevice is null)
        {
            return Results.NotFound(new { message = "GPS device chưa được đăng ký hoặc đã bị khóa." });
        }

        if (!string.IsNullOrWhiteSpace(request.BoatCode)
            && !string.Equals(request.BoatCode.Trim(), gpsDevice.Boat.Code, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = "boatCode không khớp với thiết bị GPS đã đăng ký." });
        }

        if (gpsDevice.LastSequence.HasValue && request.Sequence <= gpsDevice.LastSequence.Value)
        {
            return Results.Conflict(new
            {
                accepted = false,
                message = "sequence cũ hơn hoặc trùng với bản tin đã nhận.",
                lastSequence = gpsDevice.LastSequence.Value
            });
        }

        var requestedRouteCode = request.RouteCode ?? request.CapturedRoute?.RouteCode;
        var route = await ResolveRouteAsync(dbContext, request.RouteId, requestedRouteCode, cancellationToken);
        if (route.NotFound)
        {
            return Results.NotFound(new { message = "routeId/routeCode không tồn tại." });
        }

        var trip = await ResolveTripAsync(dbContext, request.TripId, cancellationToken);
        if (trip.NotFound)
        {
            return Results.NotFound(new { message = "tripId không tồn tại." });
        }

        if (trip.Value is not null)
        {
            if (trip.Value.BoatId.HasValue && trip.Value.BoatId.Value != gpsDevice.BoatId)
            {
                return Results.BadRequest(new { message = "tripId không thuộc tàu của thiết bị GPS này." });
            }

            if (route.Value is not null && trip.Value.RouteId != route.Value.Id)
            {
                return Results.BadRequest(new { message = "tripId không thuộc route đã gửi." });
            }
        }

        var session = await ResolveCapturedRouteSessionAsync(
            dbContext,
            request.CapturedRoute?.SessionId,
            gpsDevice,
            route.Value,
            requestedRouteCode,
            trip.Value,
            cancellationToken);

        if (session.Error is not null)
        {
            return session.Error;
        }

        var now = timeProvider.GetUtcNow();
        var recordedAt = request.RecordedAt.ToUniversalTime();
        var routeId = route.Value?.Id ?? trip.Value?.RouteId ?? session.Value?.RouteId;
        var status = NormalizeOptionalText(request.Status) ?? "unknown";
        var direction = NormalizeOptionalText(request.Direction);
        var gpsFixQuality = NormalizeOptionalText(request.GpsFixQuality);

        var deviceRows = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE gps_devices
             SET last_sequence = {request.Sequence},
                 last_seen_at = {now},
                 updated_at = {now}
             WHERE gps_device_id = {gpsDevice.Id}
               AND (last_sequence IS NULL OR last_sequence < {request.Sequence});
             """,
            cancellationToken);

        if (deviceRows == 0)
        {
            var lastSequence = await dbContext.GpsDevices
                .AsNoTracking()
                .Where(x => x.Id == gpsDevice.Id)
                .Select(x => x.LastSequence)
                .FirstOrDefaultAsync(cancellationToken);

            return Results.Conflict(new
            {
                accepted = false,
                message = "sequence cũ hơn hoặc trùng với bản tin đã nhận.",
                lastSequence
            });
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO boat_latest_locations (
                 boat_id,
                 gps_device_id,
                 route_id,
                 trip_id,
                 latitude,
                 longitude,
                 speed_kmh,
                 heading,
                 accuracy_meters,
                 recorded_at,
                 received_at,
                 sequence,
                 status,
                 direction,
                 battery_percent,
                 signal_strength,
                 gps_fix_quality,
                 updated_at
             )
             VALUES (
                 {gpsDevice.BoatId},
                 {gpsDevice.Id},
                 {routeId},
                 {trip.Value?.Id},
                 {request.Lat},
                 {request.Lng},
                 {request.SpeedKmh},
                 {request.Heading},
                 {request.AccuracyMeters},
                 {recordedAt},
                 {now},
                 {request.Sequence},
                 {status},
                 {direction},
                 {request.BatteryPercent},
                 {request.SignalStrength},
                 {gpsFixQuality},
                 {now}
             )
             ON CONFLICT (boat_id) DO UPDATE
             SET gps_device_id = EXCLUDED.gps_device_id,
                 route_id = EXCLUDED.route_id,
                 trip_id = EXCLUDED.trip_id,
                 latitude = EXCLUDED.latitude,
                 longitude = EXCLUDED.longitude,
                 speed_kmh = EXCLUDED.speed_kmh,
                 heading = EXCLUDED.heading,
                 accuracy_meters = EXCLUDED.accuracy_meters,
                 recorded_at = EXCLUDED.recorded_at,
                 received_at = EXCLUDED.received_at,
                 sequence = EXCLUDED.sequence,
                 status = EXCLUDED.status,
                 direction = EXCLUDED.direction,
                 battery_percent = EXCLUDED.battery_percent,
                 signal_strength = EXCLUDED.signal_strength,
                 gps_fix_quality = EXCLUDED.gps_fix_quality,
                 updated_at = EXCLUDED.updated_at
             WHERE boat_latest_locations.sequence < EXCLUDED.sequence;
            """,
            cancellationToken);

        if (session.Value is not null)
        {
            dbContext.GpsTrackPoints.Add(new GpsTrackPoint
            {
                SessionId = session.Value.Id,
                GpsDeviceId = gpsDevice.Id,
                BoatId = gpsDevice.BoatId,
                RouteId = routeId,
                TripId = trip.Value?.Id ?? session.Value.TripId,
                MessageId = request.MessageId,
                Latitude = request.Lat,
                Longitude = request.Lng,
                SpeedKmh = request.SpeedKmh,
                Heading = request.Heading,
                AccuracyMeters = request.AccuracyMeters,
                RecordedAt = recordedAt,
                ReceivedAt = now,
                Sequence = request.Sequence,
                Status = status,
                Direction = direction,
                BatteryPercent = request.BatteryPercent,
                SignalStrength = request.SignalStrength,
                GpsFixQuality = gpsFixQuality
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var acceptedResponse = new TrackingLocationAcceptedResponse(
            true,
            request.MessageId,
            gpsDevice.DeviceId,
            gpsDevice.Boat.Code,
            gpsDevice.BoatId,
            routeId,
            trip.Value?.Id,
            now);

        var activeIncident = await LoadActiveIncidentForBoatAsync(
            dbContext,
            gpsDevice.BoatId,
            cancellationToken);

        await PublishBoatLocationAsync(
            trackingHubContext,
            logger,
            new BoatLocationRealtimeDto(
                gpsDevice.BoatId,
                gpsDevice.Boat.Code,
                gpsDevice.Boat.Name,
                gpsDevice.Boat.Status.ToString(),
                gpsDevice.DeviceId,
                request.MessageId,
                routeId,
                route.Value?.RouteCode,
                trip.Value?.Id,
                trip.Value?.TripCode,
                request.Lat,
                request.Lng,
                request.SpeedKmh,
                request.Heading,
                request.AccuracyMeters,
                recordedAt,
                now,
                request.Sequence,
                status,
                direction,
                request.BatteryPercent,
                request.SignalStrength,
                gpsFixQuality,
                activeIncident,
                IsOnline: true),
            cancellationToken);

        return Results.Ok(acceptedResponse);
    }

    private static async Task<IResult> GetLatestBoatLocations(
        IApplicationDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var locations = await dbContext.BoatLatestLocations
            .Include(x => x.Boat)
            .Include(x => x.Route)
            .Include(x => x.Trip)
            .OrderBy(x => x.Boat.Code)
            .ToListAsync(cancellationToken);

        var incidentsByBoatId = await LoadActiveIncidentsByBoatAsync(
            dbContext,
            locations.Select(x => x.BoatId),
            cancellationToken);

        return Results.Ok(locations.Select(x =>
        {
            incidentsByBoatId.TryGetValue(x.BoatId, out var activeIncident);
            return ToLatestLocationDto(x, now, activeIncident);
        }));
    }

    private static async Task<IResult> GetLatestBoatLocationByCode(
        IApplicationDbContext dbContext,
        TimeProvider timeProvider,
        string boatCode,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var normalizedBoatCode = boatCode.Trim();
        var location = await dbContext.BoatLatestLocations
            .Include(x => x.Boat)
            .Include(x => x.Route)
            .Include(x => x.Trip)
            .Where(x => x.Boat.Code == normalizedBoatCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (location is null)
        {
            return Results.NotFound(new { message = "Chưa có vị trí mới nhất cho tàu này." });
        }

        var activeIncident = await LoadActiveIncidentForBoatAsync(
            dbContext,
            location.BoatId,
            cancellationToken);

        return Results.Ok(ToLatestLocationDto(location, now, activeIncident));
    }

    private static Dictionary<string, string[]> ValidateLocationRequest(
        string? headerDeviceId,
        TrackingLocationRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        AddRequiredHeaderError(errors, headerDeviceId, "X-Device-Id");

        if (!string.IsNullOrWhiteSpace(request.DeviceId)
            && !string.IsNullOrWhiteSpace(headerDeviceId)
            && !string.Equals(request.DeviceId.Trim(), headerDeviceId.Trim(), StringComparison.Ordinal))
        {
            errors["deviceId"] = ["deviceId phải trùng với header X-Device-Id."];
        }

        if (request.Lat is < -90m or > 90m)
        {
            errors["lat"] = ["lat phải nằm trong khoảng -90 đến 90."];
        }

        if (request.Lng is < -180m or > 180m)
        {
            errors["lng"] = ["lng phải nằm trong khoảng -180 đến 180."];
        }

        if (request.Sequence < 0)
        {
            errors["sequence"] = ["sequence không được âm."];
        }

        if (request.SpeedKmh is < 0)
        {
            errors["speedKmh"] = ["speedKmh không được âm."];
        }

        if (request.Heading is < 0 or > 359)
        {
            errors["heading"] = ["heading phải nằm trong khoảng 0 đến 359."];
        }

        if (request.AccuracyMeters is < 0)
        {
            errors["accuracyMeters"] = ["accuracyMeters không được âm."];
        }

        if (request.BatteryPercent is < 0 or > 100)
        {
            errors["batteryPercent"] = ["batteryPercent phải nằm trong khoảng 0 đến 100."];
        }

        return errors;
    }

    private static void AddRequiredHeaderError(Dictionary<string, string[]> errors, string? value, string headerName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[headerName] = [$"Header {headerName} là bắt buộc."];
        }
    }

    private static async Task<LookupResult<WaterbusRoute>> ResolveRouteAsync(
        IApplicationDbContext dbContext,
        Guid? routeId,
        string? routeCode,
        CancellationToken cancellationToken)
    {
        if (routeId.HasValue)
        {
            var route = await dbContext.Set<WaterbusRoute>()
                .FirstOrDefaultAsync(x => x.Id == routeId.Value, cancellationToken);
            return route is null ? LookupResult<WaterbusRoute>.Missing() : LookupResult<WaterbusRoute>.Found(route);
        }

        if (!string.IsNullOrWhiteSpace(routeCode))
        {
            var normalizedRouteCode = routeCode.Trim();
            var route = await dbContext.Set<WaterbusRoute>()
                .FirstOrDefaultAsync(x => x.RouteCode == normalizedRouteCode, cancellationToken);
            return route is null ? LookupResult<WaterbusRoute>.Missing() : LookupResult<WaterbusRoute>.Found(route);
        }

        return LookupResult<WaterbusRoute>.Empty();
    }

    private static async Task<LookupResult<Trip>> ResolveTripAsync(
        IApplicationDbContext dbContext,
        Guid? tripId,
        CancellationToken cancellationToken)
    {
        if (!tripId.HasValue)
        {
            return LookupResult<Trip>.Empty();
        }

        var trip = await dbContext.Set<Trip>()
            .FirstOrDefaultAsync(x => x.Id == tripId.Value, cancellationToken);
        return trip is null ? LookupResult<Trip>.Missing() : LookupResult<Trip>.Found(trip);
    }

    private static async Task<CapturedRouteSessionLookup> ResolveCapturedRouteSessionAsync(
        ApplicationDbContext dbContext,
        Guid? sessionId,
        GpsDevice gpsDevice,
        WaterbusRoute? route,
        string? routeCode,
        Trip? trip,
        CancellationToken cancellationToken)
    {
        if (!sessionId.HasValue)
        {
            return CapturedRouteSessionLookup.Empty();
        }

        var session = await dbContext.GpsTrackingSessions
            .FirstOrDefaultAsync(x => x.Id == sessionId.Value, cancellationToken);

        if (session is null)
        {
            return CapturedRouteSessionLookup.Failed(
                Results.NotFound(new { message = "GPS session không tồn tại." }));
        }

        if (session.GpsDeviceId != gpsDevice.Id || session.BoatId != gpsDevice.BoatId)
        {
            return CapturedRouteSessionLookup.Failed(
                Results.BadRequest(new { message = "sessionId không thuộc thiết bị GPS này." }));
        }

        if (!IsRecording(session.Status))
        {
            return CapturedRouteSessionLookup.Failed(
                Results.BadRequest(new { message = "GPS session không còn ở trạng thái recording." }));
        }

        if (session.RouteId.HasValue && route is not null && session.RouteId.Value != route.Id)
        {
            return CapturedRouteSessionLookup.Failed(
                Results.BadRequest(new { message = "routeId/routeCode không khớp với GPS session." }));
        }

        if (!string.IsNullOrWhiteSpace(session.RouteCode)
            && !string.IsNullOrWhiteSpace(routeCode)
            && !string.Equals(session.RouteCode.Trim(), routeCode.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return CapturedRouteSessionLookup.Failed(
                Results.BadRequest(new { message = "routeCode không khớp với GPS session." }));
        }

        if (session.TripId.HasValue && trip is not null && session.TripId.Value != trip.Id)
        {
            return CapturedRouteSessionLookup.Failed(
                Results.BadRequest(new { message = "tripId không khớp với GPS session." }));
        }

        return CapturedRouteSessionLookup.Found(session);
    }

    private static bool IsRecording(string status) =>
        string.Equals(status, "Recording", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "recording", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static BoatLatestLocationDto ToLatestLocationDto(
        BoatLatestLocation location,
        DateTimeOffset now,
        ActiveIncidentTrackingDto? activeIncident) =>
        new(
            location.BoatId,
            location.Boat.Code,
            location.Boat.Name,
            location.Boat.Status.ToString(),
            location.RouteId,
            location.Route?.RouteCode,
            location.TripId,
            location.Trip?.TripCode,
            location.Latitude,
            location.Longitude,
            location.SpeedKmh,
            location.Heading,
            location.AccuracyMeters,
            location.RecordedAt,
            location.ReceivedAt,
            location.Sequence,
            location.Status,
            location.Direction,
            location.BatteryPercent,
            location.SignalStrength,
            location.GpsFixQuality,
            activeIncident,
            now - location.ReceivedAt <= TimeSpan.FromSeconds(OnlineThresholdSeconds));

    private static async Task<Dictionary<Guid, ActiveIncidentTrackingDto>> LoadActiveIncidentsByBoatAsync(
        IApplicationDbContext dbContext,
        IEnumerable<Guid> boatIds,
        CancellationToken cancellationToken)
    {
        var boatIdList = boatIds.Distinct().ToArray();
        if (boatIdList.Length == 0)
        {
            return new Dictionary<Guid, ActiveIncidentTrackingDto>();
        }

        var incidents = await dbContext.Incidents
            .AsNoTracking()
            .Include(x => x.ReplacementBoat)
            .Where(x => boatIdList.Contains(x.BoatId) && x.ResolutionStatus == OpenIncidentStatus)
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        return incidents
            .GroupBy(x => x.BoatId)
            .ToDictionary(x => x.Key, x => ToActiveIncidentTrackingDto(x.First()));
    }

    private static async Task<ActiveIncidentTrackingDto?> LoadActiveIncidentForBoatAsync(
        IApplicationDbContext dbContext,
        Guid boatId,
        CancellationToken cancellationToken)
    {
        var incident = await dbContext.Incidents
            .AsNoTracking()
            .Include(x => x.ReplacementBoat)
            .Where(x => x.BoatId == boatId && x.ResolutionStatus == OpenIncidentStatus)
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return incident is null ? null : ToActiveIncidentTrackingDto(incident);
    }

    private static ActiveIncidentTrackingDto ToActiveIncidentTrackingDto(Incident incident) =>
        new(
            incident.Id,
            incident.IncidentType,
            incident.Severity,
            incident.Description,
            incident.OccurredAt,
            incident.ResolutionStatus,
            incident.ReplacementBoatId,
            incident.ReplacementBoat?.Name);

    private static async Task PublishBoatLocationAsync(
        IHubContext<TrackingHub> trackingHubContext,
        ILogger logger,
        BoatLocationRealtimeDto payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await trackingHubContext.Clients.All.SendAsync(
                TrackingHub.BoatLocationEventName,
                payload,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Failed to broadcast GPS location for boat {BoatCode} sequence {Sequence}.",
                payload.BoatCode,
                payload.Sequence);
        }
    }

    public sealed record TrackingLocationRequest(
        Guid MessageId,
        string? DeviceId,
        string? BoatCode,
        Guid? RouteId,
        string? RouteCode,
        Guid? TripId,
        decimal Lat,
        decimal Lng,
        decimal? SpeedKmh,
        int? Heading,
        decimal? AccuracyMeters,
        DateTimeOffset RecordedAt,
        long Sequence,
        int? BatteryPercent,
        int? SignalStrength,
        string? GpsFixQuality,
        string? Direction,
        string? Status,
        TrackingCapturedRouteRequest? CapturedRoute);

    public sealed record TrackingCapturedRouteRequest(
        Guid? SessionId,
        string? RouteCode,
        string? RouteName,
        decimal? ProgressMeters,
        decimal? LengthMeters,
        int? SampleIndex,
        Guid? StartStationId,
        Guid? EndStationId);

    private sealed record TrackingLocationAcceptedResponse(
        bool Accepted,
        Guid MessageId,
        string DeviceId,
        string BoatCode,
        Guid BoatId,
        Guid? RouteId,
        Guid? TripId,
        DateTimeOffset ReceivedAt);

    private sealed record BoatLatestLocationDto(
        Guid BoatId,
        string BoatCode,
        string BoatName,
        string BoatStatus,
        Guid? RouteId,
        string? RouteCode,
        Guid? TripId,
        string? TripCode,
        decimal Lat,
        decimal Lng,
        decimal? SpeedKmh,
        int? Heading,
        decimal? AccuracyMeters,
        DateTimeOffset RecordedAt,
        DateTimeOffset ReceivedAt,
        long Sequence,
        string Status,
        string? Direction,
        int? BatteryPercent,
        int? SignalStrength,
        string? GpsFixQuality,
        ActiveIncidentTrackingDto? ActiveIncident,
        bool IsOnline);

    private sealed record BoatLocationRealtimeDto(
        Guid BoatId,
        string BoatCode,
        string BoatName,
        string BoatStatus,
        string DeviceId,
        Guid MessageId,
        Guid? RouteId,
        string? RouteCode,
        Guid? TripId,
        string? TripCode,
        decimal Lat,
        decimal Lng,
        decimal? SpeedKmh,
        int? Heading,
        decimal? AccuracyMeters,
        DateTimeOffset RecordedAt,
        DateTimeOffset ReceivedAt,
        long Sequence,
        string Status,
        string? Direction,
        int? BatteryPercent,
        int? SignalStrength,
        string? GpsFixQuality,
        ActiveIncidentTrackingDto? ActiveIncident,
        bool IsOnline);

    private sealed record ActiveIncidentTrackingDto(
        Guid IncidentId,
        string IncidentType,
        string? Severity,
        string Description,
        DateTimeOffset OccurredAt,
        string ResolutionStatus,
        Guid? ReplacementBoatId,
        string? ReplacementBoatName);

    private sealed record LookupResult<T>(T? Value, bool NotFound)
        where T : class
    {
        public static LookupResult<T> Empty() => new(null, false);

        public static LookupResult<T> Missing() => new(null, true);

        public static LookupResult<T> Found(T value) => new(value, false);
    }

    private sealed record CapturedRouteSessionLookup(GpsTrackingSession? Value, IResult? Error)
    {
        public static CapturedRouteSessionLookup Empty() => new(null, null);

        public static CapturedRouteSessionLookup Found(GpsTrackingSession session) => new(session, null);

        public static CapturedRouteSessionLookup Failed(IResult error) => new(null, error);
    }
}
