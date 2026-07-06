using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.CharterBookings;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CharterBookings;

public class QuoteCharterBookingCommandTests
{
    [Test]
    public async Task QuoteRequiresSelectedBoatCountToMatchCustomerRequest()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var fullStandardBoat = ActiveBoat(SeatSetupType.FullStandard, 1_000_000m);
        var booking = CharterBooking(SeatSetupType.FullStandard, SeatSetupType.StandardAndVip);
        context.AddRange(fullStandardBoat, booking);
        await context.SaveChangesAsync();

        var handler = new QuoteCharterBookingCommandHandler(
            context,
            admin,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero)));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new QuoteCharterBookingCommand(
                    booking.Id,
                    Boats: [new QuoteCharterBookingBoatRequest(1, fullStandardBoat.Id)]),
                CancellationToken.None));

        exception.Errors["boats"].Single()
            .ShouldContain("phải bằng số tàu khách yêu cầu");
    }

    [Test]
    public async Task QuoteStoresMultipleSelectedBoatsAndSumsAutomaticPrice()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var fullStandardBoat = ActiveBoat(SeatSetupType.FullStandard, 1_000_000m, "Full Standard");
        var standardAndVipBoat = ActiveBoat(SeatSetupType.StandardAndVip, 2_000_000m, "Standard And Vip");
        var booking = CharterBooking(SeatSetupType.FullStandard, SeatSetupType.StandardAndVip);
        context.AddRange(fullStandardBoat, standardAndVipBoat, booking);
        await context.SaveChangesAsync();

        var handler = new QuoteCharterBookingCommandHandler(
            context,
            admin,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero)));

        var result = await handler.Handle(
            new QuoteCharterBookingCommand(
                booking.Id,
                Boats:
                [
                    new QuoteCharterBookingBoatRequest(1, fullStandardBoat.Id),
                    new QuoteCharterBookingBoatRequest(2, standardAndVipBoat.Id)
                ]),
            CancellationToken.None);

        result.BoatId.ShouldBe(fullStandardBoat.Id);
        result.SubtotalAmount.ShouldBe(3_000_000m);
        result.TotalAmount.ShouldBe(3_000_000m);
        result.Boats.Select(x => x.BoatId)
            .ShouldBe([fullStandardBoat.Id, standardAndVipBoat.Id]);
        result.Boats.Select(x => x.SubtotalAmount)
            .ShouldBe([1_000_000m, 2_000_000m]);

        var savedBooking = await context.Set<Booking>()
            .Include(x => x.CharterBoats)
            .SingleAsync(x => x.Id == booking.Id);

        savedBooking.BoatId.ShouldBe(fullStandardBoat.Id);
        savedBooking.BookingStatus.ShouldBe(BookingStatus.Quoted);
        savedBooking.CharterBoats.Count.ShouldBe(2);
        savedBooking.CharterBoats
            .OrderBy(x => x.BoatOrder)
            .Select(x => x.BoatId)
            .ShouldBe([fullStandardBoat.Id, standardAndVipBoat.Id]);
    }

    [Test]
    public async Task PreviewReturnsPerBoatPricesWithoutUpdatingBooking()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var fullStandardBoat = ActiveBoat(SeatSetupType.FullStandard, 1_000_000m, "Full Standard");
        var standardAndVipBoat = ActiveBoat(SeatSetupType.StandardAndVip, 2_000_000m, "Standard And Vip");
        var booking = CharterBooking(SeatSetupType.FullStandard, SeatSetupType.StandardAndVip);
        context.AddRange(fullStandardBoat, standardAndVipBoat, booking);
        await context.SaveChangesAsync();

        var handler = new PreviewCharterBookingQuoteCommandHandler(
            context,
            admin,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero)));

        var result = await handler.Handle(
            new PreviewCharterBookingQuoteCommand(
                booking.Id,
                Boats:
                [
                    new QuoteCharterBookingBoatRequest(1, fullStandardBoat.Id),
                    new QuoteCharterBookingBoatRequest(2, standardAndVipBoat.Id)
                ]),
            CancellationToken.None);

        result.SubtotalAmount.ShouldBe(3_000_000m);
        result.TotalAmount.ShouldBe(3_000_000m);
        result.PricingSource.ShouldBe("Automatic");
        result.Boats.Select(x => x.UnitPrice)
            .ShouldBe([1_000_000m, 2_000_000m]);
        result.Boats.Select(x => x.SubtotalAmount)
            .ShouldBe([1_000_000m, 2_000_000m]);

        var savedBooking = await context.Set<Booking>()
            .Include(x => x.CharterBoats)
            .SingleAsync(x => x.Id == booking.Id);

        savedBooking.BookingStatus.ShouldBe(BookingStatus.PendingQuote);
        savedBooking.TotalAmount.ShouldBe(0);
        savedBooking.BoatId.ShouldBeNull();
        savedBooking.CharterBoats.ShouldBeEmpty();
    }

    private static Boat ActiveBoat(
        SeatSetupType setupType,
        decimal dailyRentalPrice,
        string name = "Charter boat")
    {
        var boat = SeatFlowTestData.Boat(setupType, seatsConfigured: true, status: BoatStatus.Active);
        boat.Name = name;
        boat.SeatCount = 60;
        boat.DailyRentalPrice = dailyRentalPrice;
        boat.HourlyRentalPrice = dailyRentalPrice / 10m;
        return boat;
    }

    private static Booking CharterBooking(params SeatSetupType[] requestedBoatTypes) =>
        new()
        {
            BookingType = Booking.CharterBookingType,
            BookingCode = $"CB{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            ContactName = "Nguyen Van A",
            ContactPhone = "0900000000",
            DepartureDate = new DateOnly(2030, 1, 1),
            StartTime = new TimeOnly(8, 0),
            RentalUnit = BoatRentalUnit.Day,
            DurationValue = 1,
            AdultCount = 100,
            ChildCount = 1,
            PassengerCount = 101,
            RequestedBoatCount = requestedBoatTypes.Length,
            RequestedBoatTypes = string.Join(",", requestedBoatTypes.Select(x => x.ToString())),
            PreferredSeatSetupType = requestedBoatTypes.FirstOrDefault(),
            BookingStatus = BookingStatus.PendingQuote,
            PaymentStatus = "Unpaid"
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
