using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Operations;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Operations;

public class GetOperationScheduleQueryTests
{
    [Test]
    public async Task BookingServiceTypeReturnsBusAndSightseeingTripsOnly()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var departure = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();
        var busTrip = SeedTrip(
            context,
            "BB-20300101-B01-0800",
            RouteTypes.Regular,
            TripTypes.Regular,
            departure,
            capacity: 79);
        var sightseeingTrip = SeedTrip(
            context,
            "BS-20300101-S01-0830",
            RouteTypes.SightseeingLoop,
            TripTypes.Regular,
            departure.AddMinutes(30),
            capacity: 40);
        SeedTrip(
            context,
            "BR-20300101-CB001-1",
            RouteTypes.CharterReference,
            TripTypes.Charter,
            departure.AddHours(1),
            capacity: 20);
        SeedPassengers(context, busTrip, count: 2);
        SeedPassengers(context, sightseeingTrip, count: 3);
        await context.SaveChangesAsync();

        var result = await new GetOperationScheduleQueryHandler(
                context,
                admin,
                new FixedTimeProvider(departure.AddHours(-1)))
            .Handle(
                new GetOperationScheduleQuery(
                    departure.AddDays(-1),
                    departure.AddDays(7),
                    IncludeCancelled: false,
                    ServiceType: "booking"),
                CancellationToken.None);

        result.Select(x => x.SourceCode).ShouldBe([
            "BB-20300101-B01-0800",
            "BS-20300101-S01-0830"
        ]);
        result.Single(x => x.SourceCode.StartsWith("BB-", StringComparison.Ordinal)).ServiceType.ShouldBe("Bus");
        result.Single(x => x.SourceCode.StartsWith("BS-", StringComparison.Ordinal)).ServiceType.ShouldBe("Sightseeing");
        result.Single(x => x.SourceCode.StartsWith("BB-", StringComparison.Ordinal)).RouteType.ShouldBe(RouteTypes.Regular);
        result.Single(x => x.SourceCode.StartsWith("BS-", StringComparison.Ordinal)).RouteType.ShouldBe(RouteTypes.SightseeingLoop);
        result.Single(x => x.SourceCode.StartsWith("BB-", StringComparison.Ordinal)).TripType.ShouldBe(TripTypes.Regular);
        result.Single(x => x.SourceCode.StartsWith("BB-", StringComparison.Ordinal)).CapacitySnapshot.ShouldBe(79);
        result.Single(x => x.SourceCode.StartsWith("BB-", StringComparison.Ordinal)).TotalPassengerCount.ShouldBe(2);
        result.Single(x => x.SourceCode.StartsWith("BS-", StringComparison.Ordinal)).TotalPassengerCount.ShouldBe(3);
        var bus = result.Single(x => x.SourceCode.StartsWith("BB-", StringComparison.Ordinal));
        bus.DestinationStationId.ShouldBe(bus.ToStationId);
        bus.DestinationStationCode.ShouldBe(bus.ToStationCode);
        bus.DestinationStationName.ShouldBe(bus.ToLocation);
    }

    [Test]
    public async Task BusServiceTypeReturnsOnlyRegularBusTrips()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var departure = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();
        SeedTrip(context, "BB-20300101-B01-0800", RouteTypes.Regular, TripTypes.Regular, departure, capacity: 79);
        SeedTrip(context, "BS-20300101-S01-0830", RouteTypes.SightseeingLoop, TripTypes.Regular, departure.AddMinutes(30), capacity: 40);
        await context.SaveChangesAsync();

        var result = await new GetOperationScheduleQueryHandler(
                context,
                admin,
                new FixedTimeProvider(departure.AddHours(-1)))
            .Handle(
                new GetOperationScheduleQuery(
                    departure.AddDays(-1),
                    departure.AddDays(7),
                    IncludeCancelled: false,
                    ServiceType: "bus"),
                CancellationToken.None);

        result.Single().SourceCode.ShouldBe("BB-20300101-B01-0800");
    }

    [Test]
    public async Task CustomerIsLimitedToBookingTripsAndSevenDays()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customer = await SeatFlowTestData.SeedCustomerAsync(context);
        var departure = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();
        SeedTrip(context, "BB-20300101-B01-0800", RouteTypes.Regular, TripTypes.Regular, departure, capacity: 79);
        SeedTrip(context, "BR-20300101-CB001-1", RouteTypes.CharterReference, TripTypes.Charter, departure.AddHours(1), capacity: 20);
        await context.SaveChangesAsync();

        var handler = new GetOperationScheduleQueryHandler(
            context,
            customer,
            new FixedTimeProvider(departure.AddHours(-1)));

        var result = await handler.Handle(
            new GetOperationScheduleQuery(
                departure.AddDays(-1),
                departure.AddDays(6),
                IncludeCancelled: true,
                ServiceType: "charter"),
            CancellationToken.None);

        result.Single().SourceCode.ShouldBe("BB-20300101-B01-0800");
        result.Single().LatestLatitude.ShouldBeNull();
        result.Single().Stops.ShouldNotBeNull();
        result.Single().Stops!.Count.ShouldBe(2);

        await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new GetOperationScheduleQuery(
                departure.AddDays(-1),
                departure.AddDays(8),
                IncludeCancelled: false),
            CancellationToken.None));
    }

    [Test]
    public async Task AnonymousIsLimitedToBookingTripsAndSevenDays()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var departure = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();
        SeedTrip(context, "BB-20300101-B01-0800", RouteTypes.Regular, TripTypes.Regular, departure, capacity: 79);
        SeedTrip(context, "BS-20300101-S01-0830", RouteTypes.SightseeingLoop, TripTypes.Regular, departure.AddMinutes(30), capacity: 40);
        SeedTrip(context, "BR-20300101-CB001-1", RouteTypes.CharterReference, TripTypes.Charter, departure.AddHours(1), capacity: 20);
        await context.SaveChangesAsync();

        var handler = new GetOperationScheduleQueryHandler(
            context,
            AnonymousUserContext.Instance,
            new FixedTimeProvider(departure.AddHours(-1)));

        var result = await handler.Handle(
            new GetOperationScheduleQuery(
                departure.AddDays(-1),
                departure.AddDays(6),
                IncludeCancelled: true,
                ServiceType: "charter"),
            CancellationToken.None);

        result.Select(x => x.SourceCode).ShouldBe([
            "BB-20300101-B01-0800",
            "BS-20300101-S01-0830"
        ]);
        result.ShouldAllBe(x => x.ServiceType == "Bus" || x.ServiceType == "Sightseeing");
        result.ShouldAllBe(x => x.LatestLatitude == null && x.LatestLongitude == null);

        await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new GetOperationScheduleQuery(
                departure.AddDays(-1),
                departure.AddDays(8),
                IncludeCancelled: false),
            CancellationToken.None));
    }

    [Test]
    public async Task GroundStaffOnlySeesTripsThroughAssignedStation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staff = await SeatFlowTestData.SeedStaffAsync(context, StaffType.Ground);
        var departure = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();
        var assignedTrip = SeedTrip(context, "BB-20300101-B01-0800", RouteTypes.Regular, TripTypes.Regular, departure, capacity: 79);
        SeedTrip(context, "BB-20300101-B02-0900", RouteTypes.Regular, TripTypes.Regular, departure.AddHours(1), capacity: 79);
        var assignedStationId = assignedTrip.TripStops.OrderBy(x => x.StopOrder).First().StationId;
        context.Set<UserStationAssignment>().Add(new UserStationAssignment
        {
            UserId = staff.UserId!.Value,
            StationId = assignedStationId,
            IsPrimary = true,
            IsActive = true,
            AssignedAt = departure.AddDays(-1),
            AssignedByUserId = staff.UserId.Value
        });
        await context.SaveChangesAsync();

        var result = await new GetOperationScheduleQueryHandler(
                context,
                staff,
                new FixedTimeProvider(departure.AddHours(-1)))
            .Handle(
                new GetOperationScheduleQuery(
                    departure.AddDays(-1),
                    departure.AddDays(7),
                    IncludeCancelled: false),
                CancellationToken.None);

        result.Single().SourceCode.ShouldBe("BB-20300101-B01-0800");
    }

    private static Trip SeedTrip(
        ApplicationDbContext context,
        string tripCode,
        string routeType,
        string tripType,
        DateTimeOffset departure,
        int capacity)
    {
        var stationA = Station($"{tripCode}-A", "Bến A");
        var stationB = routeType == RouteTypes.SightseeingLoop
            ? stationA
            : Station($"{tripCode}-B", "Bến B");
        var route = new Route
        {
            RouteCode = tripCode.Split('-')[2],
            RouteName = tripCode,
            RouteType = routeType,
            IsBookable = true,
            Status = "Active"
        };
        route.RouteStops.Add(RouteStop(route, stationA, stopOrder: 1));
        route.RouteStops.Add(RouteStop(route, stationB, stopOrder: 2));

        var boat = new Boat
        {
            Code = $"BOAT-{tripCode}",
            Name = $"Boat {tripCode}",
            Status = BoatStatus.Active,
            SeatCount = capacity,
            NumberOfDecks = 1,
            SeatSetupType = routeType == RouteTypes.SightseeingLoop
                ? SeatSetupType.StandardAndVip
                : SeatSetupType.FullStandard,
            SeatsConfigured = true
        };
        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            Boat = boat,
            BoatId = boat.Id,
            TripCode = tripCode,
            TripType = tripType,
            OperatingDate = DateOnly.FromDateTime(departure.ToOffset(TimeSpan.FromHours(7)).Date),
            DepartureTime = departure,
            ArrivalTime = departure.AddMinutes(30),
            CapacitySnapshot = capacity,
            TripStatus = TripStatus.Scheduled
        };
        trip.TripStops.Add(TripStop(trip, stationA, stopOrder: 1, plannedArrival: null, plannedDeparture: departure));
        trip.TripStops.Add(TripStop(trip, stationB, stopOrder: 2, plannedArrival: departure.AddMinutes(30), plannedDeparture: null));
        context.Add(trip);
        return trip;
    }

    private static void SeedPassengers(ApplicationDbContext context, Trip trip, int count)
    {
        var booking = new Booking
        {
            Trip = trip,
            TripId = trip.Id,
            BookingCode = $"BK-{trip.TripCode}",
            BookingStatus = BookingStatus.Confirmed,
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            PaymentStatus = "Paid"
        };

        for (var i = 0; i < count; i++)
        {
            booking.Passengers.Add(new BookingPassenger
            {
                Booking = booking,
                BookingId = booking.Id,
                Trip = trip,
                TripId = trip.Id,
                FullName = $"Passenger {i + 1}"
            });
        }

        context.Add(booking);
    }

    private static RouteStop RouteStop(Route route, Station station, int stopOrder) =>
        new()
        {
            Route = route,
            RouteId = route.Id,
            Station = station,
            StationId = station.Id,
            StopOrder = stopOrder,
            StandardTravelMin = stopOrder == 1 ? null : 30
        };

    private static TripStop TripStop(
        Trip trip,
        Station station,
        int stopOrder,
        DateTimeOffset? plannedArrival,
        DateTimeOffset? plannedDeparture) =>
        new()
        {
            Trip = trip,
            TripId = trip.Id,
            Station = station,
            StationId = station.Id,
            StopOrder = stopOrder,
            PlannedArrivalTime = plannedArrival,
            PlannedDepartureTime = plannedDeparture
        };

    private static Station Station(string code, string name) =>
        new()
        {
            StationCode = code,
            StationName = name,
            Status = StationStatus.Active
        };

    private sealed class AnonymousUserContext : IUserContext
    {
        public static readonly AnonymousUserContext Instance = new();

        public Guid? UserId => null;

        public bool IsAuthenticated => false;
    }
}
