using NUnit.Framework;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CharterBookings;

public class CharterBookingExpirationProcessorTests
{
    [Test]
    public async Task CleanupExpiresOverdueQuotedBookingAndReleasesBoatHold()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var now = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        var boat = SeatFlowTestData.Boat(SeatSetupType.FullStandard, seatsConfigured: true, status: BoatStatus.Active);
        var promotion = new Promotion
        {
            PromotionCode = "WELCOME10",
            PromotionName = "Welcome",
            PromotionType = PromotionType.Percent,
            DiscountValue = 10,
            ValidFrom = now.AddDays(-1),
            ValidTo = now.AddDays(1),
            Status = PromotionStatus.Active
        };
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = "CB-EXPIRE",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Quoted,
            PaymentStatus = "Unpaid",
            BoatId = boat.Id,
            DepartureDate = new DateOnly(2030, 1, 1),
            StartTime = new TimeOnly(8, 0),
            RentalUnit = BoatRentalUnit.Hour,
            DurationValue = 2,
            AdultCount = 1,
            PassengerCount = 1,
            SubtotalAmount = 10000,
            DiscountAmount = 1000,
            TotalAmount = 9000,
            RemainingAmount = 9000,
            HoldExpiresAt = now.AddSeconds(-1),
            Promotion = promotion
        };
        var selectedBoat = new CharterBookingBoat
        {
            Booking = booking,
            Boat = boat,
            BoatId = boat.Id,
            BoatOrder = 1,
            SeatSetupType = boat.SeatSetupType,
            UnitPrice = 10000,
            ChargeableDurationValue = 2,
            SubtotalAmount = 10000
        };
        var pendingPayment = new Payment
        {
            Booking = booking,
            PaymentCode = "1000006",
            Provider = "PayOS",
            Amount = 9000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Deposit",
            PaymentStatus = "Pending",
            CheckoutUrl = "https://example.test/checkout",
            ExpiresAt = now.AddSeconds(-1)
        };
        context.AddRange(boat, promotion, booking, selectedBoat, pendingPayment);
        await context.SaveChangesAsync();
        var boatHoldService = new TestBoatHoldService();
        var processor = new CharterBookingExpirationProcessor(context, boatHoldService);

        var result = await processor.CleanupExpiredAsync(now, CancellationToken.None);

        result.ExpiredPayments.ShouldBe(1);
        result.ExpiredCharterBookings.ShouldBe(1);
        booking.BookingStatus.ShouldBe(BookingStatus.Expired);
        booking.HoldExpiresAt.ShouldBeNull();
        booking.PaymentStatus.ShouldBe("Unpaid");
        pendingPayment.PaymentStatus.ShouldBe("Expired");
        // Lượt khuyến mãi suy ra từ bookings: booking chuyển Expired nên không còn tính là đã dùng.
        boatHoldService.Releases.ShouldHaveSingleItem().BoatId.ShouldBe(boat.Id);
    }

    [Test]
    public async Task CleanupDoesNotExpireQuotedBookingWithPaidDeposit()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var now = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = "CB-PAID-DEPOSIT",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Quoted,
            PaymentStatus = "DepositPaid",
            DepartureDate = new DateOnly(2030, 1, 1),
            RentalUnit = BoatRentalUnit.Hour,
            DurationValue = 2,
            AdultCount = 1,
            PassengerCount = 1,
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            DepositAmount = 5000,
            RemainingAmount = 5000,
            HoldExpiresAt = now.AddSeconds(-1)
        };
        var paidPayment = new Payment
        {
            Booking = booking,
            PaymentCode = "1000007",
            Provider = "PayOS",
            Amount = 5000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "Deposit",
            PaymentStatus = "Paid",
            PaidAt = now.AddMinutes(-1)
        };
        context.AddRange(booking, paidPayment);
        await context.SaveChangesAsync();
        var processor = new CharterBookingExpirationProcessor(context, new TestBoatHoldService());

        var result = await processor.CleanupExpiredAsync(now, CancellationToken.None);

        result.ExpiredCharterBookings.ShouldBe(0);
        booking.BookingStatus.ShouldBe(BookingStatus.Quoted);
        booking.HoldExpiresAt.ShouldBe(now.AddSeconds(-1));
    }

    [Test]
    public async Task CleanupExpiresOverduePendingPaymentCharterBooking()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var now = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = "CB-PENDING-PAYMENT",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.PendingPayment,
            PaymentStatus = "Unpaid",
            DepartureDate = new DateOnly(2030, 1, 1),
            RentalUnit = BoatRentalUnit.Hour,
            DurationValue = 2,
            AdultCount = 1,
            PassengerCount = 1,
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 10000,
            HoldExpiresAt = now.AddSeconds(-1)
        };
        context.Add(booking);
        await context.SaveChangesAsync();
        var processor = new CharterBookingExpirationProcessor(context, new TestBoatHoldService());

        var result = await processor.CleanupExpiredAsync(now, CancellationToken.None);

        result.ExpiredCharterBookings.ShouldBe(1);
        booking.BookingStatus.ShouldBe(BookingStatus.Expired);
        booking.HoldExpiresAt.ShouldBeNull();
    }

    [Test]
    public async Task CleanupRemovesOwnedRouteForPreviouslyCancelledCharterBookingWithoutTrips()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var now = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        var route = OwnedRoute("CB-CANCELLED-ROUTE");
        var booking = CharterBooking("CB-CANCELLED-ROUTE", BookingStatus.Cancelled, route);
        context.AddRange(route, booking);
        await context.SaveChangesAsync();
        var processor = new CharterBookingExpirationProcessor(context, new TestBoatHoldService());

        var result = await processor.CleanupExpiredAsync(now, CancellationToken.None);

        result.CleanedCharterRoutes.ShouldBe(1);
        context.Set<Route>().Any(x => x.Id == route.Id).ShouldBeFalse();
        booking.CharterRouteId.ShouldBeNull();
    }

    [Test]
    public async Task CleanupInactivatesOwnedRouteForPreviouslyExpiredCharterBookingWithTrips()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var now = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        var route = OwnedRoute("CB-EXPIRED-ROUTE");
        var booking = CharterBooking("CB-EXPIRED-ROUTE", BookingStatus.Expired, route);
        var trip = new Trip
        {
            RouteId = route.Id,
            Route = route,
            TripCode = "TRIP-CB-EXPIRED-ROUTE",
            TripType = TripTypes.Charter,
            SourceBookingId = booking.Id,
            OperatingDate = new DateOnly(2026, 7, 7),
            DepartureTime = now.AddHours(1),
            ArrivalTime = now.AddHours(2),
            CapacitySnapshot = 10,
            TripStatus = TripStatus.Cancelled
        };
        context.AddRange(route, booking, trip);
        await context.SaveChangesAsync();
        var processor = new CharterBookingExpirationProcessor(context, new TestBoatHoldService());

        var result = await processor.CleanupExpiredAsync(now, CancellationToken.None);

        result.CleanedCharterRoutes.ShouldBe(1);
        var savedRoute = context.Set<Route>().Single(x => x.Id == route.Id);
        savedRoute.Status.ShouldBe("Inactive");
        booking.CharterRouteId.ShouldBe(route.Id);
    }

    [Test]
    public async Task CleanupRevertsExpiredPassengerAddPaymentToConfirmed()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var now = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = "CB-ADD-EXPIRE",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.PendingPayment,
            PaymentStatus = "Paid",
            DepartureDate = new DateOnly(2030, 1, 1),
            StartTime = new TimeOnly(8, 0),
            RentalUnit = BoatRentalUnit.Hour,
            DurationValue = 2,
            AdultCount = 3,
            ChildCount = 0,
            PassengerCount = 3,
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 3000,
            HoldExpiresAt = now.AddSeconds(-1)
        };
        var pendingBhPayment = new Payment
        {
            Booking = booking,
            PaymentCode = "1000099",
            Provider = "PayOS",
            Amount = 3000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "PassengerAddInsurance",
            PaymentStatus = "Pending",
            CheckoutUrl = "https://example.test/checkout"
        };
        var approvedAddedPassenger = new BookingPassenger
        {
            Booking = booking,
            FullName = "New Passenger",
            BirthYear = 2000,
            PassengerType = "Adult",
            ApprovalStatus = "Approved",
            RequestBatchId = Guid.NewGuid()
        };
        context.AddRange(booking, pendingBhPayment, approvedAddedPassenger);
        await context.SaveChangesAsync();

        var processor = new CharterBookingExpirationProcessor(context, new TestBoatHoldService());

        var result = await processor.CleanupExpiredAsync(now, CancellationToken.None);

        result.ExpiredPassengerAddPayments.ShouldBe(1);
        booking.BookingStatus.ShouldBe(BookingStatus.Confirmed);
        booking.HoldExpiresAt.ShouldBeNull();
        pendingBhPayment.PaymentStatus.ShouldBe("Expired");
        approvedAddedPassenger.ApprovalStatus.ShouldBe("Rejected");
        approvedAddedPassenger.ReviewNote.ShouldNotBeNull();
    }

    [Test]
    public async Task CleanupExpiredPassengerAddCheckoutRestoresApprovedWhenReviewDeadlineStillOpen()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var now = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = "CB-ADD-LINK-EXPIRE",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.PendingPayment,
            PaymentStatus = "Paid",
            DepartureDate = new DateOnly(2030, 1, 1),
            StartTime = new TimeOnly(8, 0),
            RentalUnit = BoatRentalUnit.Hour,
            DurationValue = 2,
            AdultCount = 3,
            ChildCount = 0,
            PassengerCount = 3,
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 3000,
            HoldExpiresAt = now.AddHours(1)
        };
        var expiredBhPayment = new Payment
        {
            Booking = booking,
            PaymentCode = "1000100",
            Provider = "PayOS",
            Amount = 3000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "PassengerAddInsurance",
            PaymentStatus = "Pending",
            CheckoutUrl = "https://example.test/checkout",
            ExpiresAt = now.AddSeconds(-1)
        };
        context.AddRange(booking, expiredBhPayment);
        await context.SaveChangesAsync();

        var processor = new CharterBookingExpirationProcessor(context, new TestBoatHoldService());

        var result = await processor.CleanupExpiredAsync(now, CancellationToken.None);

        result.ExpiredPayments.ShouldBe(1);
        result.ExpiredPassengerAddPayments.ShouldBe(0);
        booking.BookingStatus.ShouldBe(BookingStatus.Approved);
        booking.HoldExpiresAt.ShouldBe(now.AddHours(1));
        expiredBhPayment.PaymentStatus.ShouldBe("Expired");
    }

    [Test]
    public async Task CleanupDoesNotRevertPassengerAddPaymentWhenBhAlreadyPaid()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var now = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = "CB-ADD-PAID",
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.PendingPayment,
            PaymentStatus = "Paid",
            DepartureDate = new DateOnly(2030, 1, 1),
            StartTime = new TimeOnly(8, 0),
            RentalUnit = BoatRentalUnit.Hour,
            DurationValue = 2,
            AdultCount = 3,
            ChildCount = 0,
            PassengerCount = 3,
            SubtotalAmount = 10000,
            TotalAmount = 10000,
            RemainingAmount = 0,
            HoldExpiresAt = now.AddSeconds(-1)
        };
        var paidBhPayment = new Payment
        {
            Booking = booking,
            PaymentCode = "1000101",
            Provider = "PayOS",
            Amount = 3000,
            Currency = "VND",
            PaymentMethod = "PayOS",
            PaymentPurpose = "PassengerAddInsurance",
            PaymentStatus = "Paid",
            PaidAt = now.AddMinutes(-1)
        };
        context.AddRange(booking, paidBhPayment);
        await context.SaveChangesAsync();

        var processor = new CharterBookingExpirationProcessor(context, new TestBoatHoldService());

        var result = await processor.CleanupExpiredAsync(now, CancellationToken.None);

        result.ExpiredPassengerAddPayments.ShouldBe(1);
        booking.BookingStatus.ShouldBe(BookingStatus.Confirmed);
        booking.HoldExpiresAt.ShouldBeNull();
    }

    private sealed class TestBoatHoldService : IBoatHoldService
    {
        public List<ReleaseCall> Releases { get; } = [];

        public Task<bool> TryHoldAsync(
            Guid bookingId,
            Guid boatId,
            DateOnly departureDate,
            TimeOnly? startTime,
            BoatRentalUnit rentalUnit,
            int durationValue,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task ReleaseAsync(
            Guid bookingId,
            Guid? boatId,
            DateOnly departureDate,
            TimeOnly? startTime,
            BoatRentalUnit rentalUnit,
            int durationValue,
            CancellationToken cancellationToken)
        {
            Releases.Add(new ReleaseCall(
                bookingId,
                boatId,
                departureDate,
                startTime,
                rentalUnit,
                durationValue));
            return Task.CompletedTask;
        }
    }

    private sealed record ReleaseCall(
        Guid BookingId,
        Guid? BoatId,
        DateOnly DepartureDate,
        TimeOnly? StartTime,
        BoatRentalUnit RentalUnit,
        int DurationValue);

    private static Route OwnedRoute(string bookingCode) =>
        new()
        {
            RouteCode = CharterBookingRouteSupport.BuildCompactRouteCodeBase(bookingCode),
            RouteName = $"Charter {bookingCode}: Bến A - Bến B",
            RouteType = RouteTypes.Charter,
            Description = $"Route charter ghép từ booking {bookingCode}.",
            Status = "Active",
            IsBookable = false
        };

    private static Booking CharterBooking(string bookingCode, BookingStatus status, Route route) =>
        new()
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = bookingCode,
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = status,
            PaymentStatus = "Unpaid",
            CharterRouteId = route.Id,
            CharterRoute = route
        };
}
