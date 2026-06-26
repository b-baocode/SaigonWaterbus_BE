using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed record DemoScheduleSeedResult(
    DateTimeOffset NowUtc,
    DateTimeOffset SyncFromUtc,
    DateTimeOffset SyncToUtc,
    IReadOnlyCollection<Guid> TripIds);

public static class DemoScheduleSeedData
{
    private const string DemoPrefix = "DEMO-";
    private static readonly TimeSpan VietnamUtcOffset = TimeSpan.FromHours(7);

    public static async Task<DemoScheduleSeedResult> SeedAsync(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        var syncFrom = nowUtc.AddHours(-2);
        var syncTo = nowUtc.AddDays(2);

        await RemovePreviousDemoDataAsync(context, cancellationToken);

        var stations = await EnsureStationsAsync(context, cancellationToken);
        var route = await EnsureRouteAsync(context, stations, cancellationToken);
        var routeStops = await context.RouteStops
            .Where(x => x.RouteId == route.Id)
            .Include(x => x.Station)
            .OrderBy(x => x.StopOrder)
            .ToArrayAsync(cancellationToken);

        await EnsureBoatsAsync(context, cancellationToken);

        var tripIds = await SeedTripsAsync(context, route, routeStops, nowUtc, cancellationToken);

        return new DemoScheduleSeedResult(
            nowUtc,
            syncFrom,
            syncTo,
            tripIds);
    }

    private static async Task RemovePreviousDemoDataAsync(
        ApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        var demoTripIds = await context.Trips
            .Where(x => x.TripCode.StartsWith(DemoPrefix))
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);

        if (demoTripIds.Length > 0)
        {
            await context.Trips
                .Where(x => demoTripIds.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<Station>> EnsureStationsAsync(
        ApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new StationDefinition("DEMO-BD", "Demo Bến Bạch Đằng", "Quận 1", 10.776760m, 106.703500m),
            new StationDefinition("DEMO-TT", "Demo Bến Thủ Thiêm", "TP. Thủ Đức", 10.788100m, 106.719500m),
            new StationDefinition("DEMO-BS", "Demo Bến Ba Sơn", "Quận 1", 10.793000m, 106.708300m),
            new StationDefinition("DEMO-BA", "Demo Bến Bình An", "TP. Thủ Đức", 10.803800m, 106.732700m),
            new StationDefinition("DEMO-TD", "Demo Bến Thảo Điền", "TP. Thủ Đức", 10.807700m, 106.738300m),
            new StationDefinition("DEMO-LD", "Demo Bến Linh Đông", "TP. Thủ Đức", 10.841800m, 106.716700m)
        };

        var stations = new List<Station>();
        foreach (var definition in definitions)
        {
            var station = await context.Stations
                .SingleOrDefaultAsync(x => x.StationCode == definition.Code, cancellationToken);

            if (station is null)
            {
                station = new Station { StationCode = definition.Code };
                context.Stations.Add(station);
            }

            station.StationName = definition.Name;
            station.Address = definition.Address;
            station.Latitude = definition.Latitude;
            station.Longitude = definition.Longitude;
            station.Status = StationStatus.Active;
            station.HasWaitingArea = true;
            station.HasTicketCounter = true;
            stations.Add(station);
        }

        await context.SaveChangesAsync(cancellationToken);
        return stations;
    }

    private static async Task<Route> EnsureRouteAsync(
        ApplicationDbContext context,
        IReadOnlyList<Station> stations,
        CancellationToken cancellationToken)
    {
        var route = await context.Routes
            .SingleOrDefaultAsync(x => x.RouteCode == "DEMO-R01", cancellationToken);

        if (route is null)
        {
            route = new Route { RouteCode = "DEMO-R01" };
            context.Routes.Add(route);
        }

        route.RouteName = "Demo tuyến public board";
        route.Description = "Tuyến demo dùng để xem bảng điện tử công cộng và lịch vận hành nội bộ.";
        route.BaseDistanceKm = 10.8m;
        route.EstimatedDurationMin = 55;
        route.Status = "Active";

        await context.SaveChangesAsync(cancellationToken);

        var travelMinutes = new[] { 0, 8, 8, 12, 12, 15 };
        for (var i = 0; i < stations.Count; i++)
        {
            var stopOrder = i + 1;
            var station = stations[i];
            var routeStop = await context.RouteStops
                .SingleOrDefaultAsync(
                    x => x.RouteId == route.Id && x.StopOrder == stopOrder,
                    cancellationToken);

            if (routeStop is null)
            {
                routeStop = new RouteStop
                {
                    RouteId = route.Id,
                    StopOrder = stopOrder
                };
                context.RouteStops.Add(routeStop);
            }

            routeStop.StationId = station.Id;
            routeStop.StandardTravelMin = travelMinutes[i];
            routeStop.StandardDwellMin = i == stations.Count - 1 ? 0 : 2;
            routeStop.IsPickupAllowed = true;
            routeStop.IsDropoffAllowed = true;
        }

        await context.SaveChangesAsync(cancellationToken);
        return route;
    }

    private static async Task<IReadOnlyList<Boat>> EnsureBoatsAsync(
        ApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new BoatDefinition("DEMO-V01", "Demo Waterbus 01", 80, 1),
            new BoatDefinition("DEMO-V02", "Demo Waterbus 02", 60, 2)
        };

        var boats = new List<Boat>();
        foreach (var definition in definitions)
        {
            var boat = await context.Boats
                .SingleOrDefaultAsync(x => x.Code == definition.Code, cancellationToken);

            if (boat is null)
            {
                boat = new Boat { Code = definition.Code };
                context.Boats.Add(boat);
            }

            boat.Name = definition.Name;
            boat.RegistrationNumber = $"{definition.Code}-REG";
            boat.Status = BoatStatus.Active;
            boat.SeatCount = definition.SeatCount;
            boat.NumberOfDecks = definition.Decks;
            boat.SeatSetupType = SeatSetupType.FullStandard;
            boat.MaxSpeedKmh = 25;
            boat.YearBuilt = 2024;
            boat.Description = "Tàu demo dùng cho lịch vận hành.";
            boats.Add(boat);
        }

        await context.SaveChangesAsync(cancellationToken);
        return boats;
    }

    private static async Task<IReadOnlyCollection<Guid>> SeedTripsAsync(
        ApplicationDbContext context,
        Route route,
        IReadOnlyList<RouteStop> routeStops,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var tripDefinitions = new[]
        {
            new TripDefinition("DEMO-PAST-01", -18, TripStatus.InProgress, 80),
            new TripDefinition("DEMO-BOARD-01", 5, TripStatus.Boarding, 80),
            new TripDefinition("DEMO-ARRIVE-TT", 1, TripStatus.Scheduled, 60),
            new TripDefinition("DEMO-ARRIVE-BS", -7, TripStatus.InProgress, 60),
            new TripDefinition("DEMO-UP-01", 25, TripStatus.Scheduled, 80),
            new TripDefinition("DEMO-UP-02", 55, TripStatus.Scheduled, 80),
            new TripDefinition("DEMO-UP-03", 95, TripStatus.Scheduled, 60),
            new TripDefinition("DEMO-DELAY-01", 125, TripStatus.Delayed, 60)
        };

        var trips = tripDefinitions
            .Select(x => CreateTrip(route, routeStops, x, nowUtc))
            .ToArray();

        context.Trips.AddRange(trips);
        await context.SaveChangesAsync(cancellationToken);
        return trips.Select(x => x.Id).ToArray();
    }

    private static Trip CreateTrip(
        Route route,
        IReadOnlyList<RouteStop> routeStops,
        TripDefinition definition,
        DateTimeOffset nowUtc)
    {
        var startAt = nowUtc.AddMinutes(definition.StartOffsetMinutes).ToUniversalTime();
        var runningArrival = startAt;
        var firstDeparture = startAt;

        for (var i = 0; i < routeStops.Count; i++)
        {
            var routeStop = routeStops[i];
            if (i > 0)
            {
                runningArrival = runningArrival.AddMinutes(routeStop.StandardTravelMin ?? 1);
            }

            var dwellMinutes = routeStop.StandardDwellMin ?? 0;
            var departure = runningArrival.AddMinutes(dwellMinutes);
            if (i == 0)
            {
                firstDeparture = departure;
            }
        }

        return new Trip
        {
            RouteId = route.Id,
            TripCode = definition.Code,
            OperatingDate = DateOnly.FromDateTime(startAt.ToOffset(VietnamUtcOffset).DateTime),
            DepartureTime = firstDeparture,
            ArrivalTime = runningArrival,
            CapacitySnapshot = definition.Capacity,
            TripStatus = definition.Status,
            StatusNote = definition.Status == TripStatus.Delayed ? "Demo delay 10 phút" : null
        };
    }

    private sealed record StationDefinition(
        string Code,
        string Name,
        string Address,
        decimal Latitude,
        decimal Longitude);

    private sealed record BoatDefinition(string Code, string Name, int SeatCount, int Decks);

    private sealed record TripDefinition(
        string Code,
        int StartOffsetMinutes,
        TripStatus Status,
        int Capacity);
}
