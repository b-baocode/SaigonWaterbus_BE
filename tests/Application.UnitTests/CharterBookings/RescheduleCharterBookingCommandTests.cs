using FluentValidation.TestHelper;
using NUnit.Framework;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.UnitTests.CharterBookings;

public class RescheduleCharterBookingCommandTests
{
    [TestCase(5, 20)]
    [TestCase(23, 0)]
    public void ValidatorRejectsStartTimeOutsideOperatingHours(int hour, int minute)
    {
        var result = new RescheduleCharterBookingCommandValidator().TestValidate(
            new RescheduleCharterBookingCommand(
                Guid.NewGuid(),
                new DateOnly(2030, 9, 4),
                new TimeOnly(hour, minute)));

        result.ShouldHaveValidationErrorFor(x => x.StartTime);
    }

    [Test]
    public async Task RescheduleRejectsWhenCharterWouldEndAfterTwentyThree()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var booking = Booking();
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = Handler(context, admin);

        await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new RescheduleCharterBookingCommand(
                booking.Id,
                new DateOnly(2030, 9, 4),
                new TimeOnly(22, 30)),
            CancellationToken.None));
    }

    [Test]
    public async Task RescheduleAllowsCharterEndingExactlyAtTwentyThree()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var booking = Booking();
        context.Add(booking);
        await context.SaveChangesAsync();

        var result = await Handler(context, admin).Handle(
            new RescheduleCharterBookingCommand(
                booking.Id,
                new DateOnly(2030, 9, 4),
                new TimeOnly(22, 0)),
            CancellationToken.None);

        result.DepartureDate.ShouldBe(new DateOnly(2030, 9, 4));
        result.StartTime.ShouldBe(new TimeOnly(22, 0));
    }

    [Test]
    public async Task RescheduleRejectsBoatConflictAtNewTime()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(
            SeatSetupType.FullStandard,
            seatsConfigured: true,
            status: BoatStatus.Active);
        var route = Route();
        var booking = Booking(boat);
        var conflictDeparture = VietnamTime(2030, 9, 4, 10, 30);
        var conflictingTrip = Trip(route, boat, "CONFLICT", conflictDeparture, conflictDeparture.AddHours(1));
        context.AddRange(route, boat, booking, conflictingTrip);
        await context.SaveChangesAsync();

        var handler = Handler(context, admin);

        await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new RescheduleCharterBookingCommand(
                booking.Id,
                new DateOnly(2030, 9, 4),
                new TimeOnly(10, 0)),
            CancellationToken.None));
    }

    [Test]
    public async Task RescheduleRejectsBoatHeldByAnotherCharterAtNewTime()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = SeatFlowTestData.Boat(
            SeatSetupType.FullStandard,
            seatsConfigured: true,
            status: BoatStatus.Active);
        var booking = Booking(boat);
        var conflictingBooking = Booking(boat);
        conflictingBooking.StartTime = new TimeOnly(10, 30);
        context.AddRange(boat, booking, conflictingBooking);
        await context.SaveChangesAsync();

        var handler = Handler(context, admin);

        await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new RescheduleCharterBookingCommand(
                booking.Id,
                new DateOnly(2030, 9, 4),
                new TimeOnly(10, 0)),
            CancellationToken.None));
    }

    [Test]
    public async Task RescheduleRejectsBookingWithCheckedInTicket()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var booking = Booking();
        booking.Tickets.Add(new Ticket
        {
            Booking = booking,
            BookingId = booking.Id,
            TicketCode = "TK-CHECKED-IN",
            QrToken = "QR-CHECKED-IN",
            TicketStatus = TicketStatus.CheckedIn,
            IssuedAt = VietnamTime(2030, 9, 1, 8, 0),
            CheckedInAt = VietnamTime(2030, 9, 4, 9, 55)
        });
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = Handler(context, admin);

        await Should.ThrowAsync<ValidationException>(() => handler.Handle(
            new RescheduleCharterBookingCommand(
                booking.Id,
                new DateOnly(2030, 9, 5),
                new TimeOnly(10, 0)),
            CancellationToken.None));
    }

    [Test]
    public async Task RescheduleTreatsInputAsVietnamTimeAndRepairsCorruptedTripStops()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var booking = new Booking
        {
            BookingType = SaigonWaterbus.Domain.Entities.Booking.CharterBookingType,
            BookingCode = "CB-RESCHEDULE-SYNC",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = "Paid",
            DepartureDate = new DateOnly(2030, 9, 4),
            StartTime = new TimeOnly(15, 0),
            RentalUnit = BoatRentalUnit.Hour,
            DurationValue = 1,
            PassengerCount = 1
        };
        var route = Route();
        var oldDeparture = new DateTimeOffset(2030, 9, 10, 15, 0, 0, TimeSpan.FromHours(7))
            .ToUniversalTime();
        var corruptedStopDeparture = new DateTimeOffset(2030, 8, 30, 3, 0, 0, TimeSpan.Zero);
        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            TripCode = "BR-20300910-CB-RESCHEDULE-SYNC-1",
            TripType = TripTypes.Charter,
            SourceBookingId = booking.Id,
            OperatingDate = new DateOnly(2030, 9, 10),
            DepartureTime = oldDeparture,
            ArrivalTime = oldDeparture.AddMinutes(85),
            AdjustedDepartureTime = oldDeparture.AddMinutes(10),
            AdjustedArrivalTime = oldDeparture.AddMinutes(95),
            CapacitySnapshot = 10,
            TripStatus = TripStatus.Scheduled
        };
        var orderedRouteStops = route.RouteStops.OrderBy(x => x.StopOrder).ToArray();
        trip.TripStops.Add(new TripStop
        {
            Trip = trip,
            TripId = trip.Id,
            Station = orderedRouteStops[0].Station,
            StationId = orderedRouteStops[0].StationId,
            StopOrder = 1,
            PlannedDepartureTime = corruptedStopDeparture,
            AdjustedDepartureTime = corruptedStopDeparture.AddMinutes(10)
        });
        trip.TripStops.Add(new TripStop
        {
            Trip = trip,
            TripId = trip.Id,
            Station = orderedRouteStops[1].Station,
            StationId = orderedRouteStops[1].StationId,
            StopOrder = 2,
            PlannedArrivalTime = corruptedStopDeparture.AddMinutes(80),
            AdjustedArrivalTime = corruptedStopDeparture.AddMinutes(90)
        });
        booking.Trip = trip;
        booking.TripId = trip.Id;

        context.AddRange(route, booking, trip);
        await context.SaveChangesAsync();

        var handler = new RescheduleCharterBookingCommandHandler(
            context,
            admin,
            new FixedTimeProvider(new DateTimeOffset(2030, 9, 3, 0, 0, 0, TimeSpan.Zero)));
        await handler.Handle(
            new RescheduleCharterBookingCommand(
                booking.Id,
                new DateOnly(2030, 9, 4),
                new TimeOnly(21, 0)),
            CancellationToken.None);

        var expectedDeparture = new DateTimeOffset(2030, 9, 4, 21, 0, 0, TimeSpan.FromHours(7))
            .ToUniversalTime();
        booking.DepartureDate.ShouldBe(new DateOnly(2030, 9, 4));
        booking.StartTime.ShouldBe(new TimeOnly(21, 0));
        trip.OperatingDate.ShouldBe(new DateOnly(2030, 9, 4));
        trip.DepartureTime.ShouldBe(expectedDeparture);
        trip.ArrivalTime.ShouldBe(expectedDeparture.AddMinutes(85));
        trip.AdjustedDepartureTime.ShouldBe(expectedDeparture.AddMinutes(10));
        trip.AdjustedArrivalTime.ShouldBe(expectedDeparture.AddMinutes(95));
        trip.TripCode.ShouldBe("BR-20300904-CB-RESCHEDULE-SYNC-1");

        var orderedTripStops = trip.TripStops.OrderBy(x => x.StopOrder).ToArray();
        orderedTripStops[0].PlannedDepartureTime.ShouldBe(expectedDeparture);
        orderedTripStops[0].AdjustedDepartureTime.ShouldBe(expectedDeparture.AddMinutes(10));
        orderedTripStops[1].PlannedArrivalTime.ShouldBe(expectedDeparture.AddMinutes(80));
        orderedTripStops[1].AdjustedArrivalTime.ShouldBe(expectedDeparture.AddMinutes(90));
    }

    private static RescheduleCharterBookingCommandHandler Handler(
        SaigonWaterbus.Infrastructure.Data.ApplicationDbContext context,
        TestUserContext admin) =>
        new(
            context,
            admin,
            new FixedTimeProvider(new DateTimeOffset(2030, 9, 3, 0, 0, 0, TimeSpan.Zero)));

    private static Booking Booking(Boat? boat = null) => new()
    {
        BookingType = SaigonWaterbus.Domain.Entities.Booking.CharterBookingType,
        BookingCode = $"CB-{Guid.NewGuid():N}"[..20],
        ContactName = "Nguyen Van A",
        ContactPhone = "0900000000",
        BookingStatus = BookingStatus.Confirmed,
        PaymentStatus = "Paid",
        DepartureDate = new DateOnly(2030, 9, 4),
        StartTime = new TimeOnly(15, 0),
        RentalUnit = BoatRentalUnit.Hour,
        DurationValue = 1,
        PassengerCount = 1,
        BoatId = boat?.Id,
        Boat = boat
    };

    private static Trip Trip(
        Route route,
        Boat boat,
        string code,
        DateTimeOffset departure,
        DateTimeOffset arrival) => new()
    {
        Route = route,
        RouteId = route.Id,
        Boat = boat,
        BoatId = boat.Id,
        TripCode = code,
        OperatingDate = DateOnly.FromDateTime(departure.ToOffset(TimeSpan.FromHours(7)).DateTime),
        DepartureTime = departure,
        ArrivalTime = arrival,
        CapacitySnapshot = 10,
        TripStatus = TripStatus.Scheduled
    };

    private static DateTimeOffset VietnamTime(
        int year,
        int month,
        int day,
        int hour,
        int minute) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(7)).ToUniversalTime();

    private static Route Route()
    {
        var from = Station("BD", "Ben Bach Dang");
        var to = Station("TT", "Ben Thu Thiem");
        var route = new Route
        {
            RouteCode = "CH-RESCHEDULE-SYNC",
            RouteName = "Bach Dang - Thu Thiem",
            RouteType = RouteTypes.Charter,
            Status = "Active",
            IsBookable = false
        };
        route.RouteStops.Add(RouteStop(route, from, 1));
        route.RouteStops.Add(RouteStop(route, to, 2));
        return route;
    }

    private static RouteStop RouteStop(Route route, Station station, int stopOrder) =>
        new()
        {
            Route = route,
            RouteId = route.Id,
            Station = station,
            StationId = station.Id,
            StopOrder = stopOrder,
            StandardTravelMin = stopOrder == 1 ? null : 60
        };

    private static Station Station(string code, string name) =>
        new()
        {
            StationCode = code,
            StationName = name,
            Status = StationStatus.Active
        };
}
