using NUnit.Framework;
using NetTopologySuite.Geometries;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CharterBookings;

public class CreateCharterBookingTripCommandTests
{
    [Test]
    public async Task CreatesCharterTripWhenRouteMatchesItinerary()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var (stationA, stationB, stationC) = Stations();
        var boat = CharterBoat("Boat 1");
        var route = RouteWithStops("CH-REF-1", RouteTypes.CharterReference, stationA, stationB, stationC);
        var booking = ConfirmedCharterBooking(stationA, stationC, boat);
        AddItineraryStop(booking, stationB, stopOrder: 1);
        context.AddRange(stationA, stationB, stationC, boat, route, booking);
        await context.SaveChangesAsync();

        var handler = new CreateCharterBookingTripCommandHandler(context, admin);

        var result = await handler.Handle(
            new CreateCharterBookingTripCommand(booking.Id),
            CancellationToken.None);

        result.RouteId.ShouldBe(route.Id);
        result.Trips.Count.ShouldBe(1);
        var trip = context.Set<Trip>().Single(x => x.Id == result.Trips[0].TripId);
        trip.TripCode.ShouldBe($"BR-20300101-{booking.BookingCode}-1");
        trip.TripType.ShouldBe(TripTypes.Charter);
        trip.SourceBookingId.ShouldBe(booking.Id);
        trip.RouteId.ShouldBe(route.Id);
        trip.TripStatus.ShouldBe(TripStatus.Scheduled);
        trip.OperatingDate.ShouldBe(booking.DepartureDate!.Value);
        // 08:00 gio VN = 01:00 UTC.
        trip.DepartureTime.ShouldBe(new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero));
        // Thue theo gio, durationValue = 3.
        trip.ArrivalTime.ShouldBe(trip.DepartureTime.AddHours(3));
        var tripStops = context.Set<TripStop>()
            .Where(x => x.TripId == trip.Id)
            .OrderBy(x => x.StopOrder)
            .ToList();
        tripStops.Count.ShouldBe(3);
        tripStops.Select(x => x.StationId).ShouldBe([stationA.Id, stationB.Id, stationC.Id]);
        tripStops[1].StayDurationMinutes.ShouldBe(20);
        context.Set<CharterBookingBoat>().Single().TripId.ShouldBe(trip.Id);
        context.Set<Booking>().Single(x => x.Id == booking.Id).TripId.ShouldBe(trip.Id);
    }

    [Test]
    public async Task CreatesOneTripPerBoat()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var (stationA, stationB, stationC) = Stations();
        var boat1 = CharterBoat("Boat 1");
        var boat2 = CharterBoat("Boat 2");
        var route = RouteWithStops("CH-REF-2", RouteTypes.CharterReference, stationA, stationB, stationC);
        var booking = ConfirmedCharterBooking(stationA, stationC, boat1, boat2);
        AddItineraryStop(booking, stationB, stopOrder: 1);
        context.AddRange(stationA, stationB, stationC, boat1, boat2, route, booking);
        await context.SaveChangesAsync();

        var handler = new CreateCharterBookingTripCommandHandler(context, admin);

        var result = await handler.Handle(
            new CreateCharterBookingTripCommand(booking.Id),
            CancellationToken.None);

        result.Trips.Count.ShouldBe(2);
        context.Set<Trip>().Count().ShouldBe(2);
        context.Set<Trip>().Select(x => x.TripCode).Distinct().Count().ShouldBe(2);
        context.Set<CharterBookingBoat>().Count(x => x.TripId != null).ShouldBe(2);
        // Booking.TripId tro ve trip cua tau chinh (boatOrder 1).
        var primaryTripId = context.Set<CharterBookingBoat>().Single(x => x.BoatOrder == 1).TripId;
        context.Set<Booking>().Single(x => x.Id == booking.Id).TripId.ShouldBe(primaryTripId);
    }

    [Test]
    public async Task FailsWhenNoRouteMatchesItinerary()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var (stationA, stationB, stationC) = Stations();
        var boat = CharterBoat("Boat 1");
        // Route chi co A -> C, thieu diem dung B -> khong khop.
        var route = RouteWithStops("CH-REF-3", RouteTypes.CharterReference, stationA, stationC);
        var booking = ConfirmedCharterBooking(stationA, stationC, boat);
        AddItineraryStop(booking, stationB, stopOrder: 1);
        context.AddRange(stationA, stationB, stationC, boat, route, booking);
        await context.SaveChangesAsync();

        var handler = new CreateCharterBookingTripCommandHandler(context, admin);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(new CreateCharterBookingTripCommand(booking.Id), CancellationToken.None));

        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(x => x.Contains("Không có route nào khớp"));
        context.Set<Trip>().Count().ShouldBe(0);
    }

    [Test]
    public async Task FailsWhenRouteHasSameStationsInDifferentOrder()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var (stationA, stationB, stationC) = Stations();
        var boat = CharterBoat("Boat 1");
        // Cung 3 ben nhung sai thu tu (A -> C -> B).
        var route = RouteWithStops("CH-REF-4", RouteTypes.CharterReference, stationA, stationC, stationB);
        var booking = ConfirmedCharterBooking(stationA, stationC, boat);
        AddItineraryStop(booking, stationB, stopOrder: 1);
        context.AddRange(stationA, stationB, stationC, boat, route, booking);
        await context.SaveChangesAsync();

        var handler = new CreateCharterBookingTripCommandHandler(context, admin);

        await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(new CreateCharterBookingTripCommand(booking.Id), CancellationToken.None));
    }

    [Test]
    public async Task MatchesLoopItineraryWithLoopRoute()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var (stationA, stationB, stationC) = Stations();
        var boat = CharterBoat("Boat 1");
        // Lo trinh vong: A -> B -> C -> A; route cung phai vong nhu vay.
        var route = RouteWithStops("CH-LOOP-1", RouteTypes.CharterReference, stationA, stationB, stationC, stationA);
        var booking = ConfirmedCharterBooking(stationA, stationA, boat);
        AddItineraryStop(booking, stationB, stopOrder: 1);
        AddItineraryStop(booking, stationC, stopOrder: 2);
        context.AddRange(stationA, stationB, stationC, boat, route, booking);
        await context.SaveChangesAsync();

        var handler = new CreateCharterBookingTripCommandHandler(context, admin);

        var result = await handler.Handle(
            new CreateCharterBookingTripCommand(booking.Id),
            CancellationToken.None);

        result.RouteId.ShouldBe(route.Id);
    }

    [Test]
    public async Task PrefersCharterReferenceRouteWhenMultipleMatch()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var (stationA, stationB, stationC) = Stations();
        var boat = CharterBoat("Boat 1");
        var regularRoute = RouteWithStops("REG-1", RouteTypes.Regular, stationA, stationB, stationC);
        var charterRoute = RouteWithStops("CH-REF-5", RouteTypes.CharterReference, stationA, stationB, stationC);
        var booking = ConfirmedCharterBooking(stationA, stationC, boat);
        AddItineraryStop(booking, stationB, stopOrder: 1);
        context.AddRange(stationA, stationB, stationC, boat, regularRoute, charterRoute, booking);
        await context.SaveChangesAsync();

        var handler = new CreateCharterBookingTripCommandHandler(context, admin);

        var result = await handler.Handle(
            new CreateCharterBookingTripCommand(booking.Id),
            CancellationToken.None);

        result.RouteId.ShouldBe(charterRoute.Id);
    }

    [Test]
    public async Task CreatesSelectedCharterRouteFromRoutePlanAndTripUsesIt()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var (stationA, stationB, stationC) = Stations();
        SetCoordinates(stationA, latitude: 10.0m, longitude: 106.0m);
        SetCoordinates(stationB, latitude: 10.1m, longitude: 106.1m);
        SetCoordinates(stationC, latitude: 10.2m, longitude: 106.2m);
        var boat = CharterBoat("Boat 1");
        var sourceRouteAB = GpsRouteWithStops("GPS-AB", stationA, stationB, travelMinutes: 20);
        var sourceRouteBC = GpsRouteWithStops("GPS-BC", stationB, stationC, travelMinutes: 25);
        var autoMatchedRoute = RouteWithStops("CH-AUTO", RouteTypes.CharterReference, stationA, stationB, stationC);
        var booking = ConfirmedCharterBooking(stationA, stationC, boat);
        booking.DurationValue = 5;
        AddItineraryStop(booking, stationB, stopOrder: 1);
        context.AddRange(stationA, stationB, stationC, boat, sourceRouteAB, sourceRouteBC, autoMatchedRoute, booking);
        await context.SaveChangesAsync();

        var selectedRoute = await CharterBookingRoutePlanSupport.ResolveSelectedRouteAsync(
            context,
            booking,
            [
                new CharterBookingRoutePlanLegRequest(stationA.Id, stationB.Id, sourceRouteAB.Id),
                new CharterBookingRoutePlanLegRequest(stationB.Id, stationC.Id, sourceRouteBC.Id)
            ],
            persist: true,
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);
        booking.CharterRouteId = selectedRoute!.Id;
        booking.CharterRoute = selectedRoute;
        await context.SaveChangesAsync();

        var handler = new CreateCharterBookingTripCommandHandler(context, admin);

        var result = await handler.Handle(
            new CreateCharterBookingTripCommand(booking.Id),
            CancellationToken.None);

        result.RouteId.ShouldBe(selectedRoute.Id);
        selectedRoute.RouteType.ShouldBe(RouteTypes.Charter);
        selectedRoute.IsBookable.ShouldBeFalse();
        selectedRoute.RouteStops.OrderBy(x => x.StopOrder).Select(x => x.StationId)
            .ShouldBe([stationA.Id, stationB.Id, stationC.Id]);
        selectedRoute.RouteStops.Single(x => x.StopOrder == 1).StandardTravelMin.ShouldBeNull();
        selectedRoute.RouteStops.Single(x => x.StopOrder == 2).StandardTravelMin.ShouldBe(20);
        selectedRoute.RouteStops.Single(x => x.StopOrder == 3).StandardTravelMin.ShouldBe(25);
        var routeEstimate = CharterBookingRoutePricingSupport.EstimateRoute(booking, [selectedRoute]);
        var trip = context.Set<Trip>().Single(x => x.SourceBookingId == booking.Id);
        trip.RouteId.ShouldBe(selectedRoute.Id);
        trip.ArrivalTime.ShouldBe(trip.DepartureTime.AddMinutes((double)routeEstimate.EstimatedDurationMinutes));
        trip.ArrivalTime.ShouldNotBe(trip.DepartureTime.AddHours(booking.DurationValue!.Value));
    }

    [Test]
    public async Task FailsWhenBookingIsNotConfirmed()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var (stationA, stationB, _) = Stations();
        var boat = CharterBoat("Boat 1");
        var booking = ConfirmedCharterBooking(stationA, stationB, boat);
        booking.BookingStatus = BookingStatus.Quoted;
        context.AddRange(stationA, stationB, boat, booking);
        await context.SaveChangesAsync();

        var handler = new CreateCharterBookingTripCommandHandler(context, admin);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(new CreateCharterBookingTripCommand(booking.Id), CancellationToken.None));

        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(x => x.Contains("Confirmed"));
    }

    [Test]
    public async Task FailsWhenTripAlreadyCreated()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var (stationA, stationB, _) = Stations();
        var boat = CharterBoat("Boat 1");
        var route = RouteWithStops("CH-REF-6", RouteTypes.CharterReference, stationA, stationB);
        var booking = ConfirmedCharterBooking(stationA, stationB, boat);
        context.AddRange(stationA, stationB, boat, route, booking);
        await context.SaveChangesAsync();

        var handler = new CreateCharterBookingTripCommandHandler(context, admin);
        await handler.Handle(new CreateCharterBookingTripCommand(booking.Id), CancellationToken.None);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(new CreateCharterBookingTripCommand(booking.Id), CancellationToken.None));

        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(x => x.Contains("đã có trip"));
        context.Set<Trip>().Count().ShouldBe(1);
    }

    [Test]
    public async Task FailsWhenBoatHasOverlappingTrip()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var (stationA, stationB, _) = Stations();
        var boat = CharterBoat("Boat 1");
        var route = RouteWithStops("CH-REF-7", RouteTypes.CharterReference, stationA, stationB);
        var booking = ConfirmedCharterBooking(stationA, stationB, boat);
        context.AddRange(stationA, stationB, boat, route, booking);
        await context.SaveChangesAsync();

        // Trip tuyen thuong da chiem tau trong cung khung gio (08:00-11:00 VN ngay 01/01/2030).
        context.Set<Trip>().Add(new Trip
        {
            RouteId = route.Id,
            BoatId = boat.Id,
            TripCode = "TR-EXISTING-1",
            OperatingDate = booking.DepartureDate!.Value,
            DepartureTime = new DateTimeOffset(2030, 1, 1, 2, 0, 0, TimeSpan.Zero),
            ArrivalTime = new DateTimeOffset(2030, 1, 1, 3, 0, 0, TimeSpan.Zero),
            CapacitySnapshot = 4,
            TripStatus = TripStatus.Scheduled
        });
        await context.SaveChangesAsync();

        var handler = new CreateCharterBookingTripCommandHandler(context, admin);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(new CreateCharterBookingTripCommand(booking.Id), CancellationToken.None));

        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(x => x.Contains("TR-EXISTING-1"));
    }

    [Test]
    public async Task DayRentalWithoutStartTimeUsesOperatingDayWindow()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var (stationA, stationB, _) = Stations();
        var boat = CharterBoat("Boat 1");
        var route = RouteWithStops("CH-REF-8", RouteTypes.CharterReference, stationA, stationB);
        var booking = ConfirmedCharterBooking(stationA, stationB, boat);
        booking.StartTime = null;
        booking.RentalUnit = BoatRentalUnit.Day;
        booking.DurationValue = 2;
        context.AddRange(stationA, stationB, boat, route, booking);
        await context.SaveChangesAsync();

        var handler = new CreateCharterBookingTripCommandHandler(context, admin);

        var result = await handler.Handle(
            new CreateCharterBookingTripCommand(booking.Id),
            CancellationToken.None);

        var trip = context.Set<Trip>().Single(x => x.Id == result.Trips[0].TripId);
        // 07:00 gio VN ngay 01/01 = 00:00 UTC ngay 01/01.
        trip.DepartureTime.ShouldBe(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        // 2 ngay van hanh ket thuc luc 23:00 gio VN ngay 02/01 = 16:00 UTC ngay 02/01.
        trip.ArrivalTime.ShouldBe(new DateTimeOffset(2030, 1, 2, 16, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task CancellingBookingCancelsLinkedTrips()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var (stationA, stationB, _) = Stations();
        var boat = CharterBoat("Boat 1");
        var route = RouteWithStops("CH-REF-9", RouteTypes.CharterReference, stationA, stationB);
        var booking = ConfirmedCharterBooking(stationA, stationB, boat);
        context.AddRange(stationA, stationB, boat, route, booking);
        await context.SaveChangesAsync();

        var createHandler = new CreateCharterBookingTripCommandHandler(context, admin);
        await createHandler.Handle(new CreateCharterBookingTripCommand(booking.Id), CancellationToken.None);

        var statusHandler = new UpdateCharterBookingStatusCommandHandler(context, admin);
        await statusHandler.Handle(
            new UpdateCharterBookingStatusCommand(booking.Id, BookingStatus.Cancelled),
            CancellationToken.None);

        var trip = context.Set<Trip>().Single(x => x.SourceBookingId == booking.Id);
        trip.TripStatus.ShouldBe(TripStatus.Cancelled);
        trip.StatusNote.ShouldNotBeNull().ShouldContain(booking.BookingCode);
    }

    private static (Station A, Station B, Station C) Stations() =>
        (Station("ST-A", "Bến A"), Station("ST-B", "Bến B"), Station("ST-C", "Bến C"));

    private static Station Station(string code, string name) =>
        new()
        {
            StationCode = $"{code}-{Guid.NewGuid():N}"[..15],
            StationName = name,
            Status = StationStatus.Active,
            IsWaterbusStation = true
        };

    private static void SetCoordinates(Station station, decimal latitude, decimal longitude)
    {
        station.Latitude = latitude;
        station.Longitude = longitude;
    }

    private static Boat CharterBoat(string name)
    {
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, status: BoatStatus.Active);
        boat.Name = name;
        boat.SeatCount = 40;
        return boat;
    }

    private static Route RouteWithStops(string code, string routeType, params Station[] stations)
    {
        var route = new Route
        {
            RouteCode = $"{code}-{Guid.NewGuid():N}"[..20],
            RouteName = code,
            RouteType = routeType,
            Status = "Active",
            IsBookable = false
        };

        route.RouteStops = stations
            .Select((station, index) => new RouteStop
            {
                Route = route,
                Station = station,
                StationId = station.Id,
                StopOrder = index + 1,
                StandardTravelMin = 15
            })
            .ToList();

        return route;
    }

    private static Route GpsRouteWithStops(string code, Station from, Station to, int travelMinutes)
    {
        var route = RouteWithStops(code, RouteTypes.CharterReference, from, to);
        route.RouteGeometry = new LineString(
        [
            new Coordinate((double)from.Longitude!.Value, (double)from.Latitude!.Value),
            new Coordinate((double)to.Longitude!.Value, (double)to.Latitude!.Value)
        ])
        { SRID = 4326 };
        route.BaseDistanceKm = 1;
        route.EstimatedDurationMin = travelMinutes;
        route.RouteStops.Single(x => x.StopOrder == 1).StandardTravelMin = null;
        route.RouteStops.Single(x => x.StopOrder == 2).StandardTravelMin = travelMinutes;
        return route;
    }

    private static Booking ConfirmedCharterBooking(Station fromStation, Station toStation, params Boat[] boats)
    {
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = $"CB{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            DepartureDate = new DateOnly(2030, 1, 1),
            StartTime = new TimeOnly(8, 0),
            RentalUnit = BoatRentalUnit.Hour,
            DurationValue = 3,
            AdultCount = 2,
            ChildCount = 0,
            PassengerCount = 2,
            SubtotalAmount = 1_500_000m,
            TotalAmount = 1_500_000m,
            DepositAmount = 1_500_000m,
            RemainingAmount = 0m,
            FromStationId = fromStation.Id,
            FromStation = fromStation,
            ToStationId = toStation.Id,
            ToStation = toStation,
            BoatId = boats[0].Id,
            Boat = boats[0]
        };

        booking.CharterBoats = boats
            .Select((boat, index) => new CharterBookingBoat
            {
                Booking = booking,
                BookingId = booking.Id,
                Boat = boat,
                BoatId = boat.Id,
                BoatOrder = index + 1,
                SeatSetupType = boat.SeatSetupType,
                UnitPrice = 500_000m,
                ChargeableDurationValue = 3,
                SubtotalAmount = 1_500_000m
            })
            .ToList();

        return booking;
    }

    private static void AddItineraryStop(Booking booking, Station station, int stopOrder) =>
        booking.ItineraryStops.Add(new BookingItineraryStop
        {
            Booking = booking,
            BookingId = booking.Id,
            Station = station,
            StationId = station.Id,
            StopOrder = stopOrder,
            StayDurationMinutes = 20
        });
}
