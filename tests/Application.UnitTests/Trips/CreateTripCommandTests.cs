using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Trips;

public class CreateTripCommandTests
{
    [Test]
    public void TripCreationLeadTimeIsTwentyMinutes()
    {
        TripScheduleSupport.MinimumCreationLeadTime.ShouldBe(TimeSpan.FromMinutes(20));
    }

    [Test]
    public async Task CreateTripRejectsRegularRouteMissingDistance()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = Route("R1", Station("A", "Ben A"), Station("B", "Ben B"), Station("C", "Ben C"));
        route.RouteStops.Single(x => x.StopOrder == 2).DistanceFromPreviousKm = null;
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));

        context.AddRange(route, boat);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new CreateTripCommandHandler(context)
                .Handle(new CreateTripCommand("R1", "BOAT-1", DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None));

        exception.Errors["routeCode"].Single().ShouldContain("distanceFromPreviousKm");
        context.Trips.Count().ShouldBe(0);
    }

    [Test]
    public async Task CreateTripRejectsSameRouteDepartureAtSameTime()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var stationA = Station("A", "Ben A");
        var stationB = Station("B", "Ben B");
        var route = Route("R1", stationA, stationB);
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var existingTrip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            TripCode = "TR-EXISTING",
            OperatingDate = DateOnly.FromDateTime(departureTime.Date),
            DepartureTime = departureTime.ToUniversalTime(),
            ArrivalTime = departureTime.AddMinutes(30).ToUniversalTime(),
            CapacitySnapshot = 50,
            TripStatus = TripStatus.Scheduled
        };

        var boat = BoatWithSeats("BOAT-1", seatCount: 3);

        context.AddRange(route, existingTrip, boat);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new CreateTripCommandHandler(context)
                .Handle(new CreateTripCommand("R1", "BOAT-1", DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None));

        exception.Errors["departureTime"]
            .ShouldContain("Tuyến đã có chuyến tàu xuất phát trong cùng thời điểm.");
    }

    [Test]
    public async Task CreateTripAllowsSameRouteDepartureWhenExistingTripIsCancelled()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var stationA = Station("A", "Ben A");
        var stationB = Station("B", "Ben B");
        var route = Route("R1", stationA, stationB);
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var existingTrip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            TripCode = "TR-CANCELLED",
            OperatingDate = DateOnly.FromDateTime(departureTime.Date),
            DepartureTime = departureTime.ToUniversalTime(),
            ArrivalTime = departureTime.AddMinutes(30).ToUniversalTime(),
            CapacitySnapshot = 50,
            TripStatus = TripStatus.Cancelled
        };

        var boat = BoatWithSeats("BOAT-1", seatCount: 3);

        context.AddRange(route, existingTrip, boat);
        await context.SaveChangesAsync();
        await AddRequiredOnBoardStaffAsync(context, boat, departureTime);

        var result = await new CreateTripCommandHandler(context)
            .Handle(new CreateTripCommand("R1", "BOAT-1", DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None);

        result.RouteName.ShouldBe("R1");
        result.TripCode.ShouldStartWith("BB-20300101-R1-");
    }

    [Test]
    public async Task CreateTripTakesCapacityFromActiveSeatsOfBoat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = Route("R1", Station("A", "Ben A"), Station("B", "Ben B"));
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));

        // 5 ghe nhung 2 ghe bi vo hieu hoa -> suc chua phai la 3, khong phai SeatCount cua tau.
        var boat = BoatWithSeats("BOAT-1", seatCount: 5, inactiveSeatCount: 2);

        context.AddRange(route, boat);
        await context.SaveChangesAsync();
        await AddRequiredOnBoardStaffAsync(context, boat, departureTime);

        var result = await new CreateTripCommandHandler(context)
            .Handle(new CreateTripCommand("R1", "BOAT-1", DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None);

        result.CapacitySnapshot.ShouldBe(3);
        context.Set<TripSeat>().Count(x => x.TripId == result.TripId).ShouldBe(3);
        result.OnBoardStaff.ShouldNotBeNull();
        result.OnBoardStaff.Count.ShouldBe(2);
    }

    [Test]
    public async Task CreateTripReturnsBoatAndStationMedia()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var stationA = Station("A", "Ben A");
        stationA.ImageUrl = "https://example.test/stations/a-main.jpg";
        stationA.ImageUrls = ["https://example.test/stations/a-main.jpg", "https://example.test/stations/a-side.jpg"];
        stationA.Address = "Ben A address";
        stationA.Latitude = 10.1m;
        stationA.Longitude = 106.1m;
        stationA.HasWaitingArea = true;
        var stationB = Station("B", "Ben B");
        stationB.ImageUrl = "https://example.test/stations/b-main.jpg";
        var route = Route("R1", stationA, stationB);
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);
        boat.ImageUrl = "https://example.test/boats/main.jpg";
        boat.ImageUrls = ["https://example.test/boats/main.jpg", "https://example.test/boats/deck.jpg"];
        boat.RegistrationNumber = "SG-001";
        boat.Description = "Tau waterbus test";

        context.AddRange(route, boat);
        await context.SaveChangesAsync();
        await AddRequiredOnBoardStaffAsync(context, boat, departureTime);

        var result = await new CreateTripCommandHandler(context)
            .Handle(new CreateTripCommand("R1", "BOAT-1", DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None);

        result.RouteCode.ShouldBe("R1");
        result.StopCount.ShouldBe(2);
        result.Boat.ShouldNotBeNull();
        result.Boat.ImageUrl.ShouldBe("https://example.test/boats/main.jpg");
        result.Boat.ImageUrls.ShouldBe(["https://example.test/boats/main.jpg", "https://example.test/boats/deck.jpg"]);
        result.Boat.RegistrationNumber.ShouldBe("SG-001");
        result.FromStation.ShouldNotBeNull();
        result.FromStation.ImageUrl.ShouldBe("https://example.test/stations/a-main.jpg");
        result.FromStation.ImageUrls.ShouldBe(["https://example.test/stations/a-main.jpg", "https://example.test/stations/a-side.jpg"]);
        result.FromStation.Address.ShouldBe("Ben A address");
        result.FromStation.Latitude.ShouldBe(10.1m);
        result.ToStation.ShouldNotBeNull();
        result.ToStation.ImageUrl.ShouldBe("https://example.test/stations/b-main.jpg");
        result.Stops.First().StationImageUrl.ShouldBe("https://example.test/stations/a-main.jpg");
        result.Stops.First().StationImageUrls.ShouldBe(["https://example.test/stations/a-main.jpg", "https://example.test/stations/a-side.jpg"]);
    }

    [Test]
    public async Task CreateTripFailsWhenBoatDoesNotHaveTwoOnBoardStaff()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = Route("R1", Station("A", "Ben A"), Station("B", "Ben B"));
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);

        context.AddRange(route, boat);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new CreateTripCommandHandler(context)
                .Handle(new CreateTripCommand("R1", "BOAT-1", DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None));

        exception.Errors["boatCode"].Single().ShouldContain("ít nhất 2 nhân viên OnBoard");
    }

    [Test]
    public async Task CreateTripFailsWhenBoatHasNoActiveSeats()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = Route("R1", Station("A", "Ben A"), Station("B", "Ben B"));
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var boat = BoatWithSeats("BOAT-1", seatCount: 2, inactiveSeatCount: 2);

        context.AddRange(route, boat);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new CreateTripCommandHandler(context)
                .Handle(new CreateTripCommand("R1", "BOAT-1", DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None));

        exception.Errors["boatCode"].ShouldContain("Boat has no active seats.");
    }

    [Test]
    public async Task CreateTripRejectsBoatAlreadyRunningAnotherTripOnADifferentRoute()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var routeA = Route("R1", Station("A", "Ben A"), Station("B", "Ben B"));
        var routeB = Route("R2", Station("C", "Ben C"), Station("D", "Ben D"));
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);

        // Tau dang chay tuyen R2 tu 08:00 den 09:00.
        var existingDeparture = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var existingTrip = new Trip
        {
            Route = routeB,
            RouteId = routeB.Id,
            Boat = boat,
            BoatId = boat.Id,
            TripCode = "TR-BUSY",
            OperatingDate = DateOnly.FromDateTime(existingDeparture.Date),
            DepartureTime = existingDeparture.ToUniversalTime(),
            ArrivalTime = existingDeparture.AddHours(1).ToUniversalTime(),
            CapacitySnapshot = 3,
            TripStatus = TripStatus.Scheduled
        };

        context.AddRange(routeA, routeB, boat, existingTrip);
        await context.SaveChangesAsync();

        // Chuyen moi tren tuyen R1 khoi hanh 08:30 -> chong gio voi chuyen dang chay.
        var newDeparture = existingDeparture.AddMinutes(30);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new CreateTripCommandHandler(context)
                .Handle(new CreateTripCommand("R1", "BOAT-1", DateOnly.FromDateTime(newDeparture.Date), newDeparture), CancellationToken.None));

        exception.Errors["boatCode"].ShouldNotBeEmpty();
        exception.Errors["boatCode"][0].ShouldContain("TR-BUSY");
    }

    [Test]
    public async Task CreateTripRejectsBoatWhenGapIsSmallerThanTurnaroundBuffer()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = Route("R1", Station("A", "Ben A"), Station("B", "Ben B"));
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);

        var existingDeparture = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var existingTrip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            Boat = boat,
            BoatId = boat.Id,
            TripCode = "TR-BUSY",
            OperatingDate = DateOnly.FromDateTime(existingDeparture.Date),
            DepartureTime = existingDeparture.ToUniversalTime(),
            ArrivalTime = existingDeparture.AddHours(1).ToUniversalTime(),
            CapacitySnapshot = 3,
            TripStatus = TripStatus.Scheduled
        };

        context.AddRange(route, boat, existingTrip);
        await context.SaveChangesAsync();

        // Chuyen truoc ket thuc 09:00; chuyen moi khoi hanh 09:03 -> chi cach 3 phut < 5 phut quay dau.
        var tooSoon = existingDeparture.AddHours(1).AddMinutes(3);

        await Should.ThrowAsync<ValidationException>(() =>
            new CreateTripCommandHandler(context)
                .Handle(new CreateTripCommand("R1", "BOAT-1", DateOnly.FromDateTime(tooSoon.Date), tooSoon), CancellationToken.None));
    }

    [Test]
    public async Task CreateTripAllowsReverseRouteWhenGapIsAtLeastTurnaroundBuffer()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var stationA = Station("A", "Ben A");
        var stationB = Station("B", "Ben B");
        var outbound = Route("OUT", stationA, stationB);
        var inbound = Route("IN", stationB, stationA);
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);

        var existingDeparture = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var existingTrip = new Trip
        {
            Route = outbound,
            RouteId = outbound.Id,
            Boat = boat,
            BoatId = boat.Id,
            TripCode = "TR-EARLIER",
            OperatingDate = DateOnly.FromDateTime(existingDeparture.Date),
            DepartureTime = existingDeparture.ToUniversalTime(),
            ArrivalTime = existingDeparture.AddHours(1).ToUniversalTime(),
            CapacitySnapshot = 3,
            TripStatus = TripStatus.Scheduled
        };

        context.AddRange(outbound, inbound, boat, existingTrip);
        await context.SaveChangesAsync();

        // Chuyen truoc den Ben B luc 09:00; chuyen nguoc xuat phat tu Ben B luc 09:20 -> du 5 phut quay dau.
        var farEnough = existingDeparture.AddHours(1).AddMinutes(20);
        await AddRequiredOnBoardStaffAsync(context, boat, farEnough);

        var result = await new CreateTripCommandHandler(context)
            .Handle(new CreateTripCommand("IN", "BOAT-1", DateOnly.FromDateTime(farEnough.Date), farEnough), CancellationToken.None);

        result.CapacitySnapshot.ShouldBe(3);
    }

    [Test]
    public async Task CreateTripRejectsDepartureFromSameStationWithinFiveMinutes()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var stationA = Station("A", "Ben A");
        var stationB = Station("B", "Ben B");
        var stationC = Station("C", "Ben C");
        var routeA = Route("R1", stationA, stationB);
        var routeB = Route("R2", stationA, stationC);
        var boatA = BoatWithSeats("BOAT-1", seatCount: 3);
        var boatB = BoatWithSeats("BOAT-2", seatCount: 3);
        var existingDeparture = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var existingTrip = new Trip
        {
            Route = routeA,
            RouteId = routeA.Id,
            Boat = boatA,
            BoatId = boatA.Id,
            TripCode = "TR-STATION",
            OperatingDate = DateOnly.FromDateTime(existingDeparture.Date),
            DepartureTime = existingDeparture.ToUniversalTime(),
            ArrivalTime = existingDeparture.AddMinutes(30).ToUniversalTime(),
            CapacitySnapshot = 3,
            TripStatus = TripStatus.Scheduled
        };

        context.AddRange(routeA, routeB, boatA, boatB, existingTrip);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new CreateTripCommandHandler(context)
                .Handle(
                    new CreateTripCommand(
                        "R2",
                        "BOAT-2",
                        DateOnly.FromDateTime(existingDeparture.Date),
                        existingDeparture.AddMinutes(3)),
                    CancellationToken.None));

        exception.Errors.Values.SelectMany(x => x).Single()
            .ShouldContain("Các chuyến cùng bến phải cách nhau tối thiểu 5 phút");
    }

    [Test]
    public async Task CreateTripReturnsEachStopExactlyOnce()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = Route("R1", Station("A", "Ben A"), Station("B", "Ben B"));
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));

        context.AddRange(route, boat);
        await context.SaveChangesAsync();
        await AddRequiredOnBoardStaffAsync(context, boat, departureTime);

        var result = await new CreateTripCommandHandler(context)
            .Handle(new CreateTripCommand("R1", "BOAT-1", DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None);

        // Regression: add vào DbSet + nav collection cùng lúc khiến EF fixup nhân đôi stops trong DTO.
        result.Stops.Count.ShouldBe(2);
        result.Stops.Select(x => x.TripStopId).Distinct().Count().ShouldBe(2);
        result.Stops.Select(x => x.StopOrder).ShouldBe([1, 2]);
        context.Set<TripStop>().Count(x => x.TripId == result.TripId).ShouldBe(2);
    }

    [Test]
    public async Task CreateTripUsesRouteIdAndStopStayDurationWhenBuildingTripStops()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var stationA = Station("A", "Ben A");
        var stationB = Station("B", "Ben B");
        var stationC = Station("C", "Ben C");
        var route = Route("R1", stationA, stationB, stationC);
        route.RouteStops.Single(x => x.StopOrder == 2).StandardTravelMin = 20;
        route.RouteStops.Single(x => x.StopOrder == 3).StandardTravelMin = 20;
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));

        context.AddRange(route, boat);
        await context.SaveChangesAsync();
        await AddRequiredOnBoardStaffAsync(context, boat, departureTime);

        var result = await new CreateTripCommandHandler(context)
            .Handle(
                new CreateTripCommand(
                    RouteCode: null,
                    BoatCode: "BOAT-1",
                    OperatingDate: DateOnly.FromDateTime(departureTime.Date),
                    DepartureTime: departureTime,
                    RouteId: route.Id,
                    Stops: [new CreateTripStopScheduleInput(2, 5)]),
                CancellationToken.None);

        result.RouteId.ShouldBe(route.Id);
        result.ArrivalTime.ShouldBe(departureTime.ToUniversalTime().AddMinutes(45));
        result.Stops.Select(x => x.StopOrder).ShouldBe([1, 2, 3]);
        result.Stops[1].StayDurationMinutes.ShouldBe(5);
        result.Stops[1].ScheduledArrival.ShouldBe(departureTime.ToUniversalTime().AddMinutes(20));
        result.Stops[1].ScheduledDeparture.ShouldBe(departureTime.ToUniversalTime().AddMinutes(25));
        result.Stops[2].ScheduledArrival.ShouldBe(departureTime.ToUniversalTime().AddMinutes(45));
        context.Set<TripStop>().Single(x => x.TripId == result.TripId && x.StopOrder == 2)
            .StayDurationMinutes.ShouldBe(5);
    }

    [Test]
    public async Task CreateTripRequiresStayDurationForEveryIntermediateRegularStop()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = Route(
            "R1",
            Station("A", "Ben A"),
            Station("B", "Ben B"),
            Station("C", "Ben C"),
            Station("D", "Ben D"));
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));

        context.AddRange(route, boat);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new CreateTripCommandHandler(context).Handle(
                new CreateTripCommand(
                    RouteCode: "R1",
                    BoatCode: "BOAT-1",
                    OperatingDate: DateOnly.FromDateTime(departureTime.Date),
                    DepartureTime: departureTime,
                    Stops: [new CreateTripStopScheduleInput(2, 0)]),
                CancellationToken.None));

        exception.Errors.Values.SelectMany(x => x).ShouldContain(x => x.Contains("stopOrder: 3"));
    }

    [Test]
    public async Task CreateTripRejectsStandardBoatOnSightseeingRoute()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = Route("R1", Station("A", "Ben A"), Station("B", "Ben B"));
        route.RouteType = RouteTypes.SightseeingLoop;
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));

        context.AddRange(route, boat);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new CreateTripCommandHandler(context)
                .Handle(new CreateTripCommand("R1", "BOAT-1", DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None));

        exception.Errors["boatCode"][0].ShouldContain("ngắm cảnh");
    }

    [Test]
    public async Task CreateTripRejectsSightseeingBoatOnRegularRoute()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = Route("R1", Station("A", "Ben A"), Station("B", "Ben B"));
        var boat = BoatWithSeats("BOAT-1", seatCount: 3, seatSetupType: SeatSetupType.StandardAndVip);
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));

        context.AddRange(route, boat);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new CreateTripCommandHandler(context)
                .Handle(new CreateTripCommand("R1", "BOAT-1", DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None));

        exception.Errors["boatCode"][0].ShouldContain("Tuyến thường");
    }

    [Test]
    public async Task CreateTripAllowsVipBoatOnSightseeingRoute()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = Route("R1", Station("A", "Ben A"), Station("B", "Ben B"));
        route.RouteType = RouteTypes.SightseeingLoop;
        var boat = BoatWithSeats("BOAT-1", seatCount: 3, seatSetupType: SeatSetupType.StandardAndVip);
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));

        context.AddRange(route, boat);
        await context.SaveChangesAsync();
        await AddRequiredOnBoardStaffAsync(context, boat, departureTime);

        var result = await new CreateTripCommandHandler(context)
            .Handle(new CreateTripCommand("R1", "BOAT-1", DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None);

        result.CapacitySnapshot.ShouldBe(3);
    }

    [Test]
    public async Task CreateTripUsesRouteEstimatedDurationForSightseeingLoopWhenStopTravelIsMissing()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var station = Station("BD", "Bến Bạch Đằng");
        var route = Route("LOOP-BD", station, station);
        route.RouteType = RouteTypes.SightseeingLoop;
        route.EstimatedDurationMin = 49.94m;
        route.RouteStops.Single(x => x.StopOrder == 2).StandardTravelMin = null;
        var boat = BoatWithSeats("BOAT-SIGHT", seatCount: 3, seatSetupType: SeatSetupType.StandardAndVip);
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));

        context.AddRange(route, boat);
        await context.SaveChangesAsync();
        await AddRequiredOnBoardStaffAsync(context, boat, departureTime);

        var result = await new CreateTripCommandHandler(context)
            .Handle(new CreateTripCommand("LOOP-BD", "BOAT-SIGHT", DateOnly.FromDateTime(departureTime.Date), departureTime), CancellationToken.None);

        result.ArrivalTime.ToOffset(TimeSpan.FromHours(7)).TimeOfDay.ShouldBe(new TimeSpan(8, 50, 0));
        var trip = await context.Trips.SingleAsync();
        (trip.ArrivalTime - trip.DepartureTime).TotalMinutes.ShouldBe(50);
    }

    [Test]
    public async Task ReplaceTripBoatRemapsBookedPassengersToSameSeatCodesOnNewBoat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = Route("R1", Station("A", "Ben A"), Station("B", "Ben B"));
        var oldBoat = BoatWithSeats("BOAT-OLD", seatCount: 2);
        var newBoat = BoatWithSeats("BOAT-NEW", seatCount: 3);
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var trip = TripWithSeats(route, oldBoat, "TR-REPLACE", departureTime);
        var bookedTripSeat = trip.TripSeats.Single(x => x.Seat.Code == "A1");
        var booking = new Booking
        {
            BookingCode = "BKG-REPLACE",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            Trip = trip,
            TripId = trip.Id,
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid"
        };
        var passenger = new BookingPassenger
        {
            Booking = booking,
            BookingId = booking.Id,
            FullName = "Nguyen Van A",
            Trip = trip,
            TripId = trip.Id,
            TripSeat = bookedTripSeat,
            TripSeatId = bookedTripSeat.Id,
            FromStopOrder = 1,
            ToStopOrder = 2
        };

        context.AddRange(route, oldBoat, newBoat, trip, booking, passenger);
        await context.SaveChangesAsync();

        var result = await new ReplaceTripBoatCommandHandler(context)
            .Handle(new ReplaceTripBoatCommand(trip.Id, newBoat.Id), CancellationToken.None);

        result.Boat.ShouldNotBeNull();
        result.Boat.VesselId.ShouldBe(newBoat.Id);
        result.CapacitySnapshot.ShouldBe(3);

        var remappedPassenger = await context.Set<BookingPassenger>()
            .Include(x => x.TripSeat)
                .ThenInclude(x => x!.Seat)
            .SingleAsync(x => x.Id == passenger.Id);
        remappedPassenger.TripSeat.ShouldNotBeNull();
        remappedPassenger.TripSeat.Seat.BoatId.ShouldBe(newBoat.Id);
        remappedPassenger.TripSeat.Seat.Code.ShouldBe("A1");

        var tripSeatCodes = await context.Set<TripSeat>()
            .Include(x => x.Seat)
            .Where(x => x.TripId == trip.Id)
            .OrderBy(x => x.Seat.Code)
            .Select(x => x.Seat.Code)
            .ToListAsync();
        tripSeatCodes.ShouldBe(["A1", "A2", "A3"]);
    }

    [Test]
    public async Task ReplaceTripBoatRejectsBusyReplacementBoat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = Route("R1", Station("A", "Ben A"), Station("B", "Ben B"));
        var currentBoat = BoatWithSeats("BOAT-CURRENT", seatCount: 2);
        var busyBoat = BoatWithSeats("BOAT-BUSY", seatCount: 2);
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var tripToReplace = TripWithSeats(route, currentBoat, "TR-REPLACE", departureTime);
        var conflictingTrip = TripWithSeats(route, busyBoat, "TR-BUSY", departureTime.AddMinutes(30));

        context.AddRange(route, currentBoat, busyBoat, tripToReplace, conflictingTrip);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new ReplaceTripBoatCommandHandler(context)
                .Handle(new ReplaceTripBoatCommand(tripToReplace.Id, busyBoat.Id), CancellationToken.None));

        exception.Errors.Values.SelectMany(x => x).Single()
            .ShouldContain("Tàu thay thế đã có chuyến vào ngày/giờ này");
    }

    [Test]
    public async Task PreviewRoundTripScheduleAlternatesOutboundAndInboundAfterTurnaroundAtCurrentStation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var stationA = Station("A", "Ben A");
        var stationB = Station("B", "Ben B");
        var outbound = Route("OUT", stationA, stationB);
        var inbound = Route("IN", stationB, stationA);
        outbound.RouteStops.Single(x => x.StopOrder == 2).StandardTravelMin = 88;
        inbound.RouteStops.Single(x => x.StopOrder == 2).StandardTravelMin = 88;
        outbound.IsBookable = true;
        inbound.IsBookable = true;
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);
        var date = new DateOnly(2030, 1, 1);
        var start = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));

        context.AddRange(outbound, inbound, boat);
        await context.SaveChangesAsync();
        await AddRequiredOnBoardStaffAsync(context, boat, start);

        var result = await new PreviewRoundTripScheduleCommandHandler(context)
            .Handle(
                new PreviewRoundTripScheduleCommand(
                    BoatCode: "BOAT-1",
                    OutboundRouteCode: "OUT",
                    InboundRouteCode: "IN",
                    FromDate: date,
                    ToDate: date,
                    StartTime: new TimeOnly(8, 0),
                    EndTime: new TimeOnly(11, 30)),
                CancellationToken.None);

        result.Suggested.ShouldBe(3);
        result.SkippedBoatBusy.ShouldBe(0);
        result.Items.Select(x => x.Direction).ShouldBe(["Outbound", "Inbound", "Outbound"]);
        result.Items.Select(x => x.DepartureTime.ToOffset(TimeSpan.FromHours(7)).TimeOfDay)
            .ShouldBe([
                new TimeSpan(8, 0, 0),
                new TimeSpan(9, 33, 0),
                new TimeSpan(11, 6, 0)
            ]);
        result.Items.Select(x => x.ArrivalTime.ToOffset(TimeSpan.FromHours(7)).TimeOfDay)
            .ShouldBe([
                new TimeSpan(9, 28, 0),
                new TimeSpan(11, 1, 0),
                new TimeSpan(12, 34, 0)
            ]);
        result.Items.ShouldAllBe(x => x.CanCreate);
    }

    [Test]
    public async Task PreviewRoundTripScheduleRejectsRegularRouteMissingDistance()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var stationA = Station("A", "Ben A");
        var stationB = Station("B", "Ben B");
        var outbound = Route("OUT", stationA, stationB);
        var inbound = Route("IN", stationB, stationA);
        inbound.RouteStops.Single(x => x.StopOrder == 2).DistanceFromPreviousKm = null;
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);
        var date = new DateOnly(2030, 1, 1);

        context.AddRange(outbound, inbound, boat);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new PreviewRoundTripScheduleCommandHandler(context)
                .Handle(
                    new PreviewRoundTripScheduleCommand(
                        BoatCode: "BOAT-1",
                        OutboundRouteCode: "OUT",
                        InboundRouteCode: "IN",
                        FromDate: date,
                        ToDate: date,
                        StartTime: new TimeOnly(8, 0),
                        EndTime: new TimeOnly(11, 30)),
                    CancellationToken.None));

        exception.Errors["inboundRouteCode"].Single().ShouldContain("distanceFromPreviousKm");
    }

    [Test]
    public async Task GenerateTripsCreatesEveryRoundTripPreviewItemWhenScheduledOneByOne()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var stationA = Station("A", "Ben A");
        var stationB = Station("B", "Ben B");
        var outbound = Route("OUT", stationA, stationB);
        var inbound = Route("IN", stationB, stationA);
        outbound.RouteStops.Single(x => x.StopOrder == 2).StandardTravelMin = 88;
        inbound.RouteStops.Single(x => x.StopOrder == 2).StandardTravelMin = 88;
        outbound.IsBookable = true;
        inbound.IsBookable = true;
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);
        var date = new DateOnly(2030, 1, 1);
        var start = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));

        context.AddRange(outbound, inbound, boat);
        await context.SaveChangesAsync();
        await AddRequiredOnBoardStaffAsync(context, boat, start);

        var preview = await new PreviewRoundTripScheduleCommandHandler(context)
            .Handle(
                new PreviewRoundTripScheduleCommand(
                    BoatCode: "BOAT-1",
                    OutboundRouteCode: "OUT",
                    InboundRouteCode: "IN",
                    FromDate: date,
                    ToDate: date,
                    StartTime: new TimeOnly(8, 0),
                    EndTime: new TimeOnly(11, 30)),
                CancellationToken.None);
        preview.Items.ShouldAllBe(x => x.CanCreate);

        var generateHandler = new GenerateTripsCommandHandler(context);
        foreach (var item in preview.Items)
        {
            var localDeparture = item.DepartureTime.ToOffset(TimeSpan.FromHours(7));
            var result = await generateHandler.Handle(
                new GenerateTripsCommand(
                    RouteCode: item.RouteCode,
                    BoatCode: "BOAT-1",
                    DepartureTimes: [TimeOnly.FromDateTime(localDeparture.DateTime)],
                    FromDate: item.OperatingDate,
                    ToDate: item.OperatingDate),
                CancellationToken.None);

            result.Created.ShouldBe(1, result.SkippedItems?.SingleOrDefault()?.Reason ?? "Trip should be created.");
            result.SkippedItems.ShouldBeEmpty();
        }

        context.Trips
            .OrderBy(x => x.DepartureTime)
            .Select(x => new
            {
                RouteCode = x.Route.RouteCode,
                Departure = x.DepartureTime.ToOffset(TimeSpan.FromHours(7)).TimeOfDay
            })
            .ShouldBe([
                new { RouteCode = "OUT", Departure = new TimeSpan(8, 0, 0) },
                new { RouteCode = "IN", Departure = new TimeSpan(9, 33, 0) },
                new { RouteCode = "OUT", Departure = new TimeSpan(11, 6, 0) }
            ]);
    }

    [Test]
    public async Task PreviewRoundTripScheduleRoundsFractionalTravelMinutesUpBeforeSchedulingNextTrip()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var stationA = Station("A", "Ben A");
        var stationB = Station("B", "Ben B");
        var outbound = Route("OUT", stationA, stationB);
        var inbound = Route("IN", stationB, stationA);
        outbound.RouteStops.Single(x => x.StopOrder == 2).StandardTravelMin = 88.5m;
        inbound.RouteStops.Single(x => x.StopOrder == 2).StandardTravelMin = 88.5m;
        outbound.IsBookable = true;
        inbound.IsBookable = true;
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);
        var date = new DateOnly(2030, 1, 1);
        var start = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));

        context.AddRange(outbound, inbound, boat);
        await context.SaveChangesAsync();
        await AddRequiredOnBoardStaffAsync(context, boat, start);

        var preview = await new PreviewRoundTripScheduleCommandHandler(context)
            .Handle(
                new PreviewRoundTripScheduleCommand(
                    BoatCode: "BOAT-1",
                    OutboundRouteCode: "OUT",
                    InboundRouteCode: "IN",
                    FromDate: date,
                    ToDate: date,
                    StartTime: new TimeOnly(8, 0),
                    EndTime: new TimeOnly(11, 40)),
                CancellationToken.None);

        preview.Items.ShouldAllBe(x => x.CanCreate);
        preview.Items.Select(x => x.DepartureTime.ToOffset(TimeSpan.FromHours(7)).TimeOfDay)
            .ShouldBe([
                new TimeSpan(8, 0, 0),
                new TimeSpan(9, 34, 0),
                new TimeSpan(11, 8, 0)
            ]);
        preview.Items.Select(x => x.ArrivalTime.ToOffset(TimeSpan.FromHours(7)).TimeOfDay)
            .ShouldBe([
                new TimeSpan(9, 29, 0),
                new TimeSpan(11, 3, 0),
                new TimeSpan(12, 37, 0)
            ]);

        var generateHandler = new GenerateTripsCommandHandler(context);
        foreach (var item in preview.Items)
        {
            var localDeparture = item.DepartureTime.ToOffset(TimeSpan.FromHours(7));
            var result = await generateHandler.Handle(
                new GenerateTripsCommand(
                    RouteCode: item.RouteCode,
                    BoatCode: "BOAT-1",
                    DepartureTimes: [TimeOnly.FromDateTime(localDeparture.DateTime)],
                    FromDate: item.OperatingDate,
                    ToDate: item.OperatingDate),
                CancellationToken.None);

            result.Created.ShouldBe(1, result.SkippedItems?.SingleOrDefault()?.Reason ?? "Trip should be created.");
            result.SkippedItems.ShouldBeEmpty();
        }
    }

    [Test]
    public async Task GenerateTripsContinuousScheduleAddsLayoverAndReturnToStart()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = Route("R1", Station("A", "Ben A"), Station("B", "Ben B"));
        route.IsBookable = true;
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);
        var date = new DateOnly(2030, 1, 1);
        context.AddRange(route, boat);
        await context.SaveChangesAsync();
        await AddRequiredOnBoardStaffAsync(
            context,
            boat,
            new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)));

        var result = await new GenerateTripsCommandHandler(context)
            .Handle(
                new GenerateTripsCommand(
                    RouteCode: "R1",
                    BoatCode: "BOAT-1",
                    DepartureTimes: null,
                    FromDate: date,
                    ToDate: date,
                    StartTime: new TimeOnly(8, 0),
                    EndTime: new TimeOnly(9, 0),
                    IntervalMinutes: 30),
                CancellationToken.None);

        result.Created.ShouldBe(2);
        result.SkippedBoatBusy.ShouldBe(0);
        result.SkippedItems.ShouldBeEmpty();
        result.CreatedTripCodes.ShouldAllBe(x => x.StartsWith("BB-20300101-R1-", StringComparison.Ordinal));
        context.Trips
            .OrderBy(x => x.DepartureTime)
            .Select(x => x.DepartureTime.ToOffset(TimeSpan.FromHours(7)).TimeOfDay)
            .ShouldBe([
                new TimeSpan(8, 0, 0),
                new TimeSpan(9, 0, 0)
            ]);
    }

    [Test]
    public async Task PreviewTripsScheduleWarnsForBlockedFixedDepartureTimesWithoutCreatingTrips()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = Route("R1", Station("A", "Ben A"), Station("B", "Ben B"));
        route.IsBookable = true;
        route.RouteStops.Single(x => x.StopOrder == 2).StandardTravelMin = 70;
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);
        var date = new DateOnly(2030, 1, 1);
        var firstDeparture = new DateTimeOffset(2030, 1, 1, 18, 0, 0, TimeSpan.FromHours(7));
        context.AddRange(route, boat);
        await context.SaveChangesAsync();
        await AddRequiredOnBoardStaffAsync(context, boat, firstDeparture);

        var result = await new PreviewTripsScheduleCommandHandler(context)
            .Handle(
                new PreviewTripsScheduleCommand(
                    RouteCode: "R1",
                    BoatCode: "BOAT-1",
                    DepartureTimes:
                    [
                        new TimeOnly(18, 0),
                        new TimeOnly(18, 30),
                        new TimeOnly(19, 0)
                    ],
                    FromDate: date,
                    ToDate: date),
                CancellationToken.None);

        result.WouldCreate.ShouldBe(1);
        result.WouldSkip.ShouldBe(2);
        result.HasWarnings.ShouldBeTrue();
        result.SkippedBoatBusy.ShouldBe(2);
        result.Items.Select(x => new
            {
                Departure = x.RequestedDepartureTime.ToOffset(TimeSpan.FromHours(7)).TimeOfDay,
                x.CanCreate
            })
            .ShouldBe([
                new { Departure = new TimeSpan(18, 0, 0), CanCreate = true },
                new { Departure = new TimeSpan(18, 30, 0), CanCreate = false },
                new { Departure = new TimeSpan(19, 0, 0), CanCreate = false }
            ]);
        result.Items.Skip(1).ShouldAllBe(x => x.Reason!.Contains("Chuyến mới chỉ được khởi hành sớm nhất"));
        context.Trips.Count().ShouldBe(0);
    }

    [Test]
    public async Task GenerateTripsRejectsRegularRouteMissingDistance()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = Route("R1", Station("A", "Ben A"), Station("B", "Ben B"));
        route.IsBookable = true;
        route.RouteStops.Single(x => x.StopOrder == 2).DistanceFromPreviousKm = null;
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);
        var date = new DateOnly(2030, 1, 1);
        context.AddRange(route, boat);
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new GenerateTripsCommandHandler(context)
                .Handle(
                    new GenerateTripsCommand(
                        RouteCode: "R1",
                        BoatCode: "BOAT-1",
                        DepartureTimes: [new TimeOnly(8, 0)],
                        FromDate: date,
                        ToDate: date),
                    CancellationToken.None));

        exception.Errors["routeCode"].Single().ShouldContain("distanceFromPreviousKm");
        context.Trips.Count().ShouldBe(0);
    }

    [Test]
    public async Task GenerateTripsReportsEarliestDepartureWhenStationIsBusy()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var stationA = Station("A", "Ben A");
        var routeA = Route("R1", stationA, Station("B", "Ben B"));
        var routeB = Route("R2", stationA, Station("C", "Ben C"));
        routeB.IsBookable = true;
        var existingBoat = BoatWithSeats("BOAT-1", seatCount: 3);
        var newBoat = BoatWithSeats("BOAT-2", seatCount: 3);
        var existingDeparture = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var existingTrip = new Trip
        {
            Route = routeA,
            RouteId = routeA.Id,
            Boat = existingBoat,
            BoatId = existingBoat.Id,
            TripCode = "TR-STATION",
            OperatingDate = DateOnly.FromDateTime(existingDeparture.Date),
            DepartureTime = existingDeparture.ToUniversalTime(),
            ArrivalTime = existingDeparture.AddMinutes(30).ToUniversalTime(),
            CapacitySnapshot = 3,
            TripStatus = TripStatus.Scheduled
        };
        context.AddRange(routeA, routeB, existingBoat, newBoat, existingTrip);
        await context.SaveChangesAsync();

        var result = await new GenerateTripsCommandHandler(context)
            .Handle(
                new GenerateTripsCommand(
                    RouteCode: "R2",
                    BoatCode: "BOAT-2",
                    DepartureTimes: [new TimeOnly(8, 3)],
                    FromDate: DateOnly.FromDateTime(existingDeparture.Date),
                    ToDate: DateOnly.FromDateTime(existingDeparture.Date)),
                CancellationToken.None);

        result.Created.ShouldBe(0);
        result.SkippedStationBusy.ShouldBe(1);
        result.SkippedItems.ShouldNotBeNull();
        var skippedItem = result.SkippedItems.Single();
        skippedItem.ConflictTripCode.ShouldBe("TR-STATION");
        skippedItem.EarliestAllowedDepartureTime!.Value.ToOffset(TimeSpan.FromHours(7)).TimeOfDay
            .ShouldBe(new TimeSpan(8, 5, 0));
        skippedItem.Reason.ShouldContain("Các chuyến cùng bến phải cách nhau tối thiểu 5 phút");
    }

    [Test]
    public async Task CreateTripAllowsReverseDirectionAfterTurnaroundAtArrivalStation()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var stationA = Station("A", "Ben A");
        var stationB = Station("B", "Ben B");
        var outbound = Route("OUT", stationA, stationB);
        var inbound = Route("IN", stationB, stationA);
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);
        var firstDeparture = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        context.AddRange(outbound, inbound, boat);
        await context.SaveChangesAsync();
        await AddRequiredOnBoardStaffAsync(context, boat, firstDeparture);

        var handler = new CreateTripCommandHandler(context);
        await handler.Handle(
            new CreateTripCommand("OUT", "BOAT-1", DateOnly.FromDateTime(firstDeparture.Date), firstDeparture),
            CancellationToken.None);

        var reverseDeparture = firstDeparture.AddMinutes(30);
        var result = await handler.Handle(
            new CreateTripCommand("IN", "BOAT-1", DateOnly.FromDateTime(reverseDeparture.Date), reverseDeparture),
            CancellationToken.None);

        result.RouteCode.ShouldBe("IN");
    }

    [Test]
    public async Task GenerateTripsUsesStopStayDurationForIntermediateStops()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = Route("R1", Station("A", "Ben A"), Station("B", "Ben B"), Station("C", "Ben C"));
        route.IsBookable = true;
        route.RouteStops.Single(x => x.StopOrder == 2).StandardTravelMin = 20;
        route.RouteStops.Single(x => x.StopOrder == 3).StandardTravelMin = 20;
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);
        var date = new DateOnly(2030, 1, 1);
        context.AddRange(route, boat);
        await context.SaveChangesAsync();
        await AddRequiredOnBoardStaffAsync(
            context,
            boat,
            new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)));

        var result = await new GenerateTripsCommandHandler(context)
            .Handle(
                new GenerateTripsCommand(
                    RouteCode: "R1",
                    BoatCode: "BOAT-1",
                    DepartureTimes: [new TimeOnly(8, 0)],
                    FromDate: date,
                    ToDate: date,
                    Stops: [new CreateTripStopScheduleInput(2, 5)]),
                CancellationToken.None);

        result.Created.ShouldBe(1);
        var trip = await context.Trips
            .Include(x => x.TripStops)
            .SingleAsync();
        var stop = trip.TripStops.Single(x => x.StopOrder == 2);
        stop.StayDurationMinutes.ShouldBe(5);
        stop.PlannedArrivalTime!.Value.ToOffset(TimeSpan.FromHours(7)).TimeOfDay
            .ShouldBe(new TimeSpan(8, 20, 0));
        stop.PlannedDepartureTime!.Value.ToOffset(TimeSpan.FromHours(7)).TimeOfDay
            .ShouldBe(new TimeSpan(8, 25, 0));
        trip.ArrivalTime.ToOffset(TimeSpan.FromHours(7)).TimeOfDay.ShouldBe(new TimeSpan(8, 45, 0));
    }

    [Test]
    public async Task GenerateTripsSupportsContinuousSightseeingSchedule()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var route = Route("SIGHT-1", Station("A", "Ben A"), Station("B", "Ben B"));
        route.RouteType = RouteTypes.SightseeingLoop;
        route.IsBookable = true;
        var boat = BoatWithSeats("BOAT-SIGHT", seatCount: 3, seatSetupType: SeatSetupType.StandardAndVip);
        var date = new DateOnly(2030, 1, 1);
        context.AddRange(route, boat);
        await context.SaveChangesAsync();
        await AddRequiredOnBoardStaffAsync(
            context,
            boat,
            new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7)));

        var result = await new GenerateTripsCommandHandler(context)
            .Handle(
                new GenerateTripsCommand(
                    RouteCode: "SIGHT-1",
                    BoatCode: "BOAT-SIGHT",
                    DepartureTimes: null,
                    FromDate: date,
                    ToDate: date,
                    StartTime: new TimeOnly(8, 0),
                    EndTime: new TimeOnly(9, 0),
                    IntervalMinutes: 30),
                CancellationToken.None);

        result.Created.ShouldBe(2);
        result.CreatedTripCodes.ShouldAllBe(x => x.StartsWith("BS-20300101-SIGHT-1-", StringComparison.Ordinal));
        context.Trips.All(x => x.RouteId == route.Id).ShouldBeTrue();
    }

    [Test]
    public async Task GenerateTripsContinuousSightseeingScheduleUsesArrivalPlusLayover()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var station = Station("BD", "Bến Bạch Đằng");
        var route = Route("LOOP-BD", station, station);
        route.RouteType = RouteTypes.SightseeingLoop;
        route.IsBookable = true;
        route.EstimatedDurationMin = 49.94m;
        route.RouteStops.Single(x => x.StopOrder == 2).StandardTravelMin = null;
        var boat = BoatWithSeats("BOAT-SIGHT", seatCount: 3, seatSetupType: SeatSetupType.StandardAndVip);
        var date = new DateOnly(2030, 1, 1);
        context.AddRange(route, boat);
        await context.SaveChangesAsync();
        await AddRequiredOnBoardStaffAsync(
            context,
            boat,
            new DateTimeOffset(2030, 1, 1, 18, 0, 0, TimeSpan.FromHours(7)));

        var result = await new GenerateTripsCommandHandler(context)
            .Handle(
                new GenerateTripsCommand(
                    RouteCode: "LOOP-BD",
                    BoatCode: "BOAT-SIGHT",
                    DepartureTimes: null,
                    FromDate: date,
                    ToDate: date,
                    StartTime: new TimeOnly(18, 0),
                    EndTime: new TimeOnly(20, 10),
                    IntervalMinutes: 15),
                CancellationToken.None);

        result.Created.ShouldBe(3);
        result.SkippedBoatBusy.ShouldBe(0);
        result.SkippedItems.ShouldBeEmpty();
        context.Trips
            .OrderBy(x => x.DepartureTime)
            .Select(x => new
            {
                Departure = x.DepartureTime.ToOffset(TimeSpan.FromHours(7)).TimeOfDay,
                Arrival = x.ArrivalTime.ToOffset(TimeSpan.FromHours(7)).TimeOfDay
            })
            .ShouldBe([
                new { Departure = new TimeSpan(18, 0, 0), Arrival = new TimeSpan(18, 50, 0) },
                new { Departure = new TimeSpan(19, 5, 0), Arrival = new TimeSpan(19, 55, 0) },
                new { Departure = new TimeSpan(20, 10, 0), Arrival = new TimeSpan(21, 0, 0) }
            ]);
    }

    [TestCase(30, "18:00:00,18:40:00,19:20:00,20:00:00")]
    [TestCase(45, "18:00:00,18:55:00,19:50:00")]
    [TestCase(60, "18:00:00,19:10:00")]
    public async Task GenerateTripsUsesSelectedIntervalAsPostArrivalLayover(
        int intervalMinutes,
        string expectedDepartures)
    {
        await using var context = SeatFlowTestData.CreateContext();
        var station = Station("BD", "Bến Bạch Đằng");
        var route = Route("LOOP-BD", station, station);
        route.RouteType = RouteTypes.SightseeingLoop;
        route.IsBookable = true;
        route.EstimatedDurationMin = 10m;
        route.RouteStops.Single(x => x.StopOrder == 2).StandardTravelMin = null;
        var boat = BoatWithSeats("BOAT-SIGHT", seatCount: 3, seatSetupType: SeatSetupType.StandardAndVip);
        var date = new DateOnly(2030, 1, 1);
        context.AddRange(route, boat);
        await context.SaveChangesAsync();
        await AddRequiredOnBoardStaffAsync(
            context,
            boat,
            new DateTimeOffset(2030, 1, 1, 18, 0, 0, TimeSpan.FromHours(7)));

        var result = await new GenerateTripsCommandHandler(context)
            .Handle(
                new GenerateTripsCommand(
                    RouteCode: "LOOP-BD",
                    BoatCode: "BOAT-SIGHT",
                    DepartureTimes: null,
                    FromDate: date,
                    ToDate: date,
                    StartTime: new TimeOnly(18, 0),
                    EndTime: new TimeOnly(20, 0),
                    IntervalMinutes: intervalMinutes),
                CancellationToken.None);

        var expected = expectedDepartures
            .Split(',')
            .Select(TimeSpan.Parse)
            .ToArray();
        result.Created.ShouldBe(expected.Length);
        result.SkippedBoatBusy.ShouldBe(0);
        context.Trips
            .OrderBy(x => x.DepartureTime)
            .Select(x => x.DepartureTime.ToOffset(TimeSpan.FromHours(7)).TimeOfDay)
            .ShouldBe(expected);
    }

    [Test]
    public async Task SearchTripsDoesNotMarkFutureTripsClosedWhenRouteDistanceIsMissing()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var stationA = Station("A", "Ben A");
        var stationB = Station("B", "Ben B");
        var route = Route("R1", stationA, stationB);
        route.RouteStops.Single(x => x.StopOrder == 2).DistanceFromPreviousKm = null;
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var trip = TripWithSeats(route, boat, "TR-MISSING-KM", departureTime);

        context.AddRange(route, boat, trip);
        await context.SaveChangesAsync();

        var result = await new SearchTripsQueryHandler(
                context,
                new FixedTimeProvider(departureTime.ToUniversalTime().AddDays(-1)))
            .Handle(new SearchTripsQuery(stationA.Id, stationB.Id, DateOnly.FromDateTime(departureTime.Date)), CancellationToken.None);

        var tripSummary = result.Single();
        tripSummary.IsBookingClosed.ShouldBeFalse();
        tripSummary.IsBookable.ShouldBeFalse();
        tripSummary.BookingClosedReason.ShouldNotBeNull();
        tripSummary.BookingClosedReason.ShouldContain("chưa nhập đủ số km");
    }

    [Test]
    public async Task RunningTripVisibilityDependsOnBoardingStopAndStatus()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var customerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        var stationA = Station("A", "Ben A");
        var stationB = Station("B", "Ben B");
        var stationC = Station("C", "Ben C");
        var route = Route("R1", stationA, stationB, stationC);
        var boat = BoatWithSeats("BOAT-1", seatCount: 3);
        var departureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.FromHours(7));
        var operatingDate = DateOnly.FromDateTime(departureTime.Date);

        context.AddRange(route, boat);
        await context.SaveChangesAsync();
        await AddRequiredOnBoardStaffAsync(context, boat, departureTime);

        var created = await new CreateTripCommandHandler(context)
            .Handle(
                new CreateTripCommand(
                    "R1",
                    "BOAT-1",
                    operatingDate,
                    departureTime,
                    Stops: [new CreateTripStopScheduleInput(2, 0)]),
                CancellationToken.None);

        created.TripStatus.ShouldBe(nameof(TripStatus.Scheduled));

        var preDepartureSearch = await new SearchTripsQueryHandler(
                context,
                new FixedTimeProvider(departureTime.ToUniversalTime().AddMinutes(-30)))
            .Handle(new SearchTripsQuery(stationA.Id, stationB.Id, operatingDate), CancellationToken.None);
        preDepartureSearch.ShouldContain(x => x.TripId == created.TripId);

        var runningNow = departureTime.ToUniversalTime().AddMinutes(1);
        var running = await new UpdateTripStatusCommandHandler(context, new FixedTimeProvider(runningNow))
            .Handle(new UpdateTripStatusCommand(created.TripId, TripStatus.InProgress, null), CancellationToken.None);
        running.TripStatus.ShouldBe(nameof(TripStatus.InProgress));

        var adminList = await new GetTripListQueryHandler(context)
            .Handle(new GetTripListQuery(operatingDate, null, null), CancellationToken.None);
        adminList.Single(x => x.TripId == created.TripId).TripStatus.ShouldBe(nameof(TripStatus.InProgress));

        var runningSearchFromDepartedStop = await new SearchTripsQueryHandler(context, new FixedTimeProvider(runningNow))
            .Handle(new SearchTripsQuery(stationA.Id, stationB.Id, operatingDate), CancellationToken.None);
        runningSearchFromDepartedStop.Any(x => x.TripId == created.TripId).ShouldBeFalse();

        var runningSearchFromNextStop = await new SearchTripsQueryHandler(context, new FixedTimeProvider(runningNow))
            .Handle(new SearchTripsQuery(stationB.Id, stationC.Id, operatingDate), CancellationToken.None);
        runningSearchFromNextStop.ShouldContain(x => x.TripId == created.TripId);

        var closedDepartedStopSeatMap = await new GetTripSeatMapQueryHandler(
                context,
                customerContext,
                new FixedTimeProvider(runningNow))
            .Handle(new GetTripSeatMapQuery(created.TripId, "A", "B"), CancellationToken.None);
        closedDepartedStopSeatMap.IsBookingClosed.ShouldBeTrue();

        var openNextStopSeatMap = await new GetTripSeatMapQueryHandler(
                context,
                customerContext,
                new FixedTimeProvider(runningNow))
            .Handle(new GetTripSeatMapQuery(created.TripId, "B", "C"), CancellationToken.None);
        openNextStopSeatMap.IsBookingClosed.ShouldBeFalse();

        // Đã qua hạn bán cho bến B: lấy giờ tàu rời B trừ đúng cutoff rồi cộng 1 phút, để test không
        // bám vào con số cụ thể của BookingCutoffBeforeDeparture.
        var boardingAtB = departureTime.ToUniversalTime()
            .AddMinutes((double)TripStopScheduleSupport.DefaultTravelMinutes);
        var withinNextStopCutoff = boardingAtB
            .Subtract(Application.Common.BookingExpirationPolicy.BookingCutoffBeforeDeparture)
            .AddMinutes(1);
        var cutoffSearchFromNextStop = await new SearchTripsQueryHandler(context, new FixedTimeProvider(withinNextStopCutoff))
            .Handle(new SearchTripsQuery(stationB.Id, stationC.Id, operatingDate), CancellationToken.None);
        cutoffSearchFromNextStop.Any(x => x.TripId == created.TripId).ShouldBeFalse();

        var cutoffNextStopSeatMap = await new GetTripSeatMapQueryHandler(
                context,
                customerContext,
                new FixedTimeProvider(withinNextStopCutoff))
            .Handle(new GetTripSeatMapQuery(created.TripId, "B", "C"), CancellationToken.None);
        cutoffNextStopSeatMap.IsBookingClosed.ShouldBeTrue();

        await new UpdateTripStatusCommandHandler(context, new FixedTimeProvider(runningNow.AddMinutes(30)))
            .Handle(new UpdateTripStatusCommand(created.TripId, TripStatus.Completed, null), CancellationToken.None);

        var completedCustomerSearch = await new SearchTripsQueryHandler(context, new FixedTimeProvider(runningNow))
            .Handle(new SearchTripsQuery(stationB.Id, stationC.Id, operatingDate), CancellationToken.None);
        completedCustomerSearch.Any(x => x.TripId == created.TripId).ShouldBeFalse();

        adminList = await new GetTripListQueryHandler(context)
            .Handle(new GetTripListQuery(operatingDate, null, null), CancellationToken.None);
        adminList.Single(x => x.TripId == created.TripId).TripStatus.ShouldBe(nameof(TripStatus.Completed));
    }

    private static Boat BoatWithSeats(
        string code,
        int seatCount,
        int inactiveSeatCount = 0,
        SeatSetupType seatSetupType = SeatSetupType.FullStandard)
    {
        var boat = new Boat
        {
            Code = code,
            Name = code,
            Status = BoatStatus.Active,
            SeatCount = seatCount,
            NumberOfDecks = 1,
            SeatSetupType = seatSetupType,
            SeatsConfigured = true
        };

        for (var i = 0; i < seatCount; i++)
        {
            boat.Seats.Add(new Seat
            {
                Boat = boat,
                BoatId = boat.Id,
                Code = $"A{i + 1}",
                SeatTypeCode = "STANDARD",
                Deck = 1,
                Row = "A",
                Column = i + 1,
                IsActive = i >= inactiveSeatCount
            });
        }

        return boat;
    }

    private static Trip TripWithSeats(
        Route route,
        Boat boat,
        string tripCode,
        DateTimeOffset departureTime)
    {
        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            Boat = boat,
            BoatId = boat.Id,
            TripCode = tripCode,
            OperatingDate = DateOnly.FromDateTime(departureTime.Date),
            DepartureTime = departureTime.ToUniversalTime(),
            ArrivalTime = departureTime.AddMinutes(30).ToUniversalTime(),
            CapacitySnapshot = boat.Seats.Count(x => x.IsActive),
            TripStatus = TripStatus.Scheduled
        };

        foreach (var seat in boat.Seats.Where(x => x.IsActive))
        {
            trip.TripSeats.Add(new TripSeat
            {
                Trip = trip,
                TripId = trip.Id,
                Seat = seat,
                SeatId = seat.Id,
                Status = TripSeat.StatusAvailable
            });
        }

        return trip;
    }

    private static async Task AddRequiredOnBoardStaffAsync(
        ApplicationDbContext context,
        Boat boat,
        DateTimeOffset departureTime)
    {
        var firstStaff = await SeatFlowTestData.SeedStaffAsync(context, StaffType.OnBoard);
        var secondStaff = await SeatFlowTestData.SeedStaffAsync(context, StaffType.OnBoard);
        var startAt = departureTime.AddHours(-1).ToUniversalTime();
        var endAt = departureTime.AddHours(6).ToUniversalTime();
        context.StaffWorkAssignments.AddRange(
            OnBoardAssignment(firstStaff.UserId!.Value, boat.Id, startAt, endAt),
            OnBoardAssignment(secondStaff.UserId!.Value, boat.Id, startAt, endAt));
        await context.SaveChangesAsync();
    }

    private static StaffWorkAssignment OnBoardAssignment(
        Guid staffUserId,
        Guid boatId,
        DateTimeOffset startAt,
        DateTimeOffset endAt) =>
        new()
        {
            StaffUserId = staffUserId,
            AssignmentType = StaffWorkAssignmentType.Boat,
            BoatId = boatId,
            WorkingDate = DateOnly.FromDateTime(startAt.ToOffset(TimeSpan.FromHours(7)).Date),
            StartAt = startAt,
            EndAt = endAt,
            Status = StaffWorkAssignmentStatus.Scheduled,
            AssignedByUserId = staffUserId,
            AssignedAt = startAt.AddHours(-1)
        };

    private static Station Station(string code, string name) =>
        new()
        {
            StationCode = code,
            StationName = name,
            Status = StationStatus.Active
        };

    private static Route Route(string code, params Station[] stations)
    {
        var route = new Route
        {
            RouteCode = code,
            RouteName = code,
            Status = "Active"
        };

        var stopOrder = 1;
        foreach (var station in stations)
        {
            route.RouteStops.Add(new RouteStop
            {
                Route = route,
                Station = station,
                StationId = station.Id,
                StopOrder = stopOrder,
                StandardTravelMin = stopOrder == 1 ? null : 15,
                DistanceFromPreviousKm = stopOrder == 1 ? null : 3m
            });
            stopOrder++;
        }

        return route;
    }
}
