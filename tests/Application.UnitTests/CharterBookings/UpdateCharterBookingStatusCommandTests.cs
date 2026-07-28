using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CharterBookings;

public class UpdateCharterBookingStatusCommandTests
{
    [Test]
    public async Task CompletedStatusRequiresPaidBookingPaymentStatus()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var booking = CharterBooking(BookingStatus.Confirmed, paymentStatus: "DepositPaid");
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = new UpdateCharterBookingStatusCommandHandler(context, admin);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new UpdateCharterBookingStatusCommand(booking.Id, BookingStatus.Completed),
                CancellationToken.None));

        exception.Errors["bookingStatus"].Single()
            .ShouldContain("thanh toán đủ");
    }

    [Test]
    public async Task AdminCanMarkPaidCharterBookingAsCompleted()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var booking = CharterBooking(BookingStatus.Confirmed, paymentStatus: "Paid");
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = new UpdateCharterBookingStatusCommandHandler(context, admin);

        var result = await handler.Handle(
            new UpdateCharterBookingStatusCommand(booking.Id, BookingStatus.Completed),
            CancellationToken.None);

        result.BookingStatus.ShouldBe("Completed");
        booking.BookingStatus.ShouldBe(BookingStatus.Completed);
    }

    [Test]
    public async Task CompletingLinkedCharterTripInactivatesOwnedRoute()
    {
        await using var context = SeatFlowTestData.CreateContext();
        const string bookingCode = "CB-20300101-ABC";
        var route = OwnedRoute(bookingCode);
        var booking = CharterBooking(BookingStatus.Confirmed, paymentStatus: "Paid");
        booking.BookingCode = bookingCode;
        booking.CharterRouteId = route.Id;
        booking.CharterRoute = route;
        var trip = CharterTrip(route, booking);
        context.AddRange(route, booking, trip);
        await context.SaveChangesAsync();

        await new UpdateTripStatusCommandHandler(context)
            .Handle(new UpdateTripStatusCommand(trip.Id, TripStatus.Completed, null), CancellationToken.None);

        context.Set<Route>().Single(x => x.Id == route.Id).Status.ShouldBe("Inactive");
    }

    [Test]
    public async Task SystemManagedStatusesCannotBeSetManuallyByAdmin()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var booking = CharterBooking(BookingStatus.PendingQuote, paymentStatus: "Unpaid");
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = new UpdateCharterBookingStatusCommandHandler(context, admin);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new UpdateCharterBookingStatusCommand(booking.Id, BookingStatus.Quoted),
                CancellationToken.None));

        exception.Errors["bookingStatus"].Single()
            .ShouldContain("Quoted do hệ thống gán");
    }

    private static Booking CharterBooking(BookingStatus status, string paymentStatus) =>
        new()
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = $"CB{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            DepartureDate = new DateOnly(2030, 1, 1),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 1,
            PassengerCount = 1,
            BookingStatus = status,
            PaymentStatus = paymentStatus,
            TotalAmount = 1_000_000,
            RemainingAmount = paymentStatus == "Paid" ? 0 : 500_000,
            DepositAmount = paymentStatus == "Paid" ? 1_000_000 : 500_000
        };

    private static Route OwnedRoute(string bookingCode)
    {
        var stationA = Station("CTA", "Bến A");
        var stationB = Station("CTB", "Bến B");
        var route = new Route
        {
            RouteCode = CharterBookingRouteSupport.BuildCompactRouteCodeBase(bookingCode),
            RouteName = $"Charter {bookingCode}: Bến A - Bến B",
            RouteType = RouteTypes.Charter,
            Description = $"Route charter ghép từ booking {bookingCode}.",
            Status = "Active",
            IsBookable = false
        };
        route.RouteStops.Add(RouteStop(route, stationA, 1));
        route.RouteStops.Add(RouteStop(route, stationB, 2));
        return route;
    }

    private static Trip CharterTrip(Route route, Booking booking) =>
        new()
        {
            RouteId = route.Id,
            Route = route,
            TripCode = $"TRIP-{booking.BookingCode}",
            TripType = TripTypes.Charter,
            SourceBookingId = booking.Id,
            OperatingDate = booking.DepartureDate.GetValueOrDefault(),
            DepartureTime = new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero),
            ArrivalTime = new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.Zero),
            CapacitySnapshot = 10,
            TripStatus = TripStatus.Scheduled
        };

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
