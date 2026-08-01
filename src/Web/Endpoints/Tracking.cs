using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Tracking;
using SaigonWaterbus.Application.Trips;
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
    private const decimal MovingSpeedThresholdKmh = 0.5m;
    private const double IdleJumpDistanceThresholdKm = 0.5d;
    private static readonly TimeSpan IdleJumpWindow = TimeSpan.FromMinutes(5);
    private const int DefaultLiveAuthoritySeconds = 90;
    private const double CurrentStationRadiusKm = 0.08d;
    private const double TeleportJumpDistanceThresholdKm = 0.25d;
    private const double DefaultMaxPlausibleSpeedKmh = 60d;
    private const double MaxPlausibleSpeedMultiplier = 2d;
    private static readonly TimeSpan TeleportJumpWindow = TimeSpan.FromMinutes(5);
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
    private const string MovementStatusIncident = "Incident";
    private const string IncidentLocationStatus = "incident";
    private const string BoatStatusRejectedReason = "boat-status";

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
                "Header tùy chọn: X-Tracking-Source=live/railway/wb-gps để ưu tiên packet từ live GPS trusted.",
                "Backend dùng gps_devices để map thiết bị sang tàu, không tin boatId từ client.",
                "GPS có thể gửi nextStationId, remainingDistanceKmToNextStation, remainingMinutesToNextStation để FE hiển thị ETA tới bến kế tiếp.",
                "recordedAt quyết định latest; sequence chỉ dùng để phá hòa khi recordedAt trùng."));

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

        group.MapGet(GetLatestBoatLocationByTripId, "trips/{tripId:guid}/latest")
            .AllowAnonymous()
            .WithSummary("Lấy vị trí live tracking theo trip")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "FE gọi khi nhấn vào một trip để biết tàu nào được gán và vị trí mới nhất của tàu đó.",
                "hasLiveLocationForTrip=true khi bản tin GPS mới nhất của tàu đang gắn đúng tripId này.",
                "Nếu latestLocation=null nghĩa là tàu chưa gửi GPS; vẫn trả boat để FE hiển thị tàu được phân công."));
    }

    private static async Task<IResult> ReceiveLocation(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        IConfiguration configuration,
        IEndpointResponseCache responseCache,
        IHubContext<TrackingHub> trackingHubContext,
        ILogger<Tracking> logger,
        [FromHeader(Name = "X-Device-Id")] string? headerDeviceId,
        [FromHeader(Name = "X-Tracking-Source")] string? headerTrackingSource,
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

        var now = timeProvider.GetUtcNow();
        if (gpsDevice.Boat.Status is BoatStatus.UnderMaintenance or BoatStatus.Inactive or BoatStatus.Retired)
        {
            logger.LogInformation(
                "Ignored GPS latest-location update for boat {BoatCode}: {Rejected}, boat status {BoatStatus}, incoming sequence {Sequence}.",
                gpsDevice.Boat.Code,
                BoatStatusRejectedReason,
                gpsDevice.Boat.Status,
                request.Sequence);

            var current = await LoadLatestBoatLocationDtoByCodeAsync(
                dbContext,
                timeProvider,
                gpsDevice.Boat.Code,
                cancellationToken);

            return Results.Ok(new TrackingLocationAcceptedResponse(
                false,
                request.MessageId,
                gpsDevice.DeviceId,
                gpsDevice.Boat.Code,
                gpsDevice.BoatId,
                null,
                null,
                now,
                LatestUpdated: false,
                Rejected: BoatStatusRejectedReason,
                Current: ToCurrentLocationResponse(current)));
        }

        var speedKmh = ResolveRequestSpeedKmh(request);
        var recordedAt = request.RecordedAt.ToUniversalTime();
        var liveAuthorityWindow = ResolveLiveAuthorityWindow(configuration);
        var trackingSource = ResolveTrackingSource(headerTrackingSource, request.Source);
        var activeIncident = await LoadActiveIncidentForBoatAsync(
            dbContext,
            gpsDevice.BoatId,
            cancellationToken);
        if (gpsDevice.Boat.Status == BoatStatus.Incident || activeIncident is not null)
        {
            return await ReceiveIncidentLocationAsync(
                dbContext,
                timeProvider,
                trackingHubContext,
                logger,
                gpsDevice,
                request,
                now,
                recordedAt,
                liveAuthorityWindow,
                trackingSource,
                speedKmh,
                activeIncident,
                responseCache,
                cancellationToken);
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
        var status = ResolveRequestStatus(request);
        var incomingBreaksLiveAuthority = IsTrustedLivePacket(trip.Value, status, speedKmh, trackingSource);
        var previousLocation = await LoadPreviousLatestLocationAsync(
            dbContext,
            gpsDevice.BoatId,
            cancellationToken);
        var mergeDecision = TrackingLatestMergePolicy.Decide(
            previousLocation?.RecordedAt,
            previousLocation?.Sequence,
            recordedAt,
            request.Sequence,
            trackingSource,
            now,
            liveAuthorityWindow,
            previousLocation?.Source,
            previousLocation?.LiveAuthorityUntil,
            incomingBreaksLiveAuthority);
        if (!mergeDecision.LatestUpdated)
        {
            await TouchGpsDeviceSequenceAsync(
                dbContext,
                gpsDevice.Id,
                request.Sequence,
                now,
                cancellationToken);

            logger.LogInformation(
                "Ignored GPS latest-location update for boat {BoatCode}: {Rejected}, incoming sequence {Sequence}, recordedAt {RecordedAt:O}.",
                gpsDevice.Boat.Code,
                mergeDecision.Rejected,
                request.Sequence,
                recordedAt);

            var current = await LoadLatestBoatLocationDtoByCodeAsync(
                dbContext,
                timeProvider,
                gpsDevice.Boat.Code,
                cancellationToken);

            return Results.Ok(new TrackingLocationAcceptedResponse(
                true,
                request.MessageId,
                gpsDevice.DeviceId,
                gpsDevice.Boat.Code,
                gpsDevice.BoatId,
                routeId,
                trip.Value?.Id,
                now,
                LatestUpdated: false,
                Rejected: mergeDecision.Rejected,
                Current: ToCurrentLocationResponse(current)));
        }

        var currentStation = await ResolveCurrentStationAsync(
            dbContext,
            request.CurrentStationCode,
            request.Lat,
            request.Lng,
            cancellationToken);

        var direction = NormalizeOptionalText(request.Direction);
        var gpsFixQuality = NormalizeOptionalText(request.GpsFixQuality);

        var writeLat = request.Lat;
        var writeLng = request.Lng;
        var locationJumpHeld = ShouldHoldLocationJump(
            previousLocation,
            gpsDevice.Boat,
            trip.Value,
            status,
            speedKmh,
            request.Lat,
            request.Lng,
            trackingSource,
            now,
            out var jumpDistanceKm,
            out var jumpReason);
        if (locationJumpHeld && previousLocation is not null)
        {
            writeLat = previousLocation.Latitude;
            writeLng = previousLocation.Longitude;
            logger.LogInformation(
                "Held GPS location jump for boat {BoatCode}: {Reason}, {DistanceKm:F2}km from ({IncomingLat}, {IncomingLng}) to previous ({PreviousLat}, {PreviousLng}).",
                gpsDevice.Boat.Code,
                jumpReason,
                jumpDistanceKm,
                request.Lat,
                request.Lng,
                previousLocation.Latitude,
                previousLocation.Longitude);
        }

        await TouchGpsDeviceSequenceAsync(
            dbContext,
            gpsDevice.Id,
            request.Sequence,
            now,
            cancellationToken);

        var latestSource = NormalizeOptionalText(trackingSource);
        var liveAuthorityUntil = TrackingLatestMergePolicy.IsLiveSource(latestSource)
            ? now.Add(liveAuthorityWindow)
            : (DateTimeOffset?)null;
        var canLiveAuthorityOverride = TrackingLatestMergePolicy.CanLiveAuthorityOverride(
            latestSource,
            recordedAt,
            now,
            liveAuthorityWindow);
        var latestRows = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO boat_latest_locations (
                 boat_id,
                 gps_device_id,
                 route_id,
                 trip_id,
                 current_station_id,
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
                 source,
                 live_authority_until,
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
                 {currentStation?.Id},
                 {resolvedNextStation?.Id},
                 {writeLat},
                 {writeLng},
                 {request.RemainingDistanceKmToNextStation},
                 {request.RemainingMinutesToNextStation},
                 {speedKmh},
                 {request.Heading},
                 {request.AccuracyMeters},
                 {recordedAt},
                 {now},
                 {request.Sequence},
                 {status},
                 {latestSource},
                 {liveAuthorityUntil},
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
                 current_station_id = EXCLUDED.current_station_id,
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
                 source = EXCLUDED.source,
                 live_authority_until = EXCLUDED.live_authority_until,
                 direction = EXCLUDED.direction,
                 battery_percent = EXCLUDED.battery_percent,
                 signal_strength = EXCLUDED.signal_strength,
                 gps_fix_quality = EXCLUDED.gps_fix_quality,
                 updated_at = EXCLUDED.updated_at
             WHERE ((boat_latest_locations.recorded_at < EXCLUDED.recorded_at
                    OR (boat_latest_locations.recorded_at = EXCLUDED.recorded_at
                        AND boat_latest_locations.sequence < EXCLUDED.sequence))
                    AND NOT (boat_latest_locations.source = 'live'
                        AND boat_latest_locations.live_authority_until IS NOT NULL
                        AND boat_latest_locations.live_authority_until >= {now}
                        AND NOT {incomingBreaksLiveAuthority}))
                OR ({canLiveAuthorityOverride}
                    AND boat_latest_locations.recorded_at >= EXCLUDED.recorded_at);
            """,
            cancellationToken);

        if (latestRows == 0)
        {
            var rejected = ResolveLatestRejectedReason(
                trackingSource,
                recordedAt,
                now,
                liveAuthorityWindow);
            logger.LogInformation(
                "Ignored GPS latest-location update for boat {BoatCode}: {Rejected}, incoming sequence {Sequence}, recordedAt {RecordedAt:O}.",
                gpsDevice.Boat.Code,
                rejected,
                request.Sequence,
                recordedAt);

            var current = await LoadLatestBoatLocationDtoByCodeAsync(
                dbContext,
                timeProvider,
                gpsDevice.Boat.Code,
                cancellationToken);

            return Results.Ok(new TrackingLocationAcceptedResponse(
                true,
                request.MessageId,
                gpsDevice.DeviceId,
                gpsDevice.Boat.Code,
                gpsDevice.BoatId,
                routeId,
                trip.Value?.Id,
                now,
                LatestUpdated: false,
                Rejected: rejected,
                Current: ToCurrentLocationResponse(current)));
        }

        var tripStatusChanged = ApplyTripStatusFromLocation(trip.Value, status, speedKmh, now);

        if (session.Value is not null
            && !await HasStoredTrackPointAsync(
                dbContext,
                session.Value.Id,
                gpsDevice.Id,
                request.Sequence,
                request.MessageId,
                cancellationToken))
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
                SpeedKmh = speedKmh,
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

        await InvalidateLatestLocationCachesAsync(
            responseCache,
            gpsDevice.Boat.Code,
            previousLocation?.TripId,
            trip.Value?.Id,
            cancellationToken);
        var latestLocation = await LoadLatestBoatLocationDtoByCodeAsync(
            dbContext,
            timeProvider,
            gpsDevice.Boat.Code,
            cancellationToken);
        if (latestLocation is not null)
        {
            await PublishBoatLocationAsync(
                trackingHubContext,
                logger,
                latestLocation,
                cancellationToken);
        }

        var acceptedResponse = new TrackingLocationAcceptedResponse(
            true,
            request.MessageId,
            gpsDevice.DeviceId,
            gpsDevice.Boat.Code,
            gpsDevice.BoatId,
            routeId,
            trip.Value?.Id,
            now,
            ReplacedBy: mergeDecision.ReplacedBy,
            Current: ToCurrentLocationResponse(latestLocation));

        return Results.Ok(acceptedResponse);
    }

    private static async Task<IResult> ReceiveIncidentLocationAsync(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        IHubContext<TrackingHub> trackingHubContext,
        ILogger<Tracking> logger,
        GpsDevice gpsDevice,
        TrackingLocationRequest request,
        DateTimeOffset now,
        DateTimeOffset recordedAt,
        TimeSpan liveAuthorityWindow,
        string? trackingSource,
        decimal? speedKmh,
        ActiveIncidentTrackingDto? activeIncident,
        IEndpointResponseCache responseCache,
        CancellationToken cancellationToken)
    {
        var direction = NormalizeOptionalText(request.Direction);
        var gpsFixQuality = NormalizeOptionalText(request.GpsFixQuality);
        var previousLocation = await LoadPreviousLatestLocationAsync(
            dbContext,
            gpsDevice.BoatId,
            cancellationToken);
        var incomingBreaksLiveAuthority = IsTrustedLivePacket(
            trip: null,
            IncidentLocationStatus,
            speedKmh,
            trackingSource);
        var mergeDecision = TrackingLatestMergePolicy.Decide(
            previousLocation?.RecordedAt,
            previousLocation?.Sequence,
            recordedAt,
            request.Sequence,
            trackingSource,
            now,
            liveAuthorityWindow,
            previousLocation?.Source,
            previousLocation?.LiveAuthorityUntil,
            incomingBreaksLiveAuthority);
        if (!mergeDecision.LatestUpdated)
        {
            await TouchGpsDeviceSequenceAsync(
                dbContext,
                gpsDevice.Id,
                request.Sequence,
                now,
                cancellationToken);

            logger.LogInformation(
                "Ignored incident GPS latest-location update for boat {BoatCode}: {Rejected}, incoming sequence {Sequence}, recordedAt {RecordedAt:O}.",
                gpsDevice.Boat.Code,
                mergeDecision.Rejected,
                request.Sequence,
                recordedAt);

            var current = await LoadLatestBoatLocationDtoByCodeAsync(
                dbContext,
                timeProvider,
                gpsDevice.Boat.Code,
                cancellationToken);

            return Results.Ok(new TrackingLocationAcceptedResponse(
                true,
                request.MessageId,
                gpsDevice.DeviceId,
                gpsDevice.Boat.Code,
                gpsDevice.BoatId,
                null,
                null,
                now,
                LatestUpdated: false,
                Rejected: mergeDecision.Rejected,
                Current: ToCurrentLocationResponse(current)));
        }

        var currentStation = await ResolveCurrentStationAsync(
            dbContext,
            request.CurrentStationCode,
            request.Lat,
            request.Lng,
            cancellationToken);
        var writeLat = request.Lat;
        var writeLng = request.Lng;
        var locationJumpHeld = ShouldHoldLocationJump(
            previousLocation,
            gpsDevice.Boat,
            trip: null,
            IncidentLocationStatus,
            speedKmh,
            request.Lat,
            request.Lng,
            trackingSource,
            now,
            out var jumpDistanceKm,
            out var jumpReason);
        if (locationJumpHeld && previousLocation is not null)
        {
            writeLat = previousLocation.Latitude;
            writeLng = previousLocation.Longitude;
            logger.LogInformation(
                "Held incident GPS location jump for boat {BoatCode}: {Reason}, {DistanceKm:F2}km from ({IncomingLat}, {IncomingLng}) to previous ({PreviousLat}, {PreviousLng}).",
                gpsDevice.Boat.Code,
                jumpReason,
                jumpDistanceKm,
                request.Lat,
                request.Lng,
                previousLocation.Latitude,
                previousLocation.Longitude);
        }

        await TouchGpsDeviceSequenceAsync(
            dbContext,
            gpsDevice.Id,
            request.Sequence,
            now,
            cancellationToken);

        var latestSource = NormalizeOptionalText(trackingSource);
        var liveAuthorityUntil = TrackingLatestMergePolicy.IsLiveSource(latestSource)
            ? now.Add(liveAuthorityWindow)
            : (DateTimeOffset?)null;
        var canLiveAuthorityOverride = TrackingLatestMergePolicy.CanLiveAuthorityOverride(
            latestSource,
            recordedAt,
            now,
            liveAuthorityWindow);
        var latestRows = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO boat_latest_locations (
                 boat_id,
                 gps_device_id,
                 route_id,
                 trip_id,
                 current_station_id,
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
                 source,
                 live_authority_until,
                 direction,
                 battery_percent,
                 signal_strength,
                 gps_fix_quality,
                 updated_at
             )
             VALUES (
                 {gpsDevice.BoatId},
                 {gpsDevice.Id},
                 NULL,
                 NULL,
                 {currentStation?.Id},
                 NULL,
                 {writeLat},
                 {writeLng},
                 NULL,
                 NULL,
                 {speedKmh},
                 {request.Heading},
                 {request.AccuracyMeters},
                 {recordedAt},
                 {now},
                 {request.Sequence},
                 {IncidentLocationStatus},
                 {latestSource},
                 {liveAuthorityUntil},
                 {direction},
                 {request.BatteryPercent},
                 {request.SignalStrength},
                 {gpsFixQuality},
                 {now}
             )
             ON CONFLICT (boat_id) DO UPDATE
             SET gps_device_id = EXCLUDED.gps_device_id,
                 route_id = NULL,
                 trip_id = NULL,
                 current_station_id = EXCLUDED.current_station_id,
                 next_station_id = NULL,
                 latitude = EXCLUDED.latitude,
                 longitude = EXCLUDED.longitude,
                 remaining_distance_km_to_next_station = NULL,
                 remaining_minutes_to_next_station = NULL,
                 speed_kmh = EXCLUDED.speed_kmh,
                 heading = EXCLUDED.heading,
                 accuracy_meters = EXCLUDED.accuracy_meters,
                 recorded_at = EXCLUDED.recorded_at,
                 received_at = EXCLUDED.received_at,
                 sequence = EXCLUDED.sequence,
                 status = EXCLUDED.status,
                 source = EXCLUDED.source,
                 live_authority_until = EXCLUDED.live_authority_until,
                 direction = EXCLUDED.direction,
                 battery_percent = EXCLUDED.battery_percent,
                 signal_strength = EXCLUDED.signal_strength,
                 gps_fix_quality = EXCLUDED.gps_fix_quality,
                 updated_at = EXCLUDED.updated_at
             WHERE ((boat_latest_locations.recorded_at < EXCLUDED.recorded_at
                    OR (boat_latest_locations.recorded_at = EXCLUDED.recorded_at
                        AND boat_latest_locations.sequence < EXCLUDED.sequence))
                    AND NOT (boat_latest_locations.source = 'live'
                        AND boat_latest_locations.live_authority_until IS NOT NULL
                        AND boat_latest_locations.live_authority_until >= {now}
                        AND NOT {incomingBreaksLiveAuthority}))
                OR ({canLiveAuthorityOverride}
                    AND boat_latest_locations.recorded_at >= EXCLUDED.recorded_at);
            """,
            cancellationToken);

        if (latestRows == 0)
        {
            var rejected = ResolveLatestRejectedReason(
                trackingSource,
                recordedAt,
                now,
                liveAuthorityWindow);
            logger.LogInformation(
                "Ignored incident GPS latest-location update for boat {BoatCode}: {Rejected}, incoming sequence {Sequence}, recordedAt {RecordedAt:O}.",
                gpsDevice.Boat.Code,
                rejected,
                request.Sequence,
                recordedAt);

            var current = await LoadLatestBoatLocationDtoByCodeAsync(
                dbContext,
                timeProvider,
                gpsDevice.Boat.Code,
                cancellationToken);

            return Results.Ok(new TrackingLocationAcceptedResponse(
                true,
                request.MessageId,
                gpsDevice.DeviceId,
                gpsDevice.Boat.Code,
                gpsDevice.BoatId,
                null,
                null,
                now,
                LatestUpdated: false,
                Rejected: rejected,
                Current: ToCurrentLocationResponse(current)));
        }

        await InvalidateLatestLocationCachesAsync(
            responseCache,
            gpsDevice.Boat.Code,
            previousLocation?.TripId,
            newTripId: null,
            cancellationToken);
        var latestLocation = await LoadLatestBoatLocationDtoByCodeAsync(
            dbContext,
            timeProvider,
            gpsDevice.Boat.Code,
            cancellationToken);
        if (latestLocation is not null)
        {
            await PublishBoatLocationAsync(
                trackingHubContext,
                logger,
                latestLocation,
                cancellationToken);
        }

        return Results.Ok(new TrackingLocationAcceptedResponse(
            true,
            request.MessageId,
            gpsDevice.DeviceId,
            gpsDevice.Boat.Code,
            gpsDevice.BoatId,
            null,
            null,
            now,
            ReplacedBy: mergeDecision.ReplacedBy,
            Current: ToCurrentLocationResponse(latestLocation)));
    }

    private static Task TouchGpsDeviceSequenceAsync(
        ApplicationDbContext dbContext,
        Guid gpsDeviceId,
        long sequence,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE gps_devices
             SET last_sequence = CASE
                     WHEN last_sequence IS NULL OR last_sequence < {sequence} THEN {sequence}
                     ELSE last_sequence
                 END,
                 last_seen_at = {now},
                 updated_at = {now}
             WHERE gps_device_id = {gpsDeviceId};
             """,
            cancellationToken);

    private static Task<bool> HasStoredTrackPointAsync(
        ApplicationDbContext dbContext,
        Guid sessionId,
        Guid gpsDeviceId,
        long sequence,
        Guid messageId,
        CancellationToken cancellationToken) =>
        dbContext.GpsTrackPoints
            .AsNoTracking()
            .AnyAsync(
                x => (x.SessionId == sessionId && x.Sequence == sequence)
                    || (x.GpsDeviceId == gpsDeviceId && x.MessageId == messageId),
                cancellationToken);

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
            .Include(x => x.CurrentStation)
            .Include(x => x.NextStation)
            .OrderBy(x => x.Boat.Code)
            .ToListAsync(cancellationToken);

        var incidentsByBoatId = await LoadActiveIncidentsByBoatAsync(
            dbContext,
            locations.Select(x => x.BoatId),
            cancellationToken);
        var onboardPassengerCounts = await TripPassengerCountSupport.LoadOnboardPassengerCountsByTripIdAsync(
            dbContext,
            locations.Where(x => x.TripId.HasValue).Select(x => x.TripId!.Value),
            cancellationToken);

        return locations.Select(x =>
        {
            incidentsByBoatId.TryGetValue(x.BoatId, out var activeIncident);
            return ToLatestLocationDto(
                x,
                now,
                activeIncident,
                x.TripId.HasValue ? onboardPassengerCounts.GetValueOrDefault(x.TripId.Value) : 0);
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

    private static async Task<IResult> GetLatestBoatLocationByTripId(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        IEndpointResponseCache responseCache,
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"tracking:trips:{tripId:N}:latest";
        var tripLocation = await responseCache.GetOrCreateAsync(
            cacheKey,
            LatestLocationCacheDuration,
            ct => LoadLatestBoatLocationDtoByTripIdAsync(
                dbContext,
                timeProvider,
                tripId,
                ct),
            cancellationToken);

        return tripLocation is null
            ? Results.NotFound(new { message = "Trip không tồn tại." })
            : Results.Ok(tripLocation);
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
            .Include(x => x.CurrentStation)
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
        var onboardPassengerCount = await TripPassengerCountSupport.LoadOnboardPassengerCountAsync(
            dbContext,
            location.TripId,
            cancellationToken);

        return ToLatestLocationDto(location, now, activeIncident, onboardPassengerCount);
    }

    private static async Task<TripLatestTrackingDto?> LoadLatestBoatLocationDtoByTripIdAsync(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var trip = await dbContext.Trips
            .AsNoTracking()
            .Include(x => x.Boat)
            .SingleOrDefaultAsync(x => x.Id == tripId, cancellationToken);
        if (trip is null)
        {
            return null;
        }

        var onboardPassengerCount = await TripPassengerCountSupport.LoadOnboardPassengerCountAsync(
            dbContext,
            trip.Id,
            cancellationToken);
        BoatLatestLocationDto? latestLocation = null;
        if (trip.BoatId.HasValue)
        {
            var location = await dbContext.BoatLatestLocations
                .AsNoTracking()
                .Include(x => x.Boat)
                .Include(x => x.Route)
                .Include(x => x.Trip)
                    .ThenInclude(x => x!.TripStops)
                        .ThenInclude(x => x.Station)
                .Include(x => x.CurrentStation)
                .Include(x => x.NextStation)
                .SingleOrDefaultAsync(x => x.BoatId == trip.BoatId.Value, cancellationToken);

            if (location is not null)
            {
                var activeIncident = await LoadActiveIncidentForBoatAsync(
                    dbContext,
                    location.BoatId,
                    cancellationToken);
                latestLocation = ToLatestLocationDto(
                    location,
                    now,
                    activeIncident,
                    location.TripId == trip.Id ? onboardPassengerCount : 0);
            }
        }

        return new TripLatestTrackingDto(
            trip.Id,
            trip.TripCode,
            trip.TripStatus.ToString(),
            onboardPassengerCount,
            trip.BoatId,
            ToTripBoatDto(trip.Boat, trip.CapacitySnapshot),
            latestLocation,
            latestLocation?.TripId == trip.Id,
            latestLocation?.IsOnline ?? false);
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

        if (request.Speed is < 0)
        {
            errors["speed"] = ["speed không được âm."];
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

    private static int? ResolveMinutesUntilDeparture(Trip? trip, TripStop? currentStop, DateTimeOffset now)
    {
        if (trip is null || trip.TripStatus is TripStatus.Cancelled or TripStatus.Completed)
        {
            return null;
        }

        var scheduledDepartureAt = TripStatusTransitionSupport.ResolveScheduledDepartureAt(trip, currentStop);
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
                x.Source,
                x.LiveAuthorityUntil,
                x.RecordedAt,
                x.ReceivedAt,
                x.Sequence))
            .FirstOrDefaultAsync(cancellationToken);

    private static bool ShouldHoldLocationJump(
        PreviousLatestLocationSnapshot? previousLocation,
        Boat boat,
        Trip? trip,
        string gpsStatus,
        decimal? speedKmh,
        decimal incomingLat,
        decimal incomingLng,
        string? trackingSource,
        DateTimeOffset now,
        out double distanceKm,
        out string reason)
    {
        distanceKm = 0d;
        reason = string.Empty;
        if (previousLocation is null
            || now - previousLocation.ReceivedAt > IdleJumpWindow)
        {
            return false;
        }

        if (IsTrustedLivePacket(trip, gpsStatus, speedKmh, trackingSource)
            || IsTrustedLiveMovement(previousLocation.Status, previousLocation.SpeedKmh))
        {
            return false;
        }

        distanceKm = CalculateDistanceKm(
            previousLocation.Latitude,
            previousLocation.Longitude,
            incomingLat,
            incomingLng);

        if (trip is null
            && !previousLocation.TripId.HasValue
            && distanceKm > IdleJumpDistanceThresholdKm
            && IsIdleGpsStatus(gpsStatus)
            && IsIdleGpsStatus(previousLocation.Status)
            && speedKmh.GetValueOrDefault() <= MovingSpeedThresholdKmh
            && previousLocation.SpeedKmh.GetValueOrDefault() <= MovingSpeedThresholdKmh)
        {
            reason = "idle-jump";
            return true;
        }

        if (distanceKm <= TeleportJumpDistanceThresholdKm
            || now - previousLocation.ReceivedAt > TeleportJumpWindow)
        {
            return false;
        }

        var elapsedHours = Math.Max(
            (now - previousLocation.ReceivedAt).TotalHours,
            1d / 3600d);
        var impliedSpeedKmh = distanceKm / elapsedHours;
        var maxPlausibleSpeedKmh = ResolveMaxPlausibleSpeedKmh(boat);
        if (impliedSpeedKmh <= maxPlausibleSpeedKmh)
        {
            return false;
        }

        reason = $"teleport-speed-{impliedSpeedKmh:F1}kmh";
        return true;
    }

    private static bool IsTrustedLivePacket(
        Trip? trip,
        string gpsStatus,
        decimal? speedKmh,
        string? trackingSource) =>
        trip is not null
        || IsTrustedLiveMovement(gpsStatus, speedKmh)
        || IsTrustedTrackingSource(trackingSource);

    private static bool IsTrustedLiveMovement(string gpsStatus, decimal? speedKmh) =>
        IsMovingGpsStatus(gpsStatus) || speedKmh is > MovingSpeedThresholdKmh;

    private static bool IsTrustedTrackingSource(string? trackingSource) =>
        string.Equals(trackingSource, "live", StringComparison.OrdinalIgnoreCase)
        || string.Equals(trackingSource, "railway", StringComparison.OrdinalIgnoreCase)
        || string.Equals(trackingSource, "wb-gps", StringComparison.OrdinalIgnoreCase)
        || string.Equals(trackingSource, "gps-live", StringComparison.OrdinalIgnoreCase);

    private static bool IsIdleGpsStatus(string status) =>
        string.Equals(status, "idle", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "docked", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "stopped", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "waiting", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, MovementStatusAtStation, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, IncidentLocationStatus, StringComparison.OrdinalIgnoreCase);

    private static double ResolveMaxPlausibleSpeedKmh(Boat boat)
    {
        var configuredMaxSpeed = boat.MaxSpeedKmh is > 0
            ? boat.MaxSpeedKmh.Value
            : 0;
        return Math.Max(DefaultMaxPlausibleSpeedKmh, configuredMaxSpeed * MaxPlausibleSpeedMultiplier);
    }

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
        if (isMoving && TripStatusTransitionSupport.CanMarkInProgressFromMovement(trip, now))
        {
            return SetTripStatus(
                trip,
                TripStatus.InProgress,
                "GPS xác nhận tàu đã bắt đầu di chuyển.");
        }

        if (trip.TripStatus is TripStatus.Scheduled or TripStatus.Delayed
            && TripStatusTransitionSupport.CanMarkBoarding(trip, currentStop: null, now))
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

    private static TimeSpan ResolveLiveAuthorityWindow(IConfiguration configuration)
    {
        var configuredValue = NormalizeOptionalText(configuration["TRACKING_LIVE_AUTHORITY_SECONDS"])
            ?? NormalizeOptionalText(configuration["Tracking:LiveAuthoritySeconds"]);

        return int.TryParse(configuredValue, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromSeconds(DefaultLiveAuthoritySeconds);
    }

    private static decimal? ResolveRequestSpeedKmh(TrackingLocationRequest request) =>
        request.SpeedKmh ?? request.Speed;

    private static string ResolveRequestStatus(TrackingLocationRequest request) =>
        NormalizeOptionalText(request.Status)
        ?? NormalizeOptionalText(request.MovementStatus)
        ?? "unknown";

    private static string ResolveLatestRejectedReason(
        string? trackingSource,
        DateTimeOffset recordedAt,
        DateTimeOffset now,
        TimeSpan liveAuthorityWindow) =>
        TrackingLatestMergePolicy.IsLiveSource(trackingSource)
            && !TrackingLatestMergePolicy.CanLiveAuthorityOverride(
                trackingSource,
                recordedAt,
                now,
                liveAuthorityWindow)
            ? TrackingLatestMergePolicy.LiveStaleRejectedReason
            : TrackingLatestMergePolicy.OlderRejectedReason;

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ResolveTrackingSource(string? headerValue, string? bodyValue)
    {
        var headerSource = NormalizeOptionalText(headerValue);
        var bodySource = NormalizeOptionalText(bodyValue);
        if (TrackingLatestMergePolicy.IsLiveSource(headerSource)
            || TrackingLatestMergePolicy.IsLiveSource(bodySource))
        {
            return "live";
        }

        return headerSource ?? bodySource;
    }

    private static async Task<Station?> ResolveCurrentStationAsync(
        ApplicationDbContext dbContext,
        string? currentStationCode,
        decimal lat,
        decimal lng,
        CancellationToken cancellationToken)
    {
        var stations = await dbContext.Stations
            .AsNoTracking()
            .Where(x => x.IsWaterbusStation
                && x.Status == StationStatus.Active)
            .ToListAsync(cancellationToken);

        return TrackingCurrentStationResolver.Resolve(
            stations,
            currentStationCode,
            lat,
            lng,
            CurrentStationRadiusKm);
    }

    private static async Task InvalidateLatestLocationCachesAsync(
        IEndpointResponseCache responseCache,
        string boatCode,
        Guid? previousTripId,
        Guid? newTripId,
        CancellationToken cancellationToken)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal)
        {
            "tracking:boats:latest",
            $"tracking:boats:{boatCode.Trim().ToUpperInvariant()}:latest"
        };

        if (previousTripId.HasValue)
        {
            keys.Add($"tracking:trips:{previousTripId.Value:N}:latest");
        }

        if (newTripId.HasValue)
        {
            keys.Add($"tracking:trips:{newTripId.Value:N}:latest");
        }

        foreach (var key in keys)
        {
            await responseCache.RemoveAsync(key, cancellationToken);
        }
    }

    private static BoatLatestLocationDto ToLatestLocationDto(
        BoatLatestLocation location,
        DateTimeOffset now,
        ActiveIncidentTrackingDto? activeIncident,
        int onboardPassengerCount)
    {
        var tripStops = location.Trip?.TripStops
            .OrderBy(x => x.StopOrder)
            .ToArray() ?? [];
        var currentStop = ResolveCurrentStop(tripStops);
        var currentStationId = location.CurrentStationId ?? currentStop?.StationId;
        var currentStationCode = location.CurrentStation?.StationCode ?? currentStop?.Station?.StationCode;
        var currentStationName = location.CurrentStation?.StationName ?? currentStop?.Station?.StationName;
        var lastStopEvent = ResolveLastStopEvent(tripStops);
        var dwellCountdown = TripStatusTransitionSupport.ResolveDwellCountdown(
            location.Trip,
            currentStop,
            now);

        return new(
            location.BoatId,
            location.Boat.Code,
            location.Boat.Name,
            location.Boat.Status.ToString(),
            location.RouteId,
            location.Route?.RouteCode,
            location.TripId,
            location.Trip?.TripCode,
            onboardPassengerCount,
            ResolveTrackingMovementStatus(
                location.Trip,
                tripStops,
                location.Status,
                location.SpeedKmh,
                now),
            currentStationId,
            currentStationCode,
            currentStationName,
            location.NextStationId,
            location.NextStation?.StationCode,
            location.NextStation?.StationName,
            lastStopEvent.Event,
            lastStopEvent.OccurredAt,
            TripStatusTransitionSupport.ResolveScheduledDepartureAt(location.Trip, currentStop),
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
            now - location.ReceivedAt <= TimeSpan.FromSeconds(OnlineThresholdSeconds),
            dwellCountdown,
            CreateBoatImageUrls(location.Boat).FirstOrDefault(),
            CreateBoatImageUrls(location.Boat));
    }

    private static TrackingCurrentLocationResponse? ToCurrentLocationResponse(BoatLatestLocationDto? location) =>
        location is null
            ? null
            : new TrackingCurrentLocationResponse(
                location.BoatCode,
                location.Lat,
                location.Lng,
                location.CurrentStationCode,
                location.CurrentStationName,
                location.MovementStatus,
                location.RecordedAt,
                location.Sequence);

    private static TripBoatDto? ToTripBoatDto(Boat? boat, int capacity)
    {
        if (boat is null)
        {
            return null;
        }

        var imageUrls = CreateBoatImageUrls(boat);
        return new TripBoatDto(
            boat.Id,
            boat.Name,
            boat.Code,
            capacity,
            boat.Status.ToString(),
            imageUrls.FirstOrDefault(),
            imageUrls,
            boat.RegistrationNumber,
            boat.ServiceType.ToString(),
            boat.NumberOfDecks,
            boat.MaxSpeedKmh,
            boat.YearBuilt,
            boat.Description);
    }

    private static IReadOnlyList<string> CreateBoatImageUrls(Boat boat) =>
        boat.ImageUrls.Length > 0
            ? boat.ImageUrls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => url.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToArray()
            : string.IsNullOrWhiteSpace(boat.ImageUrl)
                ? []
                : [boat.ImageUrl.Trim()];

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
        BoatLatestLocationDto payload,
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
        string? CurrentStationCode,
        string? CurrentStationName,
        decimal Lat,
        decimal Lng,
        decimal? Speed,
        decimal? SpeedKmh,
        int? Heading,
        decimal? AccuracyMeters,
        DateTimeOffset RecordedAt,
        long Sequence,
        int? BatteryPercent,
        int? SignalStrength,
        string? GpsFixQuality,
        string? Direction,
        string? MovementStatus,
        string? Status,
        string? Source,
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
        DateTimeOffset ReceivedAt,
        bool LatestUpdated = true,
        string? ReplacedBy = null,
        string? Rejected = null,
        TrackingCurrentLocationResponse? Current = null);

    private sealed record TrackingCurrentLocationResponse(
        string BoatCode,
        decimal Lat,
        decimal Lng,
        string? CurrentStationCode,
        string? CurrentStationName,
        string MovementStatus,
        DateTimeOffset RecordedAt,
        long Sequence);

    private sealed record PreviousLatestLocationSnapshot(
        Guid? RouteId,
        Guid? TripId,
        decimal Latitude,
        decimal Longitude,
        decimal? SpeedKmh,
        string Status,
        string? Source,
        DateTimeOffset? LiveAuthorityUntil,
        DateTimeOffset RecordedAt,
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
        int OnboardPassengerCount,
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
        bool IsOnline,
        TripStopDwellCountdownDto? DwellCountdown = null,
        string? BoatImageUrl = null,
        IReadOnlyList<string>? BoatImageUrls = null);

    public sealed record TripLatestTrackingDto(
        Guid TripId,
        string TripCode,
        string TripStatus,
        int OnboardPassengerCount,
        Guid? BoatId,
        TripBoatDto? Boat,
        BoatLatestLocationDto? LatestLocation,
        bool HasLiveLocationForTrip,
        bool IsBoatOnline);

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
