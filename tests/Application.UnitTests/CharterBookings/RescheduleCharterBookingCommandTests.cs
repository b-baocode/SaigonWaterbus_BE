using NUnit.Framework;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CharterBookings;

public class RescheduleCharterBookingCommandTests
{
    [Test]
    public async Task RescheduleRepairsTripWhenBookingDateWasAlreadyChangedDirectly()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
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
        var trip = new Trip
        {
            Route = route,
            RouteId = route.Id,
            TripCode = "BR-20300910-CB-RESCHEDULE-SYNC-1",
            TripType = TripTypes.Charter,
            SourceBookingId = booking.Id,
            OperatingDate = new DateOnly(2030, 9, 10),
            DepartureTime = oldDeparture,
            ArrivalTime = oldDeparture.AddHours(1),
            CapacitySnapshot = 10,
            TripStatus = TripStatus.Scheduled
        };
        trip.TripStops.Add(new TripStop
        {
            Trip = trip,
            TripId = trip.Id,
            Station = route.RouteStops.First().Station,
            StationId = route.RouteStops.First().StationId,
            StopOrder = 1,
            PlannedDepartureTime = oldDeparture
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
                new TimeOnly(15, 0)),
            CancellationToken.None);

        var expectedDeparture = new DateTimeOffset(2030, 9, 4, 15, 0, 0, TimeSpan.FromHours(7))
            .ToUniversalTime();
        trip.OperatingDate.ShouldBe(new DateOnly(2030, 9, 4));
        trip.DepartureTime.ShouldBe(expectedDeparture);
        trip.ArrivalTime.ShouldBe(expectedDeparture.AddHours(1));
        trip.TripStops.Single().PlannedDepartureTime.ShouldBe(expectedDeparture);
    }

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
