using NUnit.Framework;
using SaigonWaterbus.Application.BookingHistory;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.BookingHistory;

public class GetMyBookingHistoryQueryTests
{
    [Test]
    public async Task ReturnsEmptyWhenUserHasNoBookings()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();

        var result = await new GetMyBookingHistoryQueryHandler(context, new TestUserContext(userId))
            .Handle(new GetMyBookingHistoryQuery(), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Test]
    public async Task ReturnsCharterBookingsForCurrentUser()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var fromStation = Station("BACH_DANG", "Bach Dang");
        var toStation = Station("BINH_AN", "Binh An");
        var booking = CharterBooking(userId, fromStation, toStation);
        var otherBooking = CharterBooking(otherUserId, fromStation, toStation);
        context.AddRange(fromStation, toStation, booking, otherBooking);
        await context.SaveChangesAsync();

        var result = await new GetMyBookingHistoryQueryHandler(context, new TestUserContext(userId))
            .Handle(new GetMyBookingHistoryQuery(), CancellationToken.None);

        var item = result.ShouldHaveSingleItem();
        item.Id.ShouldBe(booking.Id);
        item.Type.ShouldBe("CharterBooking");
        item.Code.ShouldBe(booking.BookingCode);
        item.DepartureDate.ShouldBe(booking.DepartureDate);
        item.DepartureTime.ShouldBe(booking.StartTime);
        item.FromStationName.ShouldBe(fromStation.StationName);
        item.ToStationName.ShouldBe(toStation.StationName);
        item.PassengerCount.ShouldBe(20);
        item.Status.ShouldBe(BookingStatus.Quoted.ToString());
        item.TotalAmount.ShouldBe(2_000_000);
        item.Currency.ShouldBe("VND");
        item.DetailEndpoint.ShouldBe($"/api/charter-bookings/{booking.Id}");
    }

    private static Booking CharterBooking(Guid userId, Station fromStation, Station toStation) =>
        new()
        {
            BookingType = Booking.CharterBookingType,
            UserId = userId,
            BookingCode = $"CB{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            BookingStatus = BookingStatus.Quoted,
            PaymentStatus = "Unpaid",
            FromStationId = fromStation.Id,
            FromStation = fromStation,
            ToStationId = toStation.Id,
            ToStation = toStation,
            DepartureDate = new DateOnly(2030, 1, 1),
            StartTime = new TimeOnly(8, 30),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            PassengerCount = 20,
            AdultCount = 15,
            ChildCount = 5,
            SubtotalAmount = 2_000_000,
            TotalAmount = 2_000_000,
            RemainingAmount = 2_000_000
        };

    private static Station Station(string code, string name) =>
        new()
        {
            StationCode = code,
            StationName = name,
            Status = StationStatus.Active
        };
}
