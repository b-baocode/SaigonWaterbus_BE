using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Data;

public sealed record DemoScheduleSeedResult(
    DateTimeOffset NowUtc,
    DateTimeOffset SyncFromUtc,
    DateTimeOffset SyncToUtc,
    IReadOnlyCollection<Guid> TripIds,
    IReadOnlyCollection<Guid> CustomBookingRequestIds);

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

        var service = await EnsureWaterTaxiServiceAsync(context, cancellationToken);
        var vessels = await EnsureVesselsAsync(context, cancellationToken);

        var tripIds = await SeedTripsAsync(context, route, routeStops, nowUtc, cancellationToken);
        var customBookingIds = await SeedCustomBookingsAsync(
            context,
            service,
            vessels,
            stations,
            nowUtc,
            cancellationToken);

        return new DemoScheduleSeedResult(
            nowUtc,
            syncFrom,
            syncTo,
            tripIds,
            customBookingIds);
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
            await context.OperationScheduleEntries
                .Where(x => x.SourceType == OperationScheduleSourceType.RegularTrip
                            && demoTripIds.Contains(x.SourceId))
                .ExecuteDeleteAsync(cancellationToken);

            await context.Trips
                .Where(x => demoTripIds.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        var demoCustomBookingIds = await context.CustomBookingRequests
            .Where(x => x.ContactName.StartsWith("DEMO "))
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);

        if (demoCustomBookingIds.Length > 0)
        {
            await context.OperationScheduleEntries
                .Where(x => x.SourceType == OperationScheduleSourceType.CustomBooking
                            && demoCustomBookingIds.Contains(x.SourceId))
                .ExecuteDeleteAsync(cancellationToken);

            await context.CustomBookingRequests
                .Where(x => demoCustomBookingIds.Contains(x.Id))
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

    private static async Task<WaterbusService> EnsureWaterTaxiServiceAsync(
        ApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        var service = await context.WaterbusServices
            .SingleOrDefaultAsync(x => x.Code == "WT", cancellationToken);

        if (service is not null)
        {
            return service;
        }

        service = new WaterbusService
        {
            Code = "WT",
            Name = "WaterTaxi",
            Description = "Dịch vụ taxi đường thủy theo nhu cầu.",
            BookingMode = BookingMode.VesselRental,
            DisplayOrder = 3,
            IsActive = true
        };
        context.WaterbusServices.Add(service);
        await context.SaveChangesAsync(cancellationToken);
        return service;
    }

    private static async Task<IReadOnlyList<Vessel>> EnsureVesselsAsync(
        ApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new VesselDefinition("DEMO-V01", "Demo Waterbus 01", 80, 1),
            new VesselDefinition("DEMO-V02", "Demo Waterbus 02", 60, 2)
        };

        var vessels = new List<Vessel>();
        foreach (var definition in definitions)
        {
            var vessel = await context.Vessels
                .SingleOrDefaultAsync(x => x.Code == definition.Code, cancellationToken);

            if (vessel is null)
            {
                vessel = new Vessel { Code = definition.Code };
                context.Vessels.Add(vessel);
            }

            vessel.Name = definition.Name;
            vessel.RegistrationNumber = $"{definition.Code}-REG";
            vessel.Status = VesselStatus.Active;
            vessel.SeatCount = definition.SeatCount;
            vessel.NumberOfDecks = definition.Decks;
            vessel.SeatSetupType = SeatSetupType.FullStandard;
            vessel.MaxSpeedKmh = 25;
            vessel.YearBuilt = 2024;
            vessel.Description = "Tàu demo dùng cho lịch vận hành.";
            vessels.Add(vessel);
        }

        await context.SaveChangesAsync(cancellationToken);
        return vessels;
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
        var tripStops = new List<TripStop>();

        for (var i = 0; i < routeStops.Count; i++)
        {
            var routeStop = routeStops[i];
            if (i > 0)
            {
                runningArrival = runningArrival.AddMinutes(routeStop.StandardTravelMin ?? 1);
            }

            var dwellMinutes = routeStop.StandardDwellMin ?? 0;
            var departure = runningArrival.AddMinutes(dwellMinutes);
            tripStops.Add(new TripStop
            {
                RouteStopId = routeStop.Id,
                StopOrder = routeStop.StopOrder,
                ScheduledArrival = runningArrival,
                ScheduledDeparture = departure,
                StopStatus = definition.Status == TripStatus.Cancelled ? "Cancelled" : "Scheduled"
            });
        }

        return new Trip
        {
            RouteId = route.Id,
            TripCode = definition.Code,
            OperatingDate = DateOnly.FromDateTime(startAt.ToOffset(VietnamUtcOffset).DateTime),
            DepartureTime = tripStops.First().ScheduledDeparture!.Value,
            ArrivalTime = tripStops.Last().ScheduledArrival!.Value,
            CapacitySnapshot = definition.Capacity,
            TripStatus = definition.Status,
            StatusNote = definition.Status == TripStatus.Delayed ? "Demo delay 10 phút" : null,
            TripStops = tripStops
        };
    }

    private static async Task<IReadOnlyCollection<Guid>> SeedCustomBookingsAsync(
        ApplicationDbContext context,
        WaterbusService service,
        IReadOnlyList<Vessel> vessels,
        IReadOnlyList<Station> stations,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new CustomBookingDefinition("DEMO Custom fully paid", 75, 120, 18_000_000m, 0m),
            new CustomBookingDefinition("DEMO Custom deposit paid", 150, 90, 12_000_000m, 0m),
            new CustomBookingDefinition("DEMO Custom tomorrow", 1_620, 180, 25_000_000m, 0m)
        };

        var requests = new List<CustomBookingRequest>();
        for (var i = 0; i < definitions.Length; i++)
        {
            var definition = definitions[i];
            var startUtc = nowUtc.AddMinutes(definition.StartOffsetMinutes).ToUniversalTime();
            var endUtc = startUtc.AddMinutes(definition.DurationMinutes).ToUniversalTime();
            var startVn = startUtc.ToOffset(VietnamUtcOffset);
            var endVn = endUtc.ToOffset(VietnamUtcOffset);
            var fromStation = stations[i % stations.Count];
            var toStation = stations[(i + 3) % stations.Count];
            var vessel = vessels[i % vessels.Count];

            var request = new CustomBookingRequest
            {
                ContactName = definition.ContactName,
                ContactPhone = $"09000009{i + 1}",
                ContactEmail = $"demo.custom{i + 1}@saigonwaterbus.local",
                WaterbusServiceId = service.Id,
                RequestedNumberOfDecks = vessel.NumberOfDecks,
                RequestedSeatSetupType = vessel.SeatSetupType,
                RentalUnit = VesselRentalUnit.Hour,
                AssignedVesselId = vessel.Id,
                AssignedAt = nowUtc,
                DepartureDate = DateOnly.FromDateTime(startVn.DateTime),
                PreferredStartTime = TimeOnly.FromDateTime(startVn.DateTime),
                PreferredEndTime = TimeOnly.FromDateTime(endVn.DateTime),
                EstimatedEndDate = DateOnly.FromDateTime(endVn.DateTime),
                EstimatedTravelMinutes = Math.Max(1, definition.DurationMinutes - 30),
                EstimatedStayMinutes = 20,
                BufferMinutes = 10,
                EstimatedDurationMinutes = definition.DurationMinutes,
                FromStationId = fromStation.Id,
                FromStationCode = fromStation.StationCode,
                FromLocation = fromStation.StationName,
                ToStationId = toStation.Id,
                ToStationCode = toStation.StationCode,
                ToLocation = toStation.StationName,
                PassengerCount = 35 + (i * 10),
                AdultCount = 30 + (i * 10),
                ChildCount = 5,
                SpecialRequests = "Demo booking dùng để kiểm tra lịch vận hành nội bộ.",
                Status = CustomBookingRequestStatus.Confirmed,
                QuotedAt = nowUtc.AddMinutes(-30),
                QuoteAcceptedAt = nowUtc.AddMinutes(-20)
            };

            request.Quote = new CustomBookingQuote
            {
                CustomBookingRequest = request,
                QuotedPrice = definition.QuotedPrice,
                ServiceFeeAmount = 0,
                DiscountAmount = 0,
                DepositPercent = 50,
                DepositAmount = definition.QuotedPrice / 2,
                RemainingAmount = definition.RemainingAmount,
                Currency = "VND",
                PriceNote = "Demo quote đã thanh toán cọc.",
                ValidUntil = nowUtc.AddDays(1),
                DepositPaymentStatus = CustomBookingDepositPaymentStatus.Paid,
                DepositPaymentPaidAt = nowUtc.AddMinutes(-15),
                RemainingPaymentStatus = definition.RemainingAmount <= 0
                    ? CustomBookingDepositPaymentStatus.Paid
                    : CustomBookingDepositPaymentStatus.Pending,
                RemainingPaymentPaidAt = definition.RemainingAmount <= 0
                    ? nowUtc.AddMinutes(-10)
                    : null
            };

            requests.Add(request);
        }

        context.CustomBookingRequests.AddRange(requests);
        await context.SaveChangesAsync(cancellationToken);
        return requests.Select(x => x.Id).ToArray();
    }

    private sealed record StationDefinition(
        string Code,
        string Name,
        string Address,
        decimal Latitude,
        decimal Longitude);

    private sealed record VesselDefinition(string Code, string Name, int SeatCount, int Decks);

    private sealed record TripDefinition(
        string Code,
        int StartOffsetMinutes,
        TripStatus Status,
        int Capacity);

    private sealed record CustomBookingDefinition(
        string ContactName,
        int StartOffsetMinutes,
        int DurationMinutes,
        decimal QuotedPrice,
        decimal RemainingAmount);
}
