using NUnit.Framework;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CharterBookings;

public class RespondCharterBookingQuoteCommandTests
{
    [Test]
    public async Task AcceptMovesQuotedBookingToPendingPaymentAndExtendsHold()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var now = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Charter customer",
            PhoneNumber = "0900000000",
            Email = "customer@example.test",
            Role = role,
            RoleId = role.Id,
            Status = UserStatus.Active
        };
        var booking = new Booking
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = "CB-ACCEPT",
            User = user,
            UserId = user.Id,
            ContactName = user.FullName,
            ContactPhone = user.PhoneNumber!,
            ContactEmail = user.Email,
            DepartureDate = new DateOnly(2030, 1, 1),
            StartTime = new TimeOnly(8, 0),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 10,
            ChildCount = 2,
            PassengerCount = 12,
            BookingStatus = BookingStatus.Quoted,
            PaymentStatus = "Unpaid",
            SubtotalAmount = 3_000_000m,
            TotalAmount = 3_000_000m,
            RemainingAmount = 3_000_000m,
            HoldExpiresAt = now.AddHours(1)
        };
        context.AddRange(role, user, booking);
        await context.SaveChangesAsync();

        var handler = new RespondCharterBookingQuoteCommandHandler(
            context,
            new TestUserContext(user.Id),
            new FixedTimeProvider(now));

        var detail = await handler.Handle(
            new RespondCharterBookingQuoteCommand(
                booking.Id,
                CharterBookingQuoteResponseAction.Accept),
            CancellationToken.None);

        detail.BookingStatus.ShouldBe(BookingStatus.PendingPayment.ToString());
        detail.PaymentStatus.ShouldBe("Unpaid");
        detail.HoldExpiresAt.ShouldBe(now + BookingExpirationPolicy.CharterPaymentCompletionTtl);

        var savedBooking = context.Set<Booking>().Single(x => x.Id == booking.Id);
        savedBooking.BookingStatus.ShouldBe(BookingStatus.PendingPayment);
        savedBooking.HoldExpiresAt.ShouldBe(now + BookingExpirationPolicy.CharterPaymentCompletionTtl);
    }
}
