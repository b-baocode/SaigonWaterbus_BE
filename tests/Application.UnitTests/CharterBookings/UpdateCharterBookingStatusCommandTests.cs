using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
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
    public async Task QuotedStatusRequiresBoatAndPrice()
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
            .ShouldContain("chưa có tàu hoặc chưa có giá");
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
}
