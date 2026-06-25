using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.CustomBookings;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CustomBookings;

public class UpdateCustomBookingStatusCommandTests
{
    [Test]
    public async Task CompletedStatusRequiresPaidBookingPaymentStatus()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var booking = CustomBooking(BookingStatus.Confirmed, paymentStatus: "DepositPaid");
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = new UpdateCustomBookingStatusCommandHandler(context, admin);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new UpdateCustomBookingStatusCommand(booking.Id, BookingStatus.Completed),
                CancellationToken.None));

        exception.Errors["bookingStatus"].Single()
            .ShouldContain("thanh toán đủ");
    }

    [Test]
    public async Task AdminCanMarkPaidCustomBookingAsCompleted()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var booking = CustomBooking(BookingStatus.Confirmed, paymentStatus: "Paid");
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = new UpdateCustomBookingStatusCommandHandler(context, admin);

        var result = await handler.Handle(
            new UpdateCustomBookingStatusCommand(booking.Id, BookingStatus.Completed),
            CancellationToken.None);

        result.BookingStatus.ShouldBe("Completed");
        booking.BookingStatus.ShouldBe(BookingStatus.Completed);
    }

    [Test]
    public async Task QuotedStatusRequiresVesselAndPrice()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var booking = CustomBooking(BookingStatus.PendingQuote, paymentStatus: "Unpaid");
        context.Add(booking);
        await context.SaveChangesAsync();

        var handler = new UpdateCustomBookingStatusCommandHandler(context, admin);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new UpdateCustomBookingStatusCommand(booking.Id, BookingStatus.Quoted),
                CancellationToken.None));

        exception.Errors["bookingStatus"].Single()
            .ShouldContain("chưa có tàu hoặc chưa có giá");
    }

    private static CustomBooking CustomBooking(BookingStatus status, string paymentStatus) =>
        new()
        {
            BookingCode = $"CB{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            DepartureDate = new DateOnly(2030, 1, 1),
            RentalUnit = VesselRentalUnit.Day,
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
