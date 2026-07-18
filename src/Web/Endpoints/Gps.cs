using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using SaigonWaterbus.Infrastructure.Options;
using WaterbusRoute = SaigonWaterbus.Domain.Entities.Route;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Gps : IEndpointGroup
{
    private const string LiveHookSecretHeaderName = "X-Live-Hook-Secret";
    private const int DefaultScheduleLookAheadMinutes = 120;
    private const int DefaultScheduleLookBehindMinutes = 30;
    private const int MaxScheduleLookAheadMinutes = 24 * 60;
    private const int MaxScheduleLookBehindMinutes = 12 * 60;
    private const double StationMatchThresholdMeters = 500;
    private const double FallbackSpeedKmh = 16;

    private const string StartSessionExample =
        """
        {
          "sessionId": "550e8400-e29b-41d4-a716-446655440000",
          "boatCode": "SURVEY-01",
          "routeCode": "BA-TT",
          "routeName": "Binh An - Thao Dien",
          "startStationId": "550e8400-e29b-41d4-a716-446655440001",
          "endStationId": "550e8400-e29b-41d4-a716-446655440002",
          "plannedLengthMeters": 3500,
          "startedAt": "2026-07-10T13:30:00+07:00"
        }
        """;

    private const string StopSessionExample =
        """
        {
          "stoppedAt": "2026-07-10T13:45:00+07:00",
          "recordedPointCount": 42,
          "status": "completed"
        }
        """;

    private const string SaveRouteExample =
        """
        {
          "sessionId": "550e8400-e29b-41d4-a716-446655440000",
          "routeCode": "BA-TT",
          "routeName": "Binh An - Thao Dien",
          "description": "Captured from GPS survey.",
          "status": "Active",
          "averageSpeedKmh": 16,
          "startStationId": "550e8400-e29b-41d4-a716-446655440001",
          "endStationId": "550e8400-e29b-41d4-a716-446655440002",
          "createReverseRoute": true,
          "reverseRouteCode": "TT-BA",
          "reverseRouteName": "Thao Dien - Binh An",
          "stops": [
            {
              "stationId": "550e8400-e29b-41d4-a716-446655440001",
              "stopOrder": 1,
              "isPickupAllowed": true,
              "isDropoffAllowed": false
            },
            {
              "stationId": "550e8400-e29b-41d4-a716-446655440002",
              "stopOrder": 2,
              "standardTravelMin": 12,
              "isPickupAllowed": false,
              "isDropoffAllowed": true
            }
          ],
          "coordinates": [
            { "lat": 10.7757, "lng": 106.7072, "speedKmh": 16, "sequence": 1, "recordedAt": "2026-07-10T13:30:00+07:00" },
            { "lat": 10.7761, "lng": 106.7080, "speedKmh": 16, "sequence": 2, "recordedAt": "2026-07-10T13:30:05+07:00" }
          ]
        }
        """;

    public static string RoutePrefix => "/api";

    public static string OpenApiTag => "GPS";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost(StartSession, "tracking/sessions/start")
            .AllowAnonymous()
            .WithSummary("Bat dau phien thu GPS")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "GPS device",
                StartSessionExample,
                "Co the gui X-Device-Id hoac deviceId trong body. Neu khong co, backend se tim GPS device active theo boatCode.",
                "routeCode co the la route moi chua ton tai trong DB."));

        group.MapPost(StopSession, "tracking/sessions/{sessionId:guid}/stop")
            .AllowAnonymous()
            .WithSummary("Ket thuc phien thu GPS")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "GPS device",
                StopSessionExample,
                "Tra ve so diem GPS backend da luu cho session; neu chua co diem, fallback ve recordedPointCount client gui len."));

        group.MapPost(SaveRouteFromGps, "routes/from-gps")
            .AllowAnonymous()
            .WithSummary("Luu tuyen tu GPS da thu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "GPS device",
                SaveRouteExample,
                "Neu coordinates rong, backend se lay diem da thu theo sessionId.",
                "Neu gui stops[], backend se luu day du cac ben theo stopOrder; neu khong gui stops[] thi fallback startStationId/endStationId.",
                "Neu khong gui stops[] va khong gui startStationId/endStationId, backend tu tim ben gan diem GPS dau/cuoi trong ban kinh 500m.",
                "BE tu xac dinh routeType: dau/cuoi trung nhau la SightseeingLoop, khac nhau la CharterReference de lam route nguon.",
                "estimatedDurationMin = baseDistanceKm / averageSpeedKmh * 60. Neu khong gui averageSpeedKmh, backend lay trung binh speedKmh cua diem GPS; neu van thieu thi dung 16 km/h.",
                "routeCode phai chua ton tai de tranh ghi de tuyen hien co."));

        group.MapGet(GetDueTripSchedules, "gps/trips/due")
            .AllowAnonymous()
            .WithSummary("GPS lay lich chuyen sap/đang chay de tu dong van hanh")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "GPS service",
                null,
                $"Header bat buoc: {LiveHookSecretHeaderName}.",
                "Query optional: boatCode, lookAheadMinutes, lookBehindMinutes.",
                "Tra ve trip da gan tau, route geometry, stop schedule de GPS tu chon trip dung gio va gui location kem tripId."));

        group.MapPost(CompleteGpsTrip, "gps/trips/{tripId:guid}/complete")
            .AllowAnonymous()
            .WithSummary("GPS bao BE chuyen thuong da chay xong")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "GPS service",
                null,
                $"Header bat buoc: {LiveHookSecretHeaderName}.",
                "Body optional: boatCode, completedAt, note.",
                "BE verify boatCode neu co, sau do set tripStatus Completed."));
    }

    private static async Task<IResult> StartSession(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        [FromHeader(Name = "X-Device-Id")] string? headerDeviceId,
        StartGpsSessionRequest request,
        CancellationToken cancellationToken)
    {
        var errors = ValidateStartSessionRequest(headerDeviceId, request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var deviceResult = await ResolveGpsDeviceAsync(
            dbContext,
            headerDeviceId,
            request.DeviceId,
            request.BoatCode,
            cancellationToken);

        if (deviceResult.Error is not null)
        {
            return deviceResult.Error;
        }

        var gpsDevice = deviceResult.Device!;
        var sessionId = request.SessionId.GetValueOrDefault(Guid.NewGuid());
        var existingSession = await dbContext.GpsTrackingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);

        if (existingSession is not null)
        {
            if (IsRecording(existingSession.Status))
            {
                return Results.Ok(new GpsSessionResponse(existingSession.Id, ToApiStatus(existingSession.Status)));
            }

            return Results.Conflict(new
            {
                message = "sessionId da ton tai va khong con o trang thai recording.",
                sessionId = existingSession.Id,
                status = ToApiStatus(existingSession.Status)
            });
        }

        var route = await ResolveRouteForSessionAsync(
            dbContext,
            request.RouteId,
            request.RouteCode,
            cancellationToken);

        if (route.NotFound)
        {
            return Results.NotFound(new { message = "routeId khong ton tai." });
        }

        var trip = await ResolveTripForSessionAsync(dbContext, request.TripId, cancellationToken);
        if (trip.NotFound)
        {
            return Results.NotFound(new { message = "tripId khong ton tai." });
        }

        if (trip.Value is not null)
        {
            if (trip.Value.BoatId.HasValue && trip.Value.BoatId.Value != gpsDevice.BoatId)
            {
                return Results.BadRequest(new { message = "tripId khong thuoc tau cua thiet bi GPS nay." });
            }

            if (route.Value is not null && trip.Value.RouteId != route.Value.Id)
            {
                return Results.BadRequest(new { message = "tripId khong thuoc route da gui." });
            }
        }

        var stationError = await ValidateStationsAsync(
            dbContext,
            request.StartStationId,
            request.EndStationId,
            cancellationToken);

        if (stationError is not null)
        {
            return stationError;
        }

        var startedAt = request.StartedAt?.ToUniversalTime() ?? timeProvider.GetUtcNow();
        var session = new GpsTrackingSession
        {
            Id = sessionId,
            GpsDeviceId = gpsDevice.Id,
            BoatId = gpsDevice.BoatId,
            RouteId = route.Value?.Id ?? trip.Value?.RouteId,
            RouteCode = NormalizeRouteCode(request.RouteCode) ?? route.Value?.RouteCode,
            RouteName = NormalizeOptionalText(request.RouteName) ?? route.Value?.RouteName,
            PlannedLengthMeters = request.PlannedLengthMeters,
            TripId = trip.Value?.Id,
            StartStationId = request.StartStationId,
            EndStationId = request.EndStationId,
            Mode = "route-survey",
            Status = "Recording",
            StartedAt = startedAt
        };

        dbContext.GpsTrackingSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api/tracking/sessions/{session.Id}",
            new GpsSessionResponse(session.Id, ToApiStatus(session.Status)));
    }

    private static async Task<IResult> StopSession(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        Guid sessionId,
        StopGpsSessionRequest request,
        CancellationToken cancellationToken)
    {
        var errors = ValidateStopSessionRequest(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var session = await dbContext.GpsTrackingSessions
            .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);

        if (session is null)
        {
            return Results.NotFound(new { message = "GPS session khong ton tai." });
        }

        if (session.StoppedAt is null)
        {
            session.StoppedAt = request.StoppedAt?.ToUniversalTime() ?? timeProvider.GetUtcNow();
            session.Status = "Stopped";
        }

        var storedPointCount = await dbContext.GpsTrackPoints
            .CountAsync(x => x.SessionId == session.Id, cancellationToken);
        var recordedPointCount = storedPointCount > 0
            ? storedPointCount
            : request.RecordedPointCount.GetValueOrDefault();

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new StopGpsSessionResponse(
            session.Id,
            ToApiStatus(session.Status),
            recordedPointCount));
    }

    private static async Task<IResult> SaveRouteFromGps(
        ApplicationDbContext dbContext,
        SaveRouteFromGpsRequest request,
        CancellationToken cancellationToken)
    {
        GpsTrackingSession? session = null;
        if (request.SessionId.HasValue)
        {
            session = await dbContext.GpsTrackingSessions
                .Include(x => x.TrackPoints)
                .FirstOrDefaultAsync(x => x.Id == request.SessionId.Value, cancellationToken);

            if (session is null)
            {
                return Results.NotFound(new { message = "GPS session khong ton tai." });
            }
        }

        var routeCode = NormalizeRouteCode(request.RouteCode ?? session?.RouteCode);
        var routeName = NormalizeOptionalText(request.RouteName)
            ?? NormalizeOptionalText(session?.RouteName)
            ?? routeCode;

        var errors = ValidateSaveRouteRequest(request, routeCode, routeName);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        if (await dbContext.Routes.AnyAsync(x => x.RouteCode == routeCode, cancellationToken))
        {
            return Results.Conflict(new { message = "routeCode da ton tai, khong tu dong ghi de tuyen hien co." });
        }

        var coordinateResult = BuildRouteGeometry(request.Coordinates, session);
        if (coordinateResult.Errors.Count > 0)
        {
            return Results.ValidationProblem(coordinateResult.Errors);
        }

        var routeGeometry = coordinateResult.Geometry!;
        var baseDistanceKm = (decimal)Math.Round(CalculateLengthKm(routeGeometry), 2);
        var estimatedDurationMin = EstimateDurationMin(baseDistanceKm, request.AverageSpeedKmh ?? coordinateResult.AverageSpeedKmh);
        var status = NormalizeOptionalText(request.Status) ?? "Active";

        var routeStopResolution = request.Stops is { Count: > 0 }
            ? await ResolveExplicitRouteStopsAsync(
                dbContext,
                request.Stops,
                cancellationToken)
            : await ResolveFallbackRouteStopsAsync(
                dbContext,
                request.StartStationId ?? session?.StartStationId,
                request.EndStationId ?? session?.EndStationId,
                coordinateResult.StartCoordinate!,
                coordinateResult.EndCoordinate!,
                estimatedDurationMin,
                cancellationToken);

        if (routeStopResolution.Error is not null)
        {
            return routeStopResolution.Error;
        }

        var routeType = InferRouteType(routeStopResolution.Stops);
        var reverseRouteRequest = ResolveReverseRouteRequest(request, routeCode!, routeType, routeStopResolution.Stops);
        if (reverseRouteRequest.Errors.Count > 0)
        {
            return Results.ValidationProblem(reverseRouteRequest.Errors);
        }

        if (reverseRouteRequest.Value is not null)
        {
            if (string.Equals(reverseRouteRequest.Value.RouteCode, routeCode, StringComparison.Ordinal))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["reverseRouteCode"] = ["reverseRouteCode phai khac routeCode."]
                });
            }

            if (await dbContext.Routes.AnyAsync(x => x.RouteCode == reverseRouteRequest.Value.RouteCode, cancellationToken))
            {
                return Results.Conflict(new
                {
                    message = "reverseRouteCode da ton tai, khong tu dong ghi de tuyen hien co.",
                    reverseRouteCode = reverseRouteRequest.Value.RouteCode
                });
            }
        }

        var route = new WaterbusRoute
        {
            RouteCode = routeCode!,
            RouteName = routeName!,
            RouteType = routeType,
            Description = NormalizeOptionalText(request.Description),
            BaseDistanceKm = baseDistanceKm,
            EstimatedDurationMin = estimatedDurationMin,
            Status = status,
            IsBookable = RouteTypes.IsBookableByDefault(routeType),
            RouteGeometry = routeGeometry
        };

        dbContext.Routes.Add(route);

        var routeStops = AddRouteStops(dbContext, route.Id, routeStopResolution.Stops);
        CreatedGpsRouteResponse? reverseRouteResponse = null;

        if (reverseRouteRequest.Value is not null)
        {
            var reverseGeometry = ReverseRouteGeometry(routeGeometry);
            var reverseRoute = new WaterbusRoute
            {
                RouteCode = reverseRouteRequest.Value.RouteCode,
                RouteName = reverseRouteRequest.Value.RouteName,
                RouteType = routeType,
                Description = NormalizeOptionalText(request.Description),
                BaseDistanceKm = baseDistanceKm,
                EstimatedDurationMin = estimatedDurationMin,
                Status = status,
                IsBookable = RouteTypes.IsBookableByDefault(routeType),
                RouteGeometry = reverseGeometry
            };

            dbContext.Routes.Add(reverseRoute);
            var reverseRouteStops = AddRouteStops(
                dbContext,
                reverseRoute.Id,
                BuildReverseRouteStops(routeStopResolution.Stops));
            reverseRouteResponse = ToCreatedGpsRouteResponse(
                reverseRoute,
                coordinateResult.PointCount,
                reverseRouteStops);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api/routes/{route.Id}",
            new SaveRouteFromGpsResponse(
                route.Id,
                route.RouteCode,
                route.RouteName,
                route.RouteType,
                route.BaseDistanceKm ?? 0,
                route.EstimatedDurationMin,
                coordinateResult.PointCount,
                route.Status,
                routeStops,
                reverseRouteResponse));
    }

    private static async Task<IResult> GetDueTripSchedules(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        IOptionsMonitor<IncidentGpsHookOptions> gpsHookOptions,
        [FromHeader(Name = LiveHookSecretHeaderName)] string? hookSecret,
        [FromQuery] string? boatCode,
        [FromQuery] int? lookAheadMinutes,
        [FromQuery] int? lookBehindMinutes,
        CancellationToken cancellationToken)
    {
        if (!IsValidLiveHookSecret(gpsHookOptions.CurrentValue.Secret, hookSecret))
        {
            return Results.Unauthorized();
        }

        var validationErrors = ValidateDueTripScheduleRequest(lookAheadMinutes, lookBehindMinutes);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var now = timeProvider.GetUtcNow();
        var resolvedLookAheadMinutes = lookAheadMinutes.GetValueOrDefault(DefaultScheduleLookAheadMinutes);
        var resolvedLookBehindMinutes = lookBehindMinutes.GetValueOrDefault(DefaultScheduleLookBehindMinutes);
        var windowStart = now.AddMinutes(-resolvedLookBehindMinutes);
        var windowEnd = now.AddMinutes(resolvedLookAheadMinutes);
        var normalizedBoatCode = NormalizeRouteCode(boatCode);

        var query = dbContext.Trips
            .AsNoTracking()
            .Include(x => x.Boat)
            .Include(x => x.Route)
                .ThenInclude(x => x.RouteStops)
                    .ThenInclude(x => x.Station)
            .Include(x => x.TripStops)
                .ThenInclude(x => x.Station)
            .Where(x => x.BoatId.HasValue
                && x.Boat != null
                && x.Boat.Status == BoatStatus.Active
                && x.TripStatus != TripStatus.Cancelled
                && x.TripStatus != TripStatus.Completed
                && ((x.DepartureTime >= windowStart && x.DepartureTime <= windowEnd)
                    || x.TripStatus == TripStatus.Boarding
                    || x.TripStatus == TripStatus.InProgress));

        if (!string.IsNullOrWhiteSpace(normalizedBoatCode))
        {
            query = query.Where(x => x.Boat!.Code == normalizedBoatCode);
        }

        var trips = await query
            .OrderBy(x => x.DepartureTime)
            .ThenBy(x => x.TripCode)
            .ToListAsync(cancellationToken);

        return Results.Ok(new GpsDueTripScheduleResponse(
            now,
            windowStart,
            windowEnd,
            trips.Select(ToGpsTripScheduleItem).ToArray()));
    }

    private static async Task<IResult> CompleteGpsTrip(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        IOptionsMonitor<IncidentGpsHookOptions> gpsHookOptions,
        [FromHeader(Name = LiveHookSecretHeaderName)] string? hookSecret,
        Guid tripId,
        CompleteGpsTripRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsValidLiveHookSecret(gpsHookOptions.CurrentValue.Secret, hookSecret))
        {
            return Results.Unauthorized();
        }

        var trip = await dbContext.Trips
            .Include(x => x.Boat)
            .SingleOrDefaultAsync(x => x.Id == tripId, cancellationToken);
        if (trip is null)
        {
            return Results.NotFound(new { message = "Trip không tồn tại." });
        }

        if (!string.IsNullOrWhiteSpace(request.BoatCode)
            && !string.Equals(trip.Boat?.Code, request.BoatCode.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = "boatCode không khớp với tàu của trip." });
        }

        if (trip.TripStatus == TripStatus.Cancelled)
        {
            return Results.BadRequest(new { message = "Trip đã bị hủy, không thể hoàn tất từ GPS." });
        }

        if (trip.TripStatus != TripStatus.Completed)
        {
            var completedAt = request.CompletedAt?.ToUniversalTime() ?? timeProvider.GetUtcNow();
            trip.TripStatus = TripStatus.Completed;
            trip.StatusNote = NormalizeOptionalText(request.Note)
                ?? $"GPS đã hoàn tất chuyến lúc {completedAt:O}.";
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Results.Ok(new GpsTripStatusResponse(
            trip.Id,
            trip.TripCode,
            trip.Boat?.Code,
            trip.TripStatus.ToString(),
            trip.StatusNote));
    }

    private static Dictionary<string, string[]> ValidateStartSessionRequest(
        string? headerDeviceId,
        StartGpsSessionRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(headerDeviceId)
            && !string.IsNullOrWhiteSpace(request.DeviceId)
            && !string.Equals(headerDeviceId.Trim(), request.DeviceId.Trim(), StringComparison.Ordinal))
        {
            errors["deviceId"] = ["deviceId phai trung voi header X-Device-Id."];
        }

        if (string.IsNullOrWhiteSpace(headerDeviceId)
            && string.IsNullOrWhiteSpace(request.DeviceId)
            && string.IsNullOrWhiteSpace(request.BoatCode))
        {
            errors["boatCode"] = ["Can gui X-Device-Id, deviceId hoac boatCode de xac dinh GPS device."];
        }

        if (request.PlannedLengthMeters is < 0)
        {
            errors["plannedLengthMeters"] = ["plannedLengthMeters khong duoc am."];
        }

        if (request.RouteCode?.Length > 50)
        {
            errors["routeCode"] = ["routeCode khong duoc vuot qua 50 ky tu."];
        }

        if (request.RouteName?.Length > 150)
        {
            errors["routeName"] = ["routeName khong duoc vuot qua 150 ky tu."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateDueTripScheduleRequest(
        int? lookAheadMinutes,
        int? lookBehindMinutes)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (lookAheadMinutes is < 1 or > MaxScheduleLookAheadMinutes)
        {
            errors["lookAheadMinutes"] = [$"lookAheadMinutes phai tu 1 den {MaxScheduleLookAheadMinutes}."];
        }

        if (lookBehindMinutes is < 0 or > MaxScheduleLookBehindMinutes)
        {
            errors["lookBehindMinutes"] = [$"lookBehindMinutes phai tu 0 den {MaxScheduleLookBehindMinutes}."];
        }

        return errors;
    }

    private static ReverseRouteResolution ResolveReverseRouteRequest(
        SaveRouteFromGpsRequest request,
        string routeCode,
        string routeType,
        IReadOnlyList<RouteStopDraft> routeStops)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (request.CreateReverseRoute != true)
        {
            return ReverseRouteResolution.Skipped();
        }

        if (routeType == RouteTypes.SightseeingLoop)
        {
            errors["createReverseRoute"] = ["SightseeingLoop khong can tao route chieu ve."];
            return ReverseRouteResolution.Invalid(errors);
        }

        var orderedStops = routeStops
            .OrderBy(x => x.StopOrder)
            .ToList();
        if (orderedStops.Count < 2)
        {
            errors["createReverseRoute"] = ["Can it nhat 2 stops de tao route chieu ve."];
            return ReverseRouteResolution.Invalid(errors);
        }

        if (orderedStops[0].Station.StationId == orderedStops[^1].Station.StationId)
        {
            errors["createReverseRoute"] = ["Route co ben dau/cuoi trung nhau khong can tao route chieu ve."];
            return ReverseRouteResolution.Invalid(errors);
        }

        var reverseRouteCode = NormalizeRouteCode(request.ReverseRouteCode)
            ?? BuildDefaultReverseRouteCode(routeCode, orderedStops);
        if (reverseRouteCode.Length > 50)
        {
            errors["reverseRouteCode"] = ["reverseRouteCode khong duoc vuot qua 50 ky tu."];
        }

        var reverseRouteName = NormalizeOptionalText(request.ReverseRouteName)
            ?? $"{orderedStops[^1].Station.StationName} - {orderedStops[0].Station.StationName}";
        if (reverseRouteName.Length > 150)
        {
            errors["reverseRouteName"] = ["reverseRouteName khong duoc vuot qua 150 ky tu."];
        }

        return errors.Count > 0
            ? ReverseRouteResolution.Invalid(errors)
            : ReverseRouteResolution.Valid(new ReverseRouteRequest(reverseRouteCode, reverseRouteName));
    }

    private static string BuildDefaultReverseRouteCode(
        string routeCode,
        IReadOnlyList<RouteStopDraft> orderedStops)
    {
        var stationCodeBased = NormalizeRouteCode(
            $"{orderedStops[^1].Station.StationCode}-{orderedStops[0].Station.StationCode}");
        if (!string.IsNullOrWhiteSpace(stationCodeBased) && stationCodeBased.Length <= 50)
        {
            return stationCodeBased;
        }

        return routeCode.Length <= 46
            ? $"{routeCode}-REV"
            : $"{routeCode[..46]}-REV";
    }

    private static void ValidateRouteStops(
        IReadOnlyList<GpsRouteStopRequest> stops,
        Dictionary<string, string[]> errors)
    {
        if (stops.Count < 2)
        {
            errors["stops"] = ["Can gui it nhat 2 stops neu dung stops[]."];
            return;
        }

        var hasExplicitOrder = stops.Any(x => x.StopOrder.HasValue);
        var assignedOrders = new List<int>();
        for (var i = 0; i < stops.Count; i++)
        {
            var stop = stops[i];
            if (stop.StationId == Guid.Empty)
            {
                errors[$"stops[{i}].stationId"] = ["stationId la bat buoc."];
            }

            if (stop.StopOrder is <= 0)
            {
                errors[$"stops[{i}].stopOrder"] = ["stopOrder phai lon hon 0."];
            }

            if (stop.StandardTravelMin is < 0)
            {
                errors[$"stops[{i}].standardTravelMin"] = ["standardTravelMin khong duoc am."];
            }

            assignedOrders.Add(stop.StopOrder ?? i + 1);
        }

        if (hasExplicitOrder && stops.Any(x => !x.StopOrder.HasValue))
        {
            errors["stops"] = ["Neu dung stopOrder thi tat ca stops phai co stopOrder."];
        }

        if (assignedOrders.Count != assignedOrders.Distinct().Count())
        {
            errors["stops.stopOrder"] = ["stopOrder khong duoc trung nhau."];
        }

    }

    private static Dictionary<string, string[]> ValidateStopSessionRequest(StopGpsSessionRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (request.RecordedPointCount is < 0)
        {
            errors["recordedPointCount"] = ["recordedPointCount khong duoc am."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateSaveRouteRequest(
        SaveRouteFromGpsRequest request,
        string? routeCode,
        string? routeName)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(routeCode))
        {
            errors["routeCode"] = ["routeCode la bat buoc neu session khong co routeCode."];
        }

        if (routeCode?.Length > 50)
        {
            errors["routeCode"] = ["routeCode khong duoc vuot qua 50 ky tu."];
        }

        if (string.IsNullOrWhiteSpace(routeName))
        {
            errors["routeName"] = ["routeName la bat buoc neu session khong co routeName."];
        }

        if (routeName?.Length > 150)
        {
            errors["routeName"] = ["routeName khong duoc vuot qua 150 ky tu."];
        }

        if (request.Description?.Length > 1000)
        {
            errors["description"] = ["description khong duoc vuot qua 1000 ky tu."];
        }

        var status = NormalizeOptionalText(request.Status);
        if (status?.Length > 30)
        {
            errors["status"] = ["status khong duoc vuot qua 30 ky tu."];
        }
        else if (status is not null
            && !string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status, "Inactive", StringComparison.OrdinalIgnoreCase))
        {
            errors["status"] = ["status hop le: Active hoac Inactive."];
        }

        if (request.AverageSpeedKmh is <= 0)
        {
            errors["averageSpeedKmh"] = ["averageSpeedKmh phai lon hon 0."];
        }

        if (request.Stops is { Count: > 0 })
        {
            ValidateRouteStops(request.Stops, errors);
        }

        return errors;
    }

    private static async Task<GpsDeviceResolution> ResolveGpsDeviceAsync(
        ApplicationDbContext dbContext,
        string? headerDeviceId,
        string? bodyDeviceId,
        string? boatCode,
        CancellationToken cancellationToken)
    {
        var deviceId = NormalizeOptionalText(headerDeviceId) ?? NormalizeOptionalText(bodyDeviceId);
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            var gpsDevice = await dbContext.GpsDevices
                .Include(x => x.Boat)
                .FirstOrDefaultAsync(x => x.DeviceId == deviceId && x.IsActive, cancellationToken);

            if (gpsDevice is null)
            {
                return GpsDeviceResolution.Failed(
                    Results.NotFound(new { message = "GPS device chua duoc dang ky hoac da bi khoa." }));
            }

            if (!string.IsNullOrWhiteSpace(boatCode)
                && !string.Equals(boatCode.Trim(), gpsDevice.Boat.Code, StringComparison.OrdinalIgnoreCase))
            {
                return GpsDeviceResolution.Failed(
                    Results.BadRequest(new { message = "boatCode khong khop voi thiet bi GPS da dang ky." }));
            }

            return GpsDeviceResolution.Success(gpsDevice);
        }

        var normalizedBoatCode = NormalizeOptionalText(boatCode);
        var deviceByBoat = await dbContext.GpsDevices
            .Include(x => x.Boat)
            .Where(x => x.IsActive && x.Boat.Code == normalizedBoatCode)
            .OrderBy(x => x.DeviceId)
            .FirstOrDefaultAsync(cancellationToken);

        return deviceByBoat is null
            ? GpsDeviceResolution.Failed(
                Results.NotFound(new { message = "Khong tim thay GPS device active theo boatCode." }))
            : GpsDeviceResolution.Success(deviceByBoat);
    }

    private static async Task<RouteLookupResult> ResolveRouteForSessionAsync(
        ApplicationDbContext dbContext,
        Guid? routeId,
        string? routeCode,
        CancellationToken cancellationToken)
    {
        if (routeId.HasValue)
        {
            var route = await dbContext.Routes
                .FirstOrDefaultAsync(x => x.Id == routeId.Value, cancellationToken);
            return route is null
                ? RouteLookupResult.Missing()
                : RouteLookupResult.Found(route);
        }

        var normalizedRouteCode = NormalizeRouteCode(routeCode);
        if (!string.IsNullOrWhiteSpace(normalizedRouteCode))
        {
            var route = await dbContext.Routes
                .FirstOrDefaultAsync(x => x.RouteCode == normalizedRouteCode, cancellationToken);
            if (route is not null)
            {
                return RouteLookupResult.Found(route);
            }
        }

        return RouteLookupResult.Empty();
    }

    private static async Task<TripLookupResult> ResolveTripForSessionAsync(
        ApplicationDbContext dbContext,
        Guid? tripId,
        CancellationToken cancellationToken)
    {
        if (!tripId.HasValue)
        {
            return TripLookupResult.Empty();
        }

        var trip = await dbContext.Trips
            .FirstOrDefaultAsync(x => x.Id == tripId.Value, cancellationToken);

        return trip is null
            ? TripLookupResult.Missing()
            : TripLookupResult.Found(trip);
    }

    private static async Task<IResult?> ValidateStationsAsync(
        ApplicationDbContext dbContext,
        Guid? startStationId,
        Guid? endStationId,
        CancellationToken cancellationToken)
    {
        var stationIds = new[] { startStationId, endStationId }
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();

        if (stationIds.Length == 0)
        {
            return null;
        }

        var existingStationIds = await dbContext.Stations
            .Where(x => stationIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var missingStationIds = stationIds.Except(existingStationIds).ToArray();
        return missingStationIds.Length == 0
            ? null
            : Results.NotFound(new { message = "startStationId/endStationId khong ton tai.", stationIds = missingStationIds });
    }

    private static RouteGeometryBuildResult BuildRouteGeometry(
        IReadOnlyList<GpsRouteCoordinateRequest>? requestCoordinates,
        GpsTrackingSession? session)
    {
        var source = requestCoordinates is { Count: > 0 }
            ? requestCoordinates.Select((x, index) => CapturedCoordinate.FromRequest(x, index))
            : session?.TrackPoints.Select((x, index) => CapturedCoordinate.FromTrackPoint(x, index))
                ?? [];

        var coordinates = source
            .OrderBy(x => x.Sequence ?? long.MaxValue)
            .ThenBy(x => x.RecordedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.Index)
            .ToArray();

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (coordinates.Length < 2)
        {
            errors["coordinates"] = ["Can it nhat 2 diem GPS de tao route_geometry."];
            return RouteGeometryBuildResult.Invalid(errors);
        }

        for (var i = 0; i < coordinates.Length; i++)
        {
            if (coordinates[i].Latitude is < -90m or > 90m)
            {
                errors[$"coordinates[{i}].lat"] = ["lat phai nam trong khoang -90 den 90."];
            }

            if (coordinates[i].Longitude is < -180m or > 180m)
            {
                errors[$"coordinates[{i}].lng"] = ["lng phai nam trong khoang -180 den 180."];
            }

            if (coordinates[i].SpeedKmh is < 0m)
            {
                errors[$"coordinates[{i}].speedKmh"] = ["speedKmh khong duoc am."];
            }
        }

        if (errors.Count > 0)
        {
            return RouteGeometryBuildResult.Invalid(errors);
        }

        var normalized = new List<Coordinate>();
        foreach (var coordinate in coordinates)
        {
            var next = new Coordinate((double)coordinate.Longitude, (double)coordinate.Latitude);
            if (normalized.Count == 0 || DistanceMeters(normalized[^1], next) > 1)
            {
                normalized.Add(next);
            }
        }

        if (normalized.Count < 2)
        {
            errors["coordinates"] = ["Can it nhat 2 diem GPS khac nhau de tao route_geometry."];
            return RouteGeometryBuildResult.Invalid(errors);
        }

        var speedSamples = coordinates
            .Where(x => x.SpeedKmh is > 0m)
            .Select(x => x.SpeedKmh!.Value)
            .ToArray();
        var averageSpeedKmh = speedSamples.Length == 0 ? null : (decimal?)speedSamples.Average();

        return RouteGeometryBuildResult.Valid(
            new LineString(normalized.ToArray()) { SRID = 4326 },
            normalized.Count,
            normalized[0],
            normalized[^1],
            averageSpeedKmh);
    }

    private static async Task<RouteStopListResolution> ResolveExplicitRouteStopsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyList<GpsRouteStopRequest> requestStops,
        CancellationToken cancellationToken)
    {
        var orderedStops = requestStops
            .Select((stop, index) => new
            {
                Stop = stop,
                StopOrder = stop.StopOrder ?? index + 1,
                Index = index
            })
            .OrderBy(x => x.StopOrder)
            .ThenBy(x => x.Index)
            .ToList();

        var stationIds = orderedStops
            .Select(x => x.Stop.StationId)
            .Distinct()
            .ToArray();

        var stations = await dbContext.Stations
            .AsNoTracking()
            .Where(x => stationIds.Contains(x.Id))
            .Select(x => new StationSummary(x.Id, x.StationCode, x.StationName, x.Latitude, x.Longitude))
            .ToListAsync(cancellationToken);

        var stationsById = stations.ToDictionary(x => x.StationId);
        var missingStationIds = stationIds.Except(stationsById.Keys).ToArray();
        if (missingStationIds.Length > 0)
        {
            return RouteStopListResolution.Failed(Results.NotFound(new
            {
                message = "stops[].stationId khong ton tai.",
                stationIds = missingStationIds
            }));
        }

        var routeStops = new List<RouteStopDraft>();
        for (var i = 0; i < orderedStops.Count; i++)
        {
            var requestStop = orderedStops[i].Stop;
            routeStops.Add(new RouteStopDraft(
                stationsById[requestStop.StationId],
                orderedStops[i].StopOrder,
                requestStop.StandardTravelMin,
                requestStop.IsPickupAllowed ?? i < orderedStops.Count - 1,
                requestStop.IsDropoffAllowed ?? i > 0));
        }

        return RouteStopListResolution.Success(routeStops);
    }

    private static async Task<RouteStopListResolution> ResolveFallbackRouteStopsAsync(
        ApplicationDbContext dbContext,
        Guid? requestedStartStationId,
        Guid? requestedEndStationId,
        Coordinate startCoordinate,
        Coordinate endCoordinate,
        int? estimatedDurationMin,
        CancellationToken cancellationToken)
    {
        var stationResolution = await ResolveRouteStopStationsAsync(
            dbContext,
            requestedStartStationId,
            requestedEndStationId,
            startCoordinate,
            endCoordinate,
            cancellationToken);

        if (stationResolution.Error is not null)
        {
            return RouteStopListResolution.Failed(stationResolution.Error);
        }

        return RouteStopListResolution.Success(BuildFallbackRouteStops(
            stationResolution.StartStation,
            stationResolution.EndStation,
            estimatedDurationMin));
    }

    private static async Task<RouteStopStationResolution> ResolveRouteStopStationsAsync(
        ApplicationDbContext dbContext,
        Guid? requestedStartStationId,
        Guid? requestedEndStationId,
        Coordinate startCoordinate,
        Coordinate endCoordinate,
        CancellationToken cancellationToken)
    {
        var requestedStationIds = new[] { requestedStartStationId, requestedEndStationId }
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();

        var requestedStations = requestedStationIds.Length == 0
            ? []
            : await dbContext.Stations
                .AsNoTracking()
                .Where(x => requestedStationIds.Contains(x.Id))
                .Select(x => new StationSummary(x.Id, x.StationCode, x.StationName, x.Latitude, x.Longitude))
                .ToListAsync(cancellationToken);

        var missingStationIds = requestedStationIds.Except(requestedStations.Select(x => x.StationId)).ToArray();
        if (missingStationIds.Length > 0)
        {
            return RouteStopStationResolution.Failed(Results.NotFound(new
            {
                message = "startStationId/endStationId khong ton tai.",
                stationIds = missingStationIds
            }));
        }

        var startStation = requestedStartStationId.HasValue
            ? requestedStations.First(x => x.StationId == requestedStartStationId.Value)
            : null;
        var endStation = requestedEndStationId.HasValue
            ? requestedStations.First(x => x.StationId == requestedEndStationId.Value)
            : null;

        if (startStation is not null && endStation is not null)
        {
            return RouteStopStationResolution.Success(startStation, endStation);
        }

        var stationCandidates = await dbContext.Stations
            .AsNoTracking()
            .Where(x => x.IsWaterbusStation
                && x.Status == StationStatus.Active
                && x.Latitude.HasValue
                && x.Longitude.HasValue)
            .Select(x => new StationSummary(x.Id, x.StationCode, x.StationName, x.Latitude, x.Longitude))
            .ToListAsync(cancellationToken);

        startStation ??= FindNearestStation(startCoordinate, stationCandidates);
        endStation ??= FindNearestStation(endCoordinate, stationCandidates);

        return RouteStopStationResolution.Success(startStation, endStation);
    }

    private static StationSummary? FindNearestStation(
        Coordinate coordinate,
        IReadOnlyList<StationSummary> stationCandidates) =>
        stationCandidates
            .Select(station => new
            {
                Station = station,
                DistanceMeters = HaversineMeters(
                    coordinate.Y,
                    coordinate.X,
                    (double)station.Latitude!.Value,
                    (double)station.Longitude!.Value)
            })
            .Where(x => x.DistanceMeters <= StationMatchThresholdMeters)
            .OrderBy(x => x.DistanceMeters)
            .FirstOrDefault()
            ?.Station;

    private static IReadOnlyList<RouteStopDraft> BuildFallbackRouteStops(
        StationSummary? startStation,
        StationSummary? endStation,
        int? estimatedDurationMin)
    {
        var stops = new List<RouteStopDraft>();

        if (startStation is not null)
        {
            stops.Add(new RouteStopDraft(
                startStation,
                1,
                null,
                true,
                false));
        }

        if (endStation is not null)
        {
            stops.Add(new RouteStopDraft(
                endStation,
                startStation is not null ? 2 : 1,
                startStation is not null ? estimatedDurationMin : null,
                false,
                true));
        }

        return stops;
    }

    private static IReadOnlyList<RouteStopDraft> BuildReverseRouteStops(
        IReadOnlyList<RouteStopDraft> routeStops)
    {
        var reversedStops = routeStops
            .OrderByDescending(x => x.StopOrder)
            .ToList();
        var stops = new List<RouteStopDraft>();

        for (var i = 0; i < reversedStops.Count; i++)
        {
            stops.Add(new RouteStopDraft(
                reversedStops[i].Station,
                i + 1,
                i == 0 ? null : reversedStops[i - 1].StandardTravelMin,
                i < reversedStops.Count - 1,
                i > 0));
        }

        return stops;
    }

    private static LineString ReverseRouteGeometry(LineString routeGeometry)
    {
        var coordinates = routeGeometry.Coordinates
            .Reverse()
            .Select(coordinate => new Coordinate(coordinate.X, coordinate.Y))
            .ToArray();

        return new LineString(coordinates) { SRID = routeGeometry.SRID };
    }

    private static IReadOnlyList<GpsRouteStopResponse> AddRouteStops(
        ApplicationDbContext dbContext,
        Guid routeId,
        IReadOnlyList<RouteStopDraft> routeStopDrafts)
    {
        var stops = new List<GpsRouteStopResponse>();

        foreach (var draft in routeStopDrafts)
        {
            var routeStop = new RouteStop
            {
                RouteId = routeId,
                StationId = draft.Station.StationId,
                StopOrder = draft.StopOrder,
                StandardTravelMin = draft.StandardTravelMin,
                IsPickupAllowed = draft.IsPickupAllowed,
                IsDropoffAllowed = draft.IsDropoffAllowed
            };

            dbContext.RouteStops.Add(routeStop);
            stops.Add(ToRouteStopResponse(routeStop, draft.Station));
        }

        return stops;
    }

    private static GpsRouteStopResponse ToRouteStopResponse(RouteStop routeStop, StationSummary station) =>
        new(
            routeStop.Id,
            station.StationId,
            station.StationCode,
            station.StationName,
            routeStop.StopOrder,
            routeStop.StandardTravelMin,
            routeStop.IsPickupAllowed,
            routeStop.IsDropoffAllowed);

    private static CreatedGpsRouteResponse ToCreatedGpsRouteResponse(
        WaterbusRoute route,
        int pointCount,
        IReadOnlyList<GpsRouteStopResponse> routeStops) =>
        new(
            route.Id,
            route.RouteCode,
            route.RouteName,
            route.RouteType,
            route.BaseDistanceKm ?? 0,
            route.EstimatedDurationMin,
            pointCount,
            route.Status,
            routeStops);

    private static string InferRouteType(IReadOnlyList<RouteStopDraft> routeStops)
    {
        var orderedStops = routeStops
            .OrderBy(x => x.StopOrder)
            .ToList();

        return orderedStops.Count >= 2
            && orderedStops[0].Station.StationId == orderedStops[^1].Station.StationId
            ? RouteTypes.SightseeingLoop
            : RouteTypes.CharterReference;
    }

    private static bool IsRecording(string status) =>
        string.Equals(status, "Recording", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "recording", StringComparison.OrdinalIgnoreCase);

    private static string ToApiStatus(string status) =>
        string.IsNullOrWhiteSpace(status) ? "unknown" : status.Trim().ToLowerInvariant();

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeRouteCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static bool IsValidLiveHookSecret(string? expectedSecret, string? actualSecret)
    {
        if (string.IsNullOrWhiteSpace(expectedSecret) || string.IsNullOrWhiteSpace(actualSecret))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expectedSecret.Trim());
        var actualBytes = Encoding.UTF8.GetBytes(actualSecret.Trim());
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static GpsTripScheduleItemResponse ToGpsTripScheduleItem(Trip trip) =>
        new(
            trip.Id,
            trip.TripCode,
            trip.TripType,
            trip.BoatId!.Value,
            trip.Boat!.Code,
            trip.Boat.Name,
            trip.RouteId,
            trip.Route.RouteCode,
            trip.Route.RouteName,
            trip.Route.RouteType,
            trip.OperatingDate,
            trip.DepartureTime,
            trip.ArrivalTime,
            trip.TripStatus.ToString(),
            trip.StatusNote,
            trip.Route.BaseDistanceKm,
            trip.Route.EstimatedDurationMin,
            ToRouteGeometryResponse(trip.Route.RouteGeometry),
            ToGpsTripStopResponses(trip));

    private static IReadOnlyList<double[]>? ToRouteGeometryResponse(LineString? routeGeometry) =>
        routeGeometry is null
            ? null
            : routeGeometry.Coordinates
                .Select(coordinate => new[] { coordinate.X, coordinate.Y })
                .ToArray();

    private static IReadOnlyList<GpsTripScheduleStopResponse> ToGpsTripStopResponses(Trip trip)
    {
        if (trip.TripStops.Count > 0)
        {
            return trip.TripStops
                .OrderBy(x => x.StopOrder)
                .Select(x => new GpsTripScheduleStopResponse(
                    x.Id,
                    x.StationId,
                    x.Station.StationCode,
                    x.Station.StationName,
                    x.StopOrder,
                    x.PlannedArrivalTime,
                    x.PlannedDepartureTime,
                    x.Station.Latitude,
                    x.Station.Longitude))
                .ToArray();
        }

        return trip.Route.RouteStops
            .OrderBy(x => x.StopOrder)
            .Select(x => new GpsTripScheduleStopResponse(
                null,
                x.StationId,
                x.Station.StationCode,
                x.Station.StationName,
                x.StopOrder,
                null,
                null,
                x.Station.Latitude,
                x.Station.Longitude))
            .ToArray();
    }

    private static double CalculateLengthKm(LineString line)
    {
        double total = 0;
        for (var i = 0; i < line.NumPoints - 1; i++)
        {
            total += DistanceMeters(line.GetCoordinateN(i), line.GetCoordinateN(i + 1));
        }

        return total / 1000.0;
    }

    private static int EstimateDurationMin(decimal distanceKm, decimal? averageSpeedKmh)
    {
        var speedKmh = averageSpeedKmh is > 0m ? (double)averageSpeedKmh.Value : FallbackSpeedKmh;
        return Math.Max(1, (int)Math.Ceiling((double)distanceKm / speedKmh * 60));
    }

    private static double DistanceMeters(Coordinate a, Coordinate b) =>
        HaversineMeters(a.Y, a.X, b.Y, b.X);

    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusMeters = 6_371_000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var firstLatitude = lat1 * Math.PI / 180;
        var secondLatitude = lat2 * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(firstLatitude) * Math.Cos(secondLatitude)
            * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return earthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    public sealed record StartGpsSessionRequest(
        Guid? SessionId,
        string? DeviceId,
        string? BoatCode,
        Guid? RouteId,
        string? RouteCode,
        string? RouteName,
        Guid? TripId,
        Guid? StartStationId,
        Guid? EndStationId,
        decimal? PlannedLengthMeters,
        DateTimeOffset? StartedAt);

    public sealed record StopGpsSessionRequest(
        DateTimeOffset? StoppedAt,
        int? RecordedPointCount,
        string? Status);

    public sealed record SaveRouteFromGpsRequest(
        Guid? SessionId,
        string? RouteCode,
        string? RouteName,
        string? Description,
        string? Status,
        Guid? StartStationId,
        Guid? EndStationId,
        decimal? AverageSpeedKmh,
        bool? CreateReverseRoute,
        string? ReverseRouteCode,
        string? ReverseRouteName,
        IReadOnlyList<GpsRouteStopRequest>? Stops,
        IReadOnlyList<GpsRouteCoordinateRequest>? Coordinates);

    public sealed record CompleteGpsTripRequest(
        string? BoatCode,
        DateTimeOffset? CompletedAt,
        string? Note);

    public sealed record GpsRouteStopRequest(
        Guid StationId,
        int? StopOrder,
        int? StandardTravelMin,
        bool? IsPickupAllowed,
        bool? IsDropoffAllowed);

    public sealed record GpsRouteCoordinateRequest(
        decimal Lat,
        decimal Lng,
        decimal? SpeedKmh,
        long? Sequence,
        DateTimeOffset? RecordedAt);

    private sealed record GpsSessionResponse(Guid SessionId, string Status);

    private sealed record GpsDueTripScheduleResponse(
        DateTimeOffset ServerTime,
        DateTimeOffset WindowStart,
        DateTimeOffset WindowEnd,
        IReadOnlyList<GpsTripScheduleItemResponse> Trips);

    private sealed record GpsTripScheduleItemResponse(
        Guid TripId,
        string TripCode,
        string TripType,
        Guid BoatId,
        string BoatCode,
        string BoatName,
        Guid RouteId,
        string RouteCode,
        string RouteName,
        string RouteType,
        DateOnly OperatingDate,
        DateTimeOffset DepartureTime,
        DateTimeOffset ArrivalTime,
        string TripStatus,
        string? StatusNote,
        decimal? BaseDistanceKm,
        int? EstimatedDurationMin,
        IReadOnlyList<double[]>? RouteGeometry,
        IReadOnlyList<GpsTripScheduleStopResponse> Stops);

    private sealed record GpsTripScheduleStopResponse(
        Guid? TripStopId,
        Guid StationId,
        string StationCode,
        string StationName,
        int StopOrder,
        DateTimeOffset? PlannedArrivalTime,
        DateTimeOffset? PlannedDepartureTime,
        decimal? Latitude,
        decimal? Longitude);

    private sealed record GpsTripStatusResponse(
        Guid TripId,
        string TripCode,
        string? BoatCode,
        string TripStatus,
        string? StatusNote);

    private sealed record StopGpsSessionResponse(Guid SessionId, string Status, int RecordedPointCount);

    private sealed record CreatedGpsRouteResponse(
        Guid RouteId,
        string RouteCode,
        string RouteName,
        string RouteType,
        decimal BaseDistanceKm,
        int? EstimatedDurationMin,
        int PointCount,
        string Status,
        IReadOnlyList<GpsRouteStopResponse> Stops);

    private sealed record SaveRouteFromGpsResponse(
        Guid RouteId,
        string RouteCode,
        string RouteName,
        string RouteType,
        decimal BaseDistanceKm,
        int? EstimatedDurationMin,
        int PointCount,
        string Status,
        IReadOnlyList<GpsRouteStopResponse> Stops,
        CreatedGpsRouteResponse? ReverseRoute);

    private sealed record GpsRouteStopResponse(
        Guid RouteStopId,
        Guid StationId,
        string StationCode,
        string StationName,
        int StopOrder,
        int? StandardTravelMin,
        bool IsPickupAllowed,
        bool IsDropoffAllowed);

    private sealed record CapturedCoordinate(
        decimal Latitude,
        decimal Longitude,
        decimal? SpeedKmh,
        long? Sequence,
        DateTimeOffset? RecordedAt,
        int Index)
    {
        public static CapturedCoordinate FromRequest(GpsRouteCoordinateRequest request, int index) =>
            new(request.Lat, request.Lng, request.SpeedKmh, request.Sequence, request.RecordedAt?.ToUniversalTime(), index);

        public static CapturedCoordinate FromTrackPoint(GpsTrackPoint point, int index) =>
            new(point.Latitude, point.Longitude, point.SpeedKmh, point.Sequence, point.RecordedAt, index);
    }

    private sealed record GpsDeviceResolution(GpsDevice? Device, IResult? Error)
    {
        public static GpsDeviceResolution Success(GpsDevice device) => new(device, null);

        public static GpsDeviceResolution Failed(IResult error) => new(null, error);
    }

    private sealed record RouteLookupResult(WaterbusRoute? Value, bool NotFound)
    {
        public static RouteLookupResult Empty() => new(null, false);

        public static RouteLookupResult Missing() => new(null, true);

        public static RouteLookupResult Found(WaterbusRoute route) => new(route, false);
    }

    private sealed record TripLookupResult(Trip? Value, bool NotFound)
    {
        public static TripLookupResult Empty() => new(null, false);

        public static TripLookupResult Missing() => new(null, true);

        public static TripLookupResult Found(Trip trip) => new(trip, false);
    }

    private sealed record StationSummary(
        Guid StationId,
        string StationCode,
        string StationName,
        decimal? Latitude,
        decimal? Longitude);

    private sealed record ReverseRouteRequest(
        string RouteCode,
        string RouteName);

    private sealed record ReverseRouteResolution(
        ReverseRouteRequest? Value,
        Dictionary<string, string[]> Errors)
    {
        public static ReverseRouteResolution Skipped() => new(null, new Dictionary<string, string[]>());

        public static ReverseRouteResolution Valid(ReverseRouteRequest value) =>
            new(value, new Dictionary<string, string[]>());

        public static ReverseRouteResolution Invalid(Dictionary<string, string[]> errors) => new(null, errors);
    }

    private sealed record RouteStopDraft(
        StationSummary Station,
        int StopOrder,
        int? StandardTravelMin,
        bool IsPickupAllowed,
        bool IsDropoffAllowed);

    private sealed record RouteStopListResolution(
        IReadOnlyList<RouteStopDraft> Stops,
        IResult? Error)
    {
        public static RouteStopListResolution Success(IReadOnlyList<RouteStopDraft> stops) =>
            new(stops, null);

        public static RouteStopListResolution Failed(IResult error) => new([], error);
    }

    private sealed record RouteStopStationResolution(
        StationSummary? StartStation,
        StationSummary? EndStation,
        IResult? Error)
    {
        public static RouteStopStationResolution Success(StationSummary? startStation, StationSummary? endStation) =>
            new(startStation, endStation, null);

        public static RouteStopStationResolution Failed(IResult error) => new(null, null, error);
    }

    private sealed record RouteGeometryBuildResult(
        LineString? Geometry,
        int PointCount,
        Coordinate? StartCoordinate,
        Coordinate? EndCoordinate,
        decimal? AverageSpeedKmh,
        Dictionary<string, string[]> Errors)
    {
        public static RouteGeometryBuildResult Valid(
            LineString geometry,
            int pointCount,
            Coordinate startCoordinate,
            Coordinate endCoordinate,
            decimal? averageSpeedKmh) =>
            new(
                geometry,
                pointCount,
                startCoordinate,
                endCoordinate,
                averageSpeedKmh,
                new Dictionary<string, string[]>());

        public static RouteGeometryBuildResult Invalid(Dictionary<string, string[]> errors) =>
            new(null, 0, null, null, null, errors);
    }
}
