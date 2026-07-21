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
        var booking = CharterBooking(1, 2);
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
        var fullStandardBoat = ActiveBoat(SeatSetupType.FullStandard, 1_000_000m, "Full Standard", numberOfDecks: 1);
        var standardAndVipBoat = ActiveBoat(SeatSetupType.StandardAndVip, 2_000_000m, "Standard And Vip", numberOfDecks: 2);
        var booking = CharterBooking(1, 2);
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
    public async Task QuoteRejectsSelectedBoatWhenConfirmedCharterBookingOverlaps()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = ActiveBoat(SeatSetupType.FullStandard, 1_000_000m);
        var booking = CharterBooking(1);
        booking.AdultCount = 50;
        booking.ChildCount = 0;
        booking.PassengerCount = 50;
        booking.RentalUnit = BoatRentalUnit.Hour;
        booking.DurationValue = 1;
        booking.StartTime = new TimeOnly(9, 0);
        var conflict = CharterBooking(1);
        conflict.AdultCount = 50;
        conflict.ChildCount = 0;
        conflict.PassengerCount = 50;
        conflict.BookingStatus = BookingStatus.Confirmed;
        conflict.BoatId = boat.Id;
        conflict.RentalUnit = BoatRentalUnit.Hour;
        conflict.DurationValue = 2;
        conflict.StartTime = new TimeOnly(8, 0);
        context.AddRange(boat, booking, conflict);
        await context.SaveChangesAsync();

        var handler = new QuoteCharterBookingCommandHandler(
            context,
            admin,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero)));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new QuoteCharterBookingCommand(
                    booking.Id,
                    Boats: [new QuoteCharterBookingBoatRequest(1, boat.Id)]),
                CancellationToken.None));

        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(x => x.Contains(conflict.BookingCode) && x.Contains("đổi giờ"));
    }

    [Test]
    public async Task QuoteRejectsSelectedBoatWhenExistingTripOverlaps()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var boat = ActiveBoat(SeatSetupType.FullStandard, 1_000_000m);
        var route = RegularRoute();
        var booking = CharterBooking(1);
        booking.AdultCount = 50;
        booking.ChildCount = 0;
        booking.PassengerCount = 50;
        booking.RentalUnit = BoatRentalUnit.Hour;
        booking.DurationValue = 1;
        booking.StartTime = new TimeOnly(9, 0);
        var existingTrip = new Trip
        {
            RouteId = route.Id,
            Route = route,
            BoatId = boat.Id,
            Boat = boat,
            TripCode = "TR-EXISTING",
            OperatingDate = booking.DepartureDate!.Value,
            DepartureTime = new DateTimeOffset(2030, 1, 1, 8, 30, 0, TimeSpan.FromHours(7)),
            ArrivalTime = new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.FromHours(7)),
            CapacitySnapshot = boat.SeatCount,
            TripStatus = TripStatus.Scheduled
        };
        context.AddRange(boat, route, booking, existingTrip);
        await context.SaveChangesAsync();

        var handler = new QuoteCharterBookingCommandHandler(
            context,
            admin,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero)));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new QuoteCharterBookingCommand(
                    booking.Id,
                    Boats: [new QuoteCharterBookingBoatRequest(1, boat.Id)]),
                CancellationToken.None));

        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(x => x.Contains("TR-EXISTING"));
    }

    [Test]
    public async Task QuoteAddsRequiredCharterInsuranceFromActivePackage()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var fullStandardBoat = ActiveBoat(SeatSetupType.FullStandard, 1_000_000m, "Full Standard", numberOfDecks: 1);
        var standardAndVipBoat = ActiveBoat(SeatSetupType.StandardAndVip, 2_000_000m, "Standard And Vip", numberOfDecks: 2);
        var insurancePackage = CharterInsurancePackage(unitPremiumAmount: 10_000m);
        var booking = CharterBooking(1, 2);
        context.AddRange(fullStandardBoat, standardAndVipBoat, insurancePackage, booking);
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

        result.Insurance.ShouldNotBeNull();
        result.Insurance.InsurancePackageId.ShouldBe(insurancePackage.Id);
        result.Insurance.Quantity.ShouldBe(101);
        result.Insurance.TotalAmount.ShouldBe(1_010_000m);
        result.SubtotalAmount.ShouldBe(4_010_000m);
        result.TotalAmount.ShouldBe(4_010_000m);

        var savedBooking = await context.Set<Booking>()
            .SingleAsync(x => x.Id == booking.Id);

        savedBooking.InsuranceSnapshot.ShouldNotBeNull();
        savedBooking.InsuranceSnapshot.Quantity.ShouldBe(101);
        savedBooking.InsuranceSnapshot.TotalAmount.ShouldBe(1_010_000m);
        savedBooking.TotalAmount.ShouldBe(4_010_000m);
    }

    [Test]
    public async Task QuotePersistsCharterInsuranceForFullSelectedBoatSeats()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var fullStandardBoat = ActiveBoat(SeatSetupType.FullStandard, 1_000_000m, "Full Standard", numberOfDecks: 1);
        var standardAndVipBoat = ActiveBoat(SeatSetupType.StandardAndVip, 2_000_000m, "Standard And Vip", numberOfDecks: 2);
        var insurancePackage = CharterInsurancePackage(unitPremiumAmount: 10_000m);
        var booking = CharterBooking(1, 2);
        context.AddRange(fullStandardBoat, standardAndVipBoat, insurancePackage, booking);
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

        result.Insurance.ShouldNotBeNull();
        result.Insurance.InsurancePackageId.ShouldBe(insurancePackage.Id);
        result.Insurance.Quantity.ShouldBe(101);
        result.Insurance.TotalAmount.ShouldBe(1_010_000m);
        result.SubtotalAmount.ShouldBe(4_010_000m);
        result.TotalAmount.ShouldBe(4_010_000m);

        var savedBooking = await context.Set<Booking>()
            .SingleAsync(x => x.Id == booking.Id);

        savedBooking.InsuranceSnapshot.ShouldNotBeNull();
        savedBooking.InsuranceSnapshot.Quantity.ShouldBe(101);
        savedBooking.InsuranceSnapshot.TotalAmount.ShouldBe(1_010_000m);
        savedBooking.TotalAmount.ShouldBe(4_010_000m);
    }

    [Test]
    public async Task PreviewReturnsPerBoatPricesWithoutUpdatingBooking()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var fullStandardBoat = ActiveBoat(SeatSetupType.FullStandard, 1_000_000m, "Full Standard", numberOfDecks: 1);
        var standardAndVipBoat = ActiveBoat(SeatSetupType.StandardAndVip, 2_000_000m, "Standard And Vip", numberOfDecks: 2);
        var booking = CharterBooking(1, 2);
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

    [Test]
    public async Task PreviewIncludesPreselectedCharterInsurance()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var fullStandardBoat = ActiveBoat(SeatSetupType.FullStandard, 1_000_000m, "Full Standard", numberOfDecks: 1);
        var standardAndVipBoat = ActiveBoat(SeatSetupType.StandardAndVip, 2_000_000m, "Standard And Vip", numberOfDecks: 2);
        var insurancePackage = CharterInsurancePackage(unitPremiumAmount: 10_000m);
        var booking = CharterBooking(1, 2);
        booking.InsuranceSnapshot = InsuranceSnapshot(insurancePackage, quantity: 101);
        context.AddRange(fullStandardBoat, standardAndVipBoat, insurancePackage, booking);
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

        result.Insurance.ShouldNotBeNull();
        result.Insurance.Selected.ShouldBeTrue();
        result.Insurance.InsurancePackageId.ShouldBe(insurancePackage.Id);
        result.Insurance.Quantity.ShouldBe(101);
        result.Insurance.TotalAmount.ShouldBe(1_010_000m);
        result.SubtotalAmount.ShouldBe(4_010_000m);
        result.TotalAmount.ShouldBe(4_010_000m);
    }

    [Test]
    public async Task QuoteRejectsBoatWithDifferentRequestedDeckCount()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var admin = await SeatFlowTestData.SeedAdminAsync(context);
        var oneDeckBoat = ActiveBoat(SeatSetupType.FullStandard, 1_000_000m, numberOfDecks: 1);
        var booking = CharterBooking(2);
        context.AddRange(oneDeckBoat, booking);
        await context.SaveChangesAsync();

        var handler = new QuoteCharterBookingCommandHandler(
            context,
            admin,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero)));

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            handler.Handle(
                new QuoteCharterBookingCommand(
                    booking.Id,
                    Boats: [new QuoteCharterBookingBoatRequest(1, oneDeckBoat.Id)]),
                CancellationToken.None));

        exception.Errors["boats"].Single()
            .ShouldContain("không trùng số tầng khách yêu cầu");
    }

    private static Boat ActiveBoat(
        SeatSetupType setupType,
        decimal dailyRentalPrice,
        string name = "Charter boat",
        int numberOfDecks = 1)
    {
        var boat = SeatFlowTestData.Boat(setupType, seatsConfigured: true, status: BoatStatus.Active);
        boat.Name = name;
        boat.SeatCount = 60;
        boat.NumberOfDecks = numberOfDecks;
        boat.DailyRentalPrice = dailyRentalPrice;
        boat.HourlyRentalPrice = dailyRentalPrice / 10m;
        return boat;
    }

    private static Booking CharterBooking(params int[] requestedBoatDecks) =>
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
            RequestedBoatCount = requestedBoatDecks.Length,
            RequestedBoatDecks = string.Join(",", requestedBoatDecks),
            BookingStatus = BookingStatus.PendingQuote,
            PaymentStatus = "Unpaid"
        };

    private static Route RegularRoute() =>
        new()
        {
            RouteCode = $"R{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            RouteName = "Regular route",
            RouteType = "Regular",
            Status = "Active"
        };

    private static InsurancePackage CharterInsurancePackage(decimal unitPremiumAmount) =>
        new()
        {
            Code = "CHARTER_PASSENGER_BASIC",
            Name = "Bao hiem hanh khach thue tau",
            BookingType = Booking.CharterBookingType,
            IsRequired = false,
            ProviderName = "Bao hiem mac dinh",
            UnitPremiumAmount = unitPremiumAmount,
            CoverageAmount = 50_000_000m,
            Currency = "VND",
            Conditions = ["Chi ap dung cho hanh khach co ten trong danh sach chuyen di."],
            IsActive = true,
            DisplayOrder = 1
        };

    private static BookingInsuranceSnapshot InsuranceSnapshot(
        InsurancePackage package,
        int quantity) =>
        new()
        {
            InsurancePackageId = package.Id,
            Code = package.Code,
            Name = package.Name,
            BookingType = package.BookingType,
            IsRequired = package.IsRequired,
            ProviderName = package.ProviderName,
            ProviderLogoUrl = package.ProviderLogoUrl,
            UnitPremiumAmount = package.UnitPremiumAmount,
            CoverageAmount = package.CoverageAmount,
            Currency = package.Currency,
            Conditions = package.Conditions,
            TermsUrl = package.TermsUrl,
            Quantity = quantity,
            TotalAmount = package.UnitPremiumAmount * quantity,
            QuotedAt = new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero)
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
