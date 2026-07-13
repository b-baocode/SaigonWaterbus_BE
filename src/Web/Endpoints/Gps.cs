using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using WaterbusRoute = SaigonWaterbus.Domain.Entities.Route;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Gps : IEndpointGroup
{
    private const double StationMatchThresholdMeters = 500;
    private const double FallbackSpeedKmh = 16;

    private const string StartSessionExample =
        """
        {
          "sessionId": "550e8400-e29b-41d4-a716-446655440000",
          "boatCode": "SURVEY-01",
          "routeCode": "BA-TT",
          "routeName": "Binh An - Thao Dien",
          "routeType": "Regular",
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
          "routeType": "Regular",
          "description": "Captured from GPS survey.",
          "status": "Active",
          "isBookable": true,
          "averageSpeedKmh": 16,
          "startStationId": "550e8400-e29b-41d4-a716-446655440001",
          "endStationId": "550e8400-e29b-41d4-a716-446655440002",
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
                "estimatedDurationMin = baseDistanceKm / averageSpeedKmh * 60. Neu khong gui averageSpeedKmh, backend lay trung binh speedKmh cua diem GPS; neu van thieu thi dung 16 km/h.",
                "routeCode phai chua ton tai de tranh ghi de tuyen hien co."));
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
        var routeType = RouteTypes.Normalize(request.RouteType);

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

        var route = new WaterbusRoute
        {
            RouteCode = routeCode!,
            RouteName = routeName!,
            RouteType = routeType,
            Description = NormalizeOptionalText(request.Description),
            BaseDistanceKm = baseDistanceKm,
            EstimatedDurationMin = estimatedDurationMin,
            Status = status,
            IsBookable = request.IsBookable ?? routeType != RouteTypes.CharterReference,
            RouteGeometry = routeGeometry
        };

        dbContext.Routes.Add(route);

        var routeStops = AddRouteStops(dbContext, route.Id, routeStopResolution.Stops);

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
                route.IsBookable,
                routeStops));
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

        if (!string.IsNullOrWhiteSpace(request.RouteType) && !RouteTypes.IsValid(request.RouteType))
        {
            errors["routeType"] = ["routeType hop le: Regular, SightseeingLoop, CharterReference."];
        }

        if (request.StartStationId.HasValue
            && request.EndStationId.HasValue
            && request.StartStationId.Value == request.EndStationId.Value
            && RouteTypes.Normalize(request.RouteType) != RouteTypes.SightseeingLoop)
        {
            errors["endStationId"] = ["startStationId/endStationId chi duoc trung nhau khi routeType=SightseeingLoop."];
        }

        return errors;
    }

    private static void ValidateRouteStops(
        IReadOnlyList<GpsRouteStopRequest> stops,
        string routeType,
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

        var orderedStops = stops
            .Select((stop, index) => new { Stop = stop, Order = stop.StopOrder ?? index + 1, Index = index })
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Index)
            .ToList();
        var sameTerminal = orderedStops[0].Stop.StationId == orderedStops[^1].Stop.StationId;

        if (routeType == RouteTypes.Regular && sameTerminal)
        {
            errors["stops"] = ["Regular route khong duoc trung ben dau/cuoi."];
        }
        else if (routeType == RouteTypes.SightseeingLoop && !sameTerminal)
        {
            errors["stops"] = ["SightseeingLoop phai co ben dau va cuoi trung nhau."];
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

        if (!string.IsNullOrWhiteSpace(request.RouteType) && !RouteTypes.IsValid(request.RouteType))
        {
            errors["routeType"] = ["routeType hop le: Regular, SightseeingLoop, CharterReference."];
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
            ValidateRouteStops(request.Stops, RouteTypes.Normalize(request.RouteType), errors);
        }

        if (request.Stops is not { Count: > 0 }
            && request.StartStationId.HasValue
            && request.EndStationId.HasValue
            && request.StartStationId.Value == request.EndStationId.Value
            && RouteTypes.Normalize(request.RouteType) != RouteTypes.SightseeingLoop)
        {
            errors["endStationId"] = ["startStationId/endStationId chi duoc trung nhau khi routeType=SightseeingLoop."];
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
                endStation is not null ? estimatedDurationMin : null,
                true,
                false));
        }

        if (endStation is not null)
        {
            stops.Add(new RouteStopDraft(
                endStation,
                startStation is not null ? 2 : 1,
                null,
                false,
                true));
        }

        return stops;
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

    private static bool IsRecording(string status) =>
        string.Equals(status, "Recording", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "recording", StringComparison.OrdinalIgnoreCase);

    private static string ToApiStatus(string status) =>
        string.IsNullOrWhiteSpace(status) ? "unknown" : status.Trim().ToLowerInvariant();

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeRouteCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

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
        string? RouteType,
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
        string? RouteType,
        string? Description,
        string? Status,
        bool? IsBookable,
        Guid? StartStationId,
        Guid? EndStationId,
        decimal? AverageSpeedKmh,
        IReadOnlyList<GpsRouteStopRequest>? Stops,
        IReadOnlyList<GpsRouteCoordinateRequest>? Coordinates);

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

    private sealed record StopGpsSessionResponse(Guid SessionId, string Status, int RecordedPointCount);

    private sealed record SaveRouteFromGpsResponse(
        Guid RouteId,
        string RouteCode,
        string RouteName,
        string RouteType,
        decimal BaseDistanceKm,
        int? EstimatedDurationMin,
        int PointCount,
        string Status,
        bool IsBookable,
        IReadOnlyList<GpsRouteStopResponse> Stops);

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
