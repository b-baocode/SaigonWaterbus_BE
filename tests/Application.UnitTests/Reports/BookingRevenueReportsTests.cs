using NUnit.Framework;
using SaigonWaterbus.Application.Payments;
using SaigonWaterbus.Application.Reports;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Reports;

public class BookingRevenueReportsTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 4, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task RevenueReportAggregatesPaidPaymentsByMethodAndServiceType()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var customerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedReportBookingsAsync(context, staffContext.UserId!.Value, customerContext.UserId!.Value);
        var handler = new GetRevenueReportQueryHandler(context, adminContext);

        var result = await handler.Handle(
            new GetRevenueReportQuery(Now.AddDays(-1), Now.AddDays(1)),
            CancellationToken.None);

        result.GrossRevenue.ShouldBe(300m);
        result.RefundAmount.ShouldBe(50m);
        result.NetRevenue.ShouldBe(250m);
        result.PaidPaymentCount.ShouldBe(2);
        result.BookingCount.ShouldBe(2);
        result.TicketCount.ShouldBe(2);
        result.CounterBookingCount.ShouldBe(1);

        result.ByPaymentMethod.Single(x => x.Key == PaymentSupport.CashPaymentMethod).NetRevenue.ShouldBe(100m);
        result.ByPaymentMethod.Single(x => x.Key == PaymentSupport.PayOsProvider).NetRevenue.ShouldBe(150m);
        result.ByServiceType.Single(x => x.Key == "Waterbus").GrossRevenue.ShouldBe(100m);
        result.ByServiceType.Single(x => x.Key == "Sightseeing").NetRevenue.ShouldBe(150m);
    }

    [Test]
    public async Task BookingManagementListFiltersByPaymentMethodAndScopesStaffToOwnSales()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var customerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedReportBookingsAsync(context, staffContext.UserId!.Value, customerContext.UserId!.Value);
        var handler = new GetBookingManagementListQueryHandler(context, staffContext);

        var result = await handler.Handle(
            new GetBookingManagementListQuery(PaymentMethod: "cash"),
            CancellationToken.None);

        result.TotalCount.ShouldBe(1);
        result.Summary.CounterBookingCount.ShouldBe(1);
        result.Summary.PaidAmount.ShouldBe(100m);
        result.Items.Single().BookingCode.ShouldBe("BK-RPT-COUNTER");
        result.Items.Single().LatestPaymentMethod.ShouldBe(PaymentSupport.CashPaymentMethod);
    }

    [Test]
    public async Task BookingSelectOptionsReturnsCompactLabels()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var managerContext = await SeatFlowTestData.SeedManagerAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var customerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedReportBookingsAsync(context, staffContext.UserId!.Value, customerContext.UserId!.Value);
        var handler = new GetBookingSelectOptionsQueryHandler(context, managerContext);

        var result = await handler.Handle(
            new GetBookingSelectOptionsQuery("RPT", Limit: 10),
            CancellationToken.None);

        result.Count.ShouldBe(2);
        result.ShouldContain(x => x.BookingCode == "BK-RPT-COUNTER" && x.Label.Contains("Waterbus"));
        result.ShouldContain(x => x.BookingCode == "BK-RPT-ONLINE" && x.Label.Contains("Sightseeing"));
    }

    [Test]
    public async Task BookingManagementListAcceptsVietnamLocalDateBoundaries()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var customerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedReportBookingsAsync(context, staffContext.UserId!.Value, customerContext.UserId!.Value);

        var result = await new GetBookingManagementListQueryHandler(context, adminContext)
            .Handle(
                new GetBookingManagementListQuery(
                    CreatedFrom: new DateTimeOffset(2026, 7, 29, 0, 0, 0, TimeSpan.FromHours(7)),
                    CreatedTo: new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.FromHours(7)),
                    DepartureFrom: new DateTimeOffset(2026, 7, 29, 11, 0, 0, TimeSpan.FromHours(7)),
                    DepartureTo: new DateTimeOffset(2026, 7, 30, 11, 0, 0, TimeSpan.FromHours(7))),
                CancellationToken.None);

        result.TotalCount.ShouldBe(2);
        result.Items.Count.ShouldBe(2);
    }

    [Test]
    public async Task WaterbusStationRevenueUsesPassengerStationsInsteadOfCharterStations()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var adminContext = await SeatFlowTestData.SeedAdminAsync(context);
        var staffContext = await SeatFlowTestData.SeedStaffAsync(context);
        var customerContext = await SeatFlowTestData.SeedCustomerAsync(context);
        await SeedReportBookingsAsync(context, staffContext.UserId!.Value, customerContext.UserId!.Value);

        var result = await new GetWaterbusStationRevenueQueryHandler(context, adminContext)
            .Handle(new GetWaterbusStationRevenueQuery(Now.AddDays(-1), Now.AddDays(1)), CancellationToken.None);

        result.TotalGross.ShouldBe(100m);
        result.BookingCount.ShouldBe(1);
        result.PaymentCount.ShouldBe(1);
        result.TotalTicketCount.ShouldBe(1);
        result.Stations.Count.ShouldBe(2);
        result.Stations.Single(x => x.StationCode == "RPA").DepartureGross.ShouldBe(100m);
        result.Stations.Single(x => x.StationCode == "RPB").ArrivalGross.ShouldBe(100m);
    }

    private static async Task SeedReportBookingsAsync(
        ApplicationDbContext context,
        Guid staffId,
        Guid customerId)
    {
        var stationA = new Station { StationCode = "RPA", StationName = "Report A" };
        var stationB = new Station { StationCode = "RPB", StationName = "Report B" };
        var waterbusRoute = new Route
        {
            RouteCode = "RPT-WB",
            RouteName = "Report Waterbus",
            RouteType = RouteTypes.Regular
        };
        var sightseeingRoute = new Route
        {
            RouteCode = "RPT-ST",
            RouteName = "Report Sightseeing",
            RouteType = RouteTypes.SightseeingLoop
        };
        var waterbusTrip = new Trip
        {
            Route = waterbusRoute,
            RouteId = waterbusRoute.Id,
            TripCode = "TR-RPT-WB",
            OperatingDate = DateOnly.FromDateTime(Now.UtcDateTime),
            DepartureTime = Now.AddHours(2),
            ArrivalTime = Now.AddHours(3),
            CapacitySnapshot = 10,
            TripStatus = TripStatus.Scheduled
        };
        var sightseeingTrip = new Trip
        {
            Route = sightseeingRoute,
            RouteId = sightseeingRoute.Id,
            TripCode = "TR-RPT-ST",
            OperatingDate = DateOnly.FromDateTime(Now.UtcDateTime),
            DepartureTime = Now.AddHours(4),
            ArrivalTime = Now.AddHours(5),
            CapacitySnapshot = 10,
            TripStatus = TripStatus.Scheduled
        };

        var counterBooking = new Booking
        {
            SoldByStaffId = staffId,
            Trip = waterbusTrip,
            TripId = waterbusTrip.Id,
            BookingCode = "BK-RPT-COUNTER",
            ContactName = "Counter Buyer",
            ContactPhone = "0901000001",
            ContactEmail = "counter@example.test",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = PaymentSupport.PaidBookingPaymentStatus,
            SubtotalAmount = 100m,
            TotalAmount = 100m,
            RemainingAmount = 0m,
            Created = Now.AddMinutes(-30)
        };
        counterBooking.Passengers.Add(new BookingPassenger
        {
            Booking = counterBooking,
            Trip = waterbusTrip,
            TripId = waterbusTrip.Id,
            FullName = "Counter Passenger",
            PassengerType = "ADULT",
            UnitPrice = 100m,
            FromStation = stationA,
            FromStationId = stationA.Id,
            ToStation = stationB,
            ToStationId = stationB.Id
        });
        counterBooking.Tickets.Add(new Ticket
        {
            Booking = counterBooking,
            TicketCode = "TK-RPT-1",
            QrToken = "QR-RPT-1",
            TicketStatus = TicketStatus.Active,
            IssuedAt = Now
        });
        counterBooking.Payments.Add(new Payment
        {
            Booking = counterBooking,
            PaymentCode = "PAY-RPT-1",
            Provider = PaymentSupport.CounterProvider,
            PaymentMethod = PaymentSupport.CashPaymentMethod,
            PaymentPurpose = PaymentSupport.FullPurpose,
            PaymentStatus = PaymentSupport.PaidStatus,
            Amount = 100m,
            PaidAt = Now,
            Created = Now
        });

        var onlineBooking = new Booking
        {
            UserId = customerId,
            Trip = sightseeingTrip,
            TripId = sightseeingTrip.Id,
            BookingCode = "BK-RPT-ONLINE",
            ContactName = "Online Buyer",
            ContactPhone = "0901000002",
            ContactEmail = "online@example.test",
            BookingStatus = BookingStatus.Confirmed,
            PaymentStatus = PaymentSupport.PaidBookingPaymentStatus,
            SubtotalAmount = 200m,
            TotalAmount = 200m,
            RemainingAmount = 0m,
            Created = Now.AddMinutes(-20)
        };
        onlineBooking.Passengers.Add(new BookingPassenger
        {
            Booking = onlineBooking,
            Trip = sightseeingTrip,
            TripId = sightseeingTrip.Id,
            FullName = "Online Passenger",
            PassengerType = "ADULT",
            UnitPrice = 200m
        });
        onlineBooking.Tickets.Add(new Ticket
        {
            Booking = onlineBooking,
            TicketCode = "TK-RPT-2",
            QrToken = "QR-RPT-2",
            TicketStatus = TicketStatus.Active,
            IssuedAt = Now
        });
        onlineBooking.Payments.Add(new Payment
        {
            Booking = onlineBooking,
            PaymentCode = "PAY-RPT-2",
            Provider = PaymentSupport.PayOsProvider,
            PaymentMethod = PaymentSupport.PayOsProvider,
            PaymentPurpose = PaymentSupport.FullPurpose,
            PaymentStatus = PaymentSupport.PaidStatus,
            Amount = 200m,
            RefundAmount = 50m,
            PaidAt = Now.AddMinutes(5),
            Created = Now.AddMinutes(5)
        });

        context.AddRange(stationA, stationB, waterbusRoute, sightseeingRoute, waterbusTrip, sightseeingTrip, counterBooking, onlineBooking);
        await context.SaveChangesAsync();
    }
}
