using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Boats;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Boats;

public class BoatSupportTests
{
    [Test]
    public void CreateBoatRequestValidatorAcceptsBoatWithoutService()
    {
        var validator = new CreateBoatRequestValidator();

        var result = validator.Validate(new CreateBoatRequest(
            "WB01",
            "Waterbus 01",
            BoatStatus.Inactive,
            80,
            1));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void EnsureCanActivateRejectsBoatWithoutSeatSetup()
    {
        var exception = Should.Throw<ValidationException>(() =>
            BoatSupport.EnsureCanActivate(Boat(seatsConfigured: false), "Status"));

        exception.Errors.Keys.ShouldContain("status");
    }

    [Test]
    public void EnsureCanActivateAcceptsConfiguredBoatWithoutService()
    {
        Should.NotThrow(() =>
            BoatSupport.EnsureCanActivate(Boat(seatsConfigured: true), "Status"));
    }

    [Test]
    public void IsReadyForOperationRequiresActiveStatusAndSeatSetup()
    {
        BoatSupport.IsReadyForOperation(Boat(seatsConfigured: false)).ShouldBeFalse();
        BoatSupport.IsReadyForOperation(Boat(seatsConfigured: true)).ShouldBeTrue();
        BoatSupport.IsReadyForOperation(Boat(
            seatsConfigured: true,
            status: BoatStatus.UnderMaintenance)).ShouldBeFalse();
    }

    [Test]
    public void CreateDtoIncludesOnlyRentalPricesWhenConfigured()
    {
        var boat = Boat(seatsConfigured: true);
        boat.DailyRentalPrice = 15000000m;
        boat.HourlyRentalPrice = 2000000m;
        boat.Currency = "VND";

        var dto = BoatSupport.CreateDto(boat);

        dto.RentalPrices.Count.ShouldBe(2);
        dto.RentalPrices.First().RentalUnit.ShouldBe(BoatRentalUnit.Hour);
        dto.RentalPrices.First().UnitPrice.ShouldBe(2000000m);
        dto.RentalPrices.Last().RentalUnit.ShouldBe(BoatRentalUnit.Day);
        dto.RentalPrices.Last().UnitPrice.ShouldBe(15000000m);
    }

    [TestCase(SeatSetupType.FullStandard)]
    [TestCase(SeatSetupType.StandardAndVip)]
    public void CreateDtoReturnsSeatSetupTypeFromBoat(SeatSetupType expected)
    {
        var boat = Boat(seatsConfigured: false);
        boat.SeatSetupType = expected;

        BoatSupport.CreateDto(boat).SeatSetupType.ShouldBe(expected);
    }

    [TestCase(null, "VND")]
    [TestCase("", "VND")]
    [TestCase(" usd ", "USD")]
    public void NormalizeCurrencyDefaultsAndUppercasesCurrency(string? currency, string expected)
    {
        BoatSupport.NormalizeCurrency(currency).ShouldBe(expected);
    }

    [Test]
    public void CreateBoatRequestValidatorRejectsDuplicateRentalUnits()
    {
        var validator = new CreateBoatRequestValidator();
        var result = validator.Validate(new CreateBoatRequest(
            "WB01",
            "Waterbus 01",
            BoatStatus.Inactive,
            80,
            1,
            RentalPrices:
            [
                new BoatRentalPriceRequest(BoatRentalUnit.Hour, 2000000m),
                new BoatRentalPriceRequest(BoatRentalUnit.Hour, 2500000m)
            ]));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Mỗi đơn vị thuê tàu chỉ được cấu hình một giá.");
    }

    [Test]
    public void UpdateBoatRequestValidatorRejectsDuplicateRentalUnits()
    {
        var validator = new UpdateBoatRequestValidator();
        var result = validator.Validate(new UpdateBoatRequest(
            Guid.NewGuid(),
            RentalPrices:
            [
                new BoatRentalPriceRequest(BoatRentalUnit.Day, 15000000m),
                new BoatRentalPriceRequest(BoatRentalUnit.Day, 16000000m)
            ]));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Mỗi đơn vị thuê tàu chỉ được cấu hình một giá.");
    }

    private static Boat Boat(
        bool seatsConfigured,
        BoatStatus status = BoatStatus.Active) =>
        new()
        {
            Code = "WB01",
            Name = "Waterbus 01",
            Status = status,
            SeatCount = 1,
            SeatsConfigured = seatsConfigured
        };

}
