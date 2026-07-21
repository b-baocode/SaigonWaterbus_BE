using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using SaigonWaterbus.Web.Hubs;
using WaterbusRoute = SaigonWaterbus.Domain.Entities.Route;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Tracking : IEndpointGroup
{
    private static readonly TimeSpan LatestLocationCacheDuration = TimeSpan.FromSeconds(5);

    private const int OnlineThresholdSeconds = 60;
    private const int TripBoardingLeadMinutes = 10;
    private const int TripStartToleranceMinutes = 2;
    private const decimal MovingSpeedThresholdKmh = 0.5m;
    private const double IdleJumpDistanceThresholdKm = 0.5d;
    private static readonly TimeSpan IdleJumpWindow = TimeSpan.FromMinutes(5);
    private const int MaxRemainingMinutesToNextStation = 24 * 60;
    private const decimal MaxRemainingDistanceKmToNextStation = 10_000m;
    private const string OpenIncidentStatus = "Open";
    private const string MovementStatusMoving = "Moving";
    private const string MovementStatusAtStation = "AtStation";
    private const string MovementStatusBoarding = "Boarding";
    private const string MovementStatusScheduled = "Scheduled";
    private const string MovementStatusDelayed = "Delayed";
    private const string MovementStatusCompleted = "Completed";
    private const string MovementStatusCancelled = "Cancelled";

    private const string LocationExample =
        """
        {
          "messageId": "e6b4e258-3342-4a02-8546-134b7a6aa4db",
          "deviceId": "gps-wb-01",
          "boatCode": "WB_01",
          "routeCode": "DEMO-WATERBUS",
          "tripId": null,
          "nextStationId": null,
          "remainingDistanceKmToNextStation": 2.4,
          "remainingMinutesToNextStation": 8,
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
                "GPS có thể gửi nextStationId, remainingDistanceKmToNextStation, remainingMinutesToNextStation để FE hiển thị ETA tới bến kế tiếp.",
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

        if (gpsDevice.Boat.Status is BoatStatus.UnderMaintenance or BoatStatus.Inactive or BoatStatus.Retired)
        {
            return Results.Conflict(new
            {
                accepted = false,
                message = "Tàu không ở trạng thái vận hành nên backend không nhận GPS chạy tuyến.",
                boatStatus = gpsDevice.Boat.Status.ToString()
            });
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

        var routeId = route.Value?.Id ?? trip.Value?.RouteId ?? session.Value?.RouteId;
        var nextStationLookup = await ResolveNextStationAsync(
            dbContext,
            request.NextStationId,
            trip.Value,
            routeId,
            cancellationToken);
        if (nextStationLookup.Error is not null)
        {
            return nextStationLookup.Error;
        }
        var resolvedNextStation = nextStationLookup.Value;

        var now = timeProvider.GetUtcNow();
        var recordedAt = request.RecordedAt.ToUniversalTime();
        var status = NormalizeOptionalText(request.Status) ?? "unknown";
        var direction = NormalizeOptionalText(request.Direction);
        var gpsFixQuality = NormalizeOptionalText(request.GpsFixQuality);

        var previousLocation = await LoadPreviousLatestLocationAsync(
            dbContext,
            gpsDevice.BoatId,
            cancellationToken);
        var writeLat = request.Lat;
        var writeLng = request.Lng;
        var idleJumpHeld = ShouldHoldIdleJump(
            previousLocation,
            trip.Value,
            status,
            request.SpeedKmh,
            request.Lat,
            request.Lng,
            now,
            out var idleJumpDistanceKm);
        if (idleJumpHeld && previousLocation is not null)
        {
            writeLat = previousLocation.Latitude;
            writeLng = previousLocation.Longitude;
            logger.LogInformation(
                "Held idle GPS jump for boat {BoatCode}: {DistanceKm:F2}km from ({IncomingLat}, {IncomingLng}) to previous ({PreviousLat}, {PreviousLng}).",
                gpsDevice.Boat.Code,
                idleJumpDistanceKm,
                request.Lat,
                request.Lng,
                previousLocation.Latitude,
                previousLocation.Longitude);
        }

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
                 next_station_id,
                 latitude,
                 longitude,
                 remaining_distance_km_to_next_station,
                 remaining_minutes_to_next_station,
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
                 {resolvedNextStation?.Id},
                 {writeLat},
                 {writeLng},
                 {request.RemainingDistanceKmToNextStation},
                 {request.RemainingMinutesToNextStation},
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
                 next_station_id = EXCLUDED.next_station_id,
                 latitude = EXCLUDED.latitude,
                 longitude = EXCLUDED.longitude,
                 remaining_distance_km_to_next_station = EXCLUDED.remaining_distance_km_to_next_station,
                 remaining_minutes_to_next_station = EXCLUDED.remaining_minutes_to_next_station,
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

        var tripStatusChanged = ApplyTripStatusFromLocation(trip.Value, status, request.SpeedKmh, now);

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
        }

        if (session.Value is not null || tripStatusChanged)
        {
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
        var tripStops = trip.Value is null
            ? []
            : await LoadTripStopsAsync(dbContext, trip.Value.Id, cancellationToken);
        var currentStop = ResolveCurrentStop(tripStops);
        var lastStopEvent = ResolveLastStopEvent(tripStops);
        var movementStatus = ResolveTrackingMovementStatus(
            trip.Value,
            tripStops,
            status,
            request.SpeedKmh,
            now);

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
                route.Value?.RouteCode ?? trip.Value?.Route.RouteCode,
                trip.Value?.Id,
                trip.Value?.TripCode,
                movementStatus,
                currentStop?.StationId,
                currentStop?.Station?.StationCode,
                currentStop?.Station?.StationName,
                resolvedNextStation?.Id,
                resolvedNextStation?.StationCode,
                resolvedNextStation?.StationName,
                lastStopEvent.Event,
                lastStopEvent.OccurredAt,
                ResolveScheduledDepartureAt(trip.Value, currentStop),
                ResolveMinutesUntilDeparture(trip.Value, currentStop, now),
                request.RemainingDistanceKmToNextStation,
                request.RemainingMinutesToNextStation,
                writeLat,
                writeLng,
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
        IEndpointResponseCache responseCache,
        CancellationToken cancellationToken)
    {
        const string cacheKey = "tracking:boats:latest";
        var cachedLocations = await responseCache.GetOrCreateAsync(
            cacheKey,
            LatestLocationCacheDuration,
            ct => LoadLatestBoatLocationDtosAsync(
                dbContext,
                timeProvider,
                ct),
            cancellationToken);

        return Results.Ok(cachedLocations ?? []);
    }

    private static async Task<BoatLatestLocationDto[]> LoadLatestBoatLocationDtosAsync(
        IApplicationDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var locations = await dbContext.BoatLatestLocations
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.Route)
            .Include(x => x.Trip)
                .ThenInclude(x => x!.TripStops)
                    .ThenInclude(x => x.Station)
            .Include(x => x.NextStation)
            .OrderBy(x => x.Boat.Code)
            .ToListAsync(cancellationToken);

        var incidentsByBoatId = await LoadActiveIncidentsByBoatAsync(
            dbContext,
            locations.Select(x => x.BoatId),
            cancellationToken);

        return locations.Select(x =>
        {
            incidentsByBoatId.TryGetValue(x.BoatId, out var activeIncident);
            return ToLatestLocationDto(x, now, activeIncident);
        }).ToArray();
    }

    private static async Task<IResult> GetLatestBoatLocationByCode(
        IApplicationDbContext dbContext,
        TimeProvider timeProvider,
        IEndpointResponseCache responseCache,
        string boatCode,
        CancellationToken cancellationToken)
    {
        var normalizedBoatCode = boatCode.Trim();
        var cacheKey = $"tracking:boats:{normalizedBoatCode.ToUpperInvariant()}:latest";
        var location = await responseCache.GetOrCreateAsync(
            cacheKey,
            LatestLocationCacheDuration,
            ct => LoadLatestBoatLocationDtoByCodeAsync(
                dbContext,
                timeProvider,
                normalizedBoatCode,
                ct),
            cancellationToken);

        return location is null
            ? Results.NotFound(new { message = "Chưa có vị trí mới nhất cho tàu này." })
            : Results.Ok(location);
    }

    private static async Task<BoatLatestLocationDto?> LoadLatestBoatLocationDtoByCodeAsync(
        IApplicationDbContext dbContext,
        TimeProvider timeProvider,
        string normalizedBoatCode,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var location = await dbContext.BoatLatestLocations
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.Route)
            .Include(x => x.Trip)
                .ThenInclude(x => x!.TripStops)
                    .ThenInclude(x => x.Station)
            .Include(x => x.NextStation)
            .Where(x => x.Boat.Code == normalizedBoatCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (location is null)
        {
            return null;
        }

        var activeIncident = await LoadActiveIncidentForBoatAsync(
            dbContext,
            location.BoatId,
            cancellationToken);

        return ToLatestLocationDto(location, now, activeIncident);
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

        if (request.NextStationId == Guid.Empty)
        {
            errors["nextStationId"] = ["nextStationId không hợp lệ."];
        }

        if (request.RemainingDistanceKmToNextStation is < 0m or > MaxRemainingDistanceKmToNextStation)
        {
            errors["remainingDistanceKmToNextStation"] = [
                $"remainingDistanceKmToNextStation phải từ 0 đến {MaxRemainingDistanceKmToNextStation}."
            ];
        }

        if (request.RemainingMinutesToNextStation is < 0 or > MaxRemainingMinutesToNextStation)
        {
            errors["remainingMinutesToNextStation"] = [
                $"remainingMinutesToNextStation phải từ 0 đến {MaxRemainingMinutesToNextStation}."
            ];
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
            .Include(x => x.Route)
            .FirstOrDefaultAsync(x => x.Id == tripId.Value, cancellationToken);
        return trip is null ? LookupResult<Trip>.Missing() : LookupResult<Trip>.Found(trip);
    }

    private static async Task<NextStationLookup> ResolveNextStationAsync(
        ApplicationDbContext dbContext,
        Guid? nextStationId,
        Trip? trip,
        Guid? routeId,
        CancellationToken cancellationToken)
    {
        if (trip is not null)
        {
            var tripStops = await dbContext.Set<TripStop>()
                .AsNoTracking()
                .Include(x => x.Station)
                .Where(x => x.TripId == trip.Id)
                .OrderBy(x => x.StopOrder)
                .ToListAsync(cancellationToken);
            var expectedNextStop = ResolveExpectedNextTripStop(tripStops);
            if (!nextStationId.HasValue)
            {
                return expectedNextStop is null
                    ? NextStationLookup.Empty()
                    : NextStationLookup.Found(expectedNextStop.Station);
            }

            var requestedTripStop = tripStops.FirstOrDefault(x => x.StationId == nextStationId.Value);
            if (requestedTripStop is not null)
            {
                if (IsBehindExpectedStop(requestedTripStop, expectedNextStop))
                {
                    return expectedNextStop is null
                        ? NextStationLookup.Empty()
                        : NextStationLookup.Found(expectedNextStop.Station);
                }

                return NextStationLookup.Found(requestedTripStop.Station);
            }

            var belongsToRoute = await dbContext.RouteStops
                .AsNoTracking()
                .AnyAsync(x => x.RouteId == trip.RouteId && x.StationId == nextStationId.Value, cancellationToken);
            if (!belongsToRoute)
            {
                return NextStationLookup.Failed(
                    Results.BadRequest(new { message = "nextStationId không thuộc trip đang gửi GPS." }));
            }
        }

        if (!nextStationId.HasValue)
        {
            return NextStationLookup.Empty();
        }

        var station = await dbContext.Stations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == nextStationId.Value, cancellationToken);
        if (station is null)
        {
            return NextStationLookup.Failed(
                Results.NotFound(new { message = "nextStationId không tồn tại." }));
        }

        if (routeId.HasValue)
        {
            var belongsToRoute = await dbContext.RouteStops
                .AsNoTracking()
                .AnyAsync(x => x.RouteId == routeId.Value && x.StationId == nextStationId.Value, cancellationToken);
            if (!belongsToRoute)
            {
                return NextStationLookup.Failed(
                    Results.BadRequest(new { message = "nextStationId không thuộc route đang gửi GPS." }));
            }
        }

        return NextStationLookup.Found(station);
    }

    private static TripStop? ResolveExpectedNextTripStop(IReadOnlyList<TripStop> tripStops)
    {
        if (tripStops.Count == 0)
        {
            return null;
        }

        var currentStop = tripStops
            .Where(x => string.Equals(x.StopStatus, TripStopStatuses.Arrived, StringComparison.OrdinalIgnoreCase)
                && x.ActualDepartureTime is null)
            .OrderByDescending(x => x.StopOrder)
            .FirstOrDefault();
        if (currentStop is not null)
        {
            return currentStop;
        }

        var lastDepartedOrder = tripStops
            .Where(x => x.ActualDepartureTime is not null
                || string.Equals(x.StopStatus, TripStopStatuses.Departed, StringComparison.OrdinalIgnoreCase))
            .Select(x => (int?)x.StopOrder)
            .Max();
        if (lastDepartedOrder.HasValue)
        {
            return tripStops.FirstOrDefault(x => x.StopOrder > lastDepartedOrder.Value);
        }

        return tripStops.FirstOrDefault(x => x.ActualArrivalTime is null);
    }

    private static bool IsBehindExpectedStop(TripStop requestedStop, TripStop? expectedStop) =>
        expectedStop is not null && requestedStop.StopOrder < expectedStop.StopOrder;

    private static async Task<IReadOnlyList<TripStop>> LoadTripStopsAsync(
        ApplicationDbContext dbContext,
        Guid tripId,
        CancellationToken cancellationToken) =>
        await dbContext.Set<TripStop>()
            .AsNoTracking()
            .Include(x => x.Station)
            .Where(x => x.TripId == tripId)
            .OrderBy(x => x.StopOrder)
            .ToListAsync(cancellationToken);

    private static TripStop? ResolveCurrentStop(IReadOnlyList<TripStop> tripStops) =>
        tripStops
            .Where(x => string.Equals(x.StopStatus, TripStopStatuses.Arrived, StringComparison.OrdinalIgnoreCase)
                && x.ActualDepartureTime is null)
            .OrderByDescending(x => x.ActualArrivalTime ?? DateTimeOffset.MinValue)
            .ThenByDescending(x => x.StopOrder)
            .FirstOrDefault();

    private static StopEventInfo ResolveLastStopEvent(IReadOnlyList<TripStop> tripStops)
    {
        StopEventInfo latest = new(null, null);
        foreach (var stop in tripStops)
        {
            latest = Max(latest, new StopEventInfo(TripStopStatuses.Arrived, stop.ActualArrivalTime));
            latest = Max(latest, new StopEventInfo(TripStopStatuses.Departed, stop.ActualDepartureTime));
            if (string.Equals(stop.StopStatus, TripStopStatuses.Arriving, StringComparison.OrdinalIgnoreCase))
            {
                latest = Max(latest, new StopEventInfo(TripStopStatuses.Arriving, ResolveAuditTime(stop)));
            }
            else if (string.Equals(stop.StopStatus, TripStopStatuses.Skipped, StringComparison.OrdinalIgnoreCase))
            {
                latest = Max(latest, new StopEventInfo(TripStopStatuses.Skipped, stop.ActualDepartureTime ?? ResolveAuditTime(stop)));
            }
        }

        return latest;
    }

    private static StopEventInfo Max(StopEventInfo left, StopEventInfo right)
    {
        if (right.OccurredAt is null)
        {
            return left;
        }

        if (left.OccurredAt is null || right.OccurredAt > left.OccurredAt)
        {
            return right;
        }

        return left;
    }

    private static DateTimeOffset? ResolveAuditTime(TripStop stop)
    {
        if (stop.LastModified != default)
        {
            return stop.LastModified;
        }

        return stop.Created == default ? null : stop.Created;
    }

    private static string ResolveTrackingMovementStatus(
        Trip? trip,
        IReadOnlyList<TripStop> tripStops,
        string gpsStatus,
        decimal? speedKmh,
        DateTimeOffset now)
    {
        if (trip?.TripStatus == TripStatus.Cancelled)
        {
            return MovementStatusCancelled;
        }

        if (trip?.TripStatus == TripStatus.Completed)
        {
            return MovementStatusCompleted;
        }

        if (tripStops.Any(x => string.Equals(x.StopStatus, TripStopStatuses.Arrived, StringComparison.OrdinalIgnoreCase)
            && x.ActualDepartureTime is null))
        {
            return MovementStatusAtStation;
        }

        if (tripStops.Any(x => string.Equals(x.StopStatus, TripStopStatuses.Arriving, StringComparison.OrdinalIgnoreCase)))
        {
            return TripStopStatuses.Arriving;
        }

        if (trip?.TripStatus == TripStatus.Boarding)
        {
            return MovementStatusBoarding;
        }

        if (trip?.TripStatus == TripStatus.Delayed)
        {
            return MovementStatusDelayed;
        }

        if (IsMovingGpsStatus(gpsStatus) || speedKmh is > MovingSpeedThresholdKmh || trip?.TripStatus == TripStatus.InProgress)
        {
            return MovementStatusMoving;
        }

        return trip is not null && now < trip.DepartureTime
            ? MovementStatusScheduled
            : gpsStatus;
    }

    private static DateTimeOffset? ResolveScheduledDepartureAt(Trip? trip, TripStop? currentStop) =>
        currentStop?.AdjustedDepartureTime
        ?? currentStop?.PlannedDepartureTime
        ?? trip?.AdjustedDepartureTime
        ?? trip?.DepartureTime;

    private static int? ResolveMinutesUntilDeparture(Trip? trip, TripStop? currentStop, DateTimeOffset now)
    {
        if (trip is null || trip.TripStatus is TripStatus.Cancelled or TripStatus.Completed)
        {
            return null;
        }

        var scheduledDepartureAt = ResolveScheduledDepartureAt(trip, currentStop);
        if (!scheduledDepartureAt.HasValue || scheduledDepartureAt.Value <= now)
        {
            return null;
        }

        return Math.Max(0, (int)Math.Ceiling((scheduledDepartureAt.Value - now).TotalMinutes));
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

    private static async Task<PreviousLatestLocationSnapshot?> LoadPreviousLatestLocationAsync(
        ApplicationDbContext dbContext,
        Guid boatId,
        CancellationToken cancellationToken) =>
        await dbContext.BoatLatestLocations
            .AsNoTracking()
            .Where(x => x.BoatId == boatId)
            .Select(x => new PreviousLatestLocationSnapshot(
                x.RouteId,
                x.TripId,
                x.Latitude,
                x.Longitude,
                x.SpeedKmh,
                x.Status,
                x.ReceivedAt,
                x.Sequence))
            .FirstOrDefaultAsync(cancellationToken);

    private static bool ShouldHoldIdleJump(
        PreviousLatestLocationSnapshot? previousLocation,
        Trip? trip,
        string gpsStatus,
        decimal? speedKmh,
        decimal incomingLat,
        decimal incomingLng,
        DateTimeOffset now,
        out double distanceKm)
    {
        distanceKm = 0d;
        if (previousLocation is null
            || trip is not null
            || previousLocation.TripId.HasValue
            || now - previousLocation.ReceivedAt > IdleJumpWindow)
        {
            return false;
        }

        if (!IsIdleGpsStatus(gpsStatus)
            || !IsIdleGpsStatus(previousLocation.Status)
            || speedKmh.GetValueOrDefault() > MovingSpeedThresholdKmh
            || previousLocation.SpeedKmh.GetValueOrDefault() > MovingSpeedThresholdKmh)
        {
            return false;
        }

        distanceKm = CalculateDistanceKm(
            previousLocation.Latitude,
            previousLocation.Longitude,
            incomingLat,
            incomingLng);
        return distanceKm > IdleJumpDistanceThresholdKm;
    }

    private static bool IsIdleGpsStatus(string status) =>
        string.Equals(status, "idle", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "docked", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "stopped", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "waiting", StringComparison.OrdinalIgnoreCase);

    private static double CalculateDistanceKm(
        decimal startLat,
        decimal startLng,
        decimal endLat,
        decimal endLng)
    {
        const double earthRadiusKm = 6371d;
        var dLat = ToRadians(endLat - startLat);
        var dLng = ToRadians(endLng - startLng);
        var lat1 = ToRadians(startLat);
        var lat2 = ToRadians(endLat);

        var a = Math.Sin(dLat / 2d) * Math.Sin(dLat / 2d)
            + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLng / 2d) * Math.Sin(dLng / 2d);
        var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
        return earthRadiusKm * c;
    }

    private static double ToRadians(decimal degrees) => (double)degrees * Math.PI / 180d;

    private static bool ApplyTripStatusFromLocation(
        Trip? trip,
        string gpsStatus,
        decimal? speedKmh,
        DateTimeOffset now)
    {
        if (trip is null || trip.TripStatus is TripStatus.Cancelled or TripStatus.Completed)
        {
            return false;
        }

        var isMoving = IsMovingGpsStatus(gpsStatus) || speedKmh is > MovingSpeedThresholdKmh;
        if (isMoving && now >= trip.DepartureTime.AddMinutes(-TripStartToleranceMinutes))
        {
            return SetTripStatus(
                trip,
                TripStatus.InProgress,
                "GPS xác nhận tàu đã bắt đầu di chuyển.");
        }

        if (trip.TripStatus is TripStatus.Scheduled or TripStatus.Delayed
            && now >= trip.DepartureTime.AddMinutes(-TripBoardingLeadMinutes))
        {
            return SetTripStatus(
                trip,
                TripStatus.Boarding,
                "GPS đã nhận lịch và tàu đang chuẩn bị khởi hành.");
        }

        return false;
    }

    private static bool IsMovingGpsStatus(string status) =>
        string.Equals(status, "moving", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "departed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "in_progress", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "inprogress", StringComparison.OrdinalIgnoreCase);

    private static bool SetTripStatus(
        Trip trip,
        TripStatus status,
        string statusNote)
    {
        if (trip.TripStatus == status)
        {
            return false;
        }

        trip.TripStatus = status;
        trip.StatusNote = string.IsNullOrWhiteSpace(trip.StatusNote)
            ? statusNote
            : trip.StatusNote;
        return true;
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static BoatLatestLocationDto ToLatestLocationDto(
        BoatLatestLocation location,
        DateTimeOffset now,
        ActiveIncidentTrackingDto? activeIncident)
    {
        var tripStops = location.Trip?.TripStops
            .OrderBy(x => x.StopOrder)
            .ToArray() ?? [];
        var currentStop = ResolveCurrentStop(tripStops);
        var lastStopEvent = ResolveLastStopEvent(tripStops);

        return new(
            location.BoatId,
            location.Boat.Code,
            location.Boat.Name,
            location.Boat.Status.ToString(),
            location.RouteId,
            location.Route?.RouteCode,
            location.TripId,
            location.Trip?.TripCode,
            ResolveTrackingMovementStatus(
                location.Trip,
                tripStops,
                location.Status,
                location.SpeedKmh,
                now),
            currentStop?.StationId,
            currentStop?.Station?.StationCode,
            currentStop?.Station?.StationName,
            location.NextStationId,
            location.NextStation?.StationCode,
            location.NextStation?.StationName,
            lastStopEvent.Event,
            lastStopEvent.OccurredAt,
            ResolveScheduledDepartureAt(location.Trip, currentStop),
            ResolveMinutesUntilDeparture(location.Trip, currentStop, now),
            location.RemainingDistanceKmToNextStation,
            location.RemainingMinutesToNextStation,
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
    }

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
        Guid? NextStationId,
        decimal? RemainingDistanceKmToNextStation,
        int? RemainingMinutesToNextStation,
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

    private sealed record PreviousLatestLocationSnapshot(
        Guid? RouteId,
        Guid? TripId,
        decimal Latitude,
        decimal Longitude,
        decimal? SpeedKmh,
        string Status,
        DateTimeOffset ReceivedAt,
        long Sequence);

    public sealed record BoatLatestLocationDto(
        Guid BoatId,
        string BoatCode,
        string BoatName,
        string BoatStatus,
        Guid? RouteId,
        string? RouteCode,
        Guid? TripId,
        string? TripCode,
        string MovementStatus,
        Guid? CurrentStationId,
        string? CurrentStationCode,
        string? CurrentStationName,
        Guid? NextStationId,
        string? NextStationCode,
        string? NextStationName,
        string? LastStopEvent,
        DateTimeOffset? LastStopEventAt,
        DateTimeOffset? ScheduledDepartureAt,
        int? MinutesUntilDeparture,
        decimal? RemainingDistanceKmToNextStation,
        int? RemainingMinutesToNextStation,
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
        string MovementStatus,
        Guid? CurrentStationId,
        string? CurrentStationCode,
        string? CurrentStationName,
        Guid? NextStationId,
        string? NextStationCode,
        string? NextStationName,
        string? LastStopEvent,
        DateTimeOffset? LastStopEventAt,
        DateTimeOffset? ScheduledDepartureAt,
        int? MinutesUntilDeparture,
        decimal? RemainingDistanceKmToNextStation,
        int? RemainingMinutesToNextStation,
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

    public sealed record ActiveIncidentTrackingDto(
        Guid IncidentId,
        string IncidentType,
        string? Severity,
        string Description,
        DateTimeOffset OccurredAt,
        string ResolutionStatus,
        Guid? ReplacementBoatId,
        string? ReplacementBoatName);

    private sealed record StopEventInfo(string? Event, DateTimeOffset? OccurredAt);

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

    private sealed record NextStationLookup(Station? Value, IResult? Error)
    {
        public static NextStationLookup Empty() => new(null, null);

        public static NextStationLookup Found(Station station) => new(station, null);

        public static NextStationLookup Failed(IResult error) => new(null, error);
    }
}
