using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Vessels;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Vessels;

public class VesselSupportTests
{
    [Test]
    public void CreateVesselRequestValidatorAcceptsVesselWithoutService()
    {
        var validator = new CreateVesselRequestValidator();

        var result = validator.Validate(new CreateVesselRequest(
            "WB01",
            "Waterbus 01",
            VesselStatus.Inactive,
            80,
            1));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void EnsureCanActivateRejectsVesselWithoutSeatSetup()
    {
        var exception = Should.Throw<ValidationException>(() =>
            VesselSupport.EnsureCanActivate(Vessel(seatsConfigured: false), "Status"));

        exception.Errors.Keys.ShouldContain("status");
    }

    [Test]
    public void EnsureCanActivateAcceptsConfiguredVesselWithoutService()
    {
        Should.NotThrow(() =>
            VesselSupport.EnsureCanActivate(Vessel(seatsConfigured: true), "Status"));
    }

    [Test]
    public void IsReadyForOperationRequiresActiveStatusAndSeatSetup()
    {
        VesselSupport.IsReadyForOperation(Vessel(seatsConfigured: false)).ShouldBeFalse();
        VesselSupport.IsReadyForOperation(Vessel(seatsConfigured: true)).ShouldBeTrue();
        VesselSupport.IsReadyForOperation(Vessel(
            seatsConfigured: true,
            status: VesselStatus.Maintenance)).ShouldBeFalse();
    }

    [Test]
    public void CreateDtoIncludesRentalPricesWhenConfigured()
    {
        var vessel = Vessel(seatsConfigured: true);
        vessel.RentalPrices.Add(new VesselRentalPrice
        {
            VesselId = vessel.Id,
            RentalUnit = VesselRentalUnit.Day,
            UnitPrice = 15000000m,
            Currency = "VND",
            Note = "Gia thue theo ngay"
        });
        vessel.RentalPrices.Add(new VesselRentalPrice
        {
            VesselId = vessel.Id,
            RentalUnit = VesselRentalUnit.Hour,
            UnitPrice = 2000000m,
            Currency = "VND",
            Note = "Gia thue theo gio"
        });

        var dto = VesselSupport.CreateDto(vessel);

        dto.RentalPrice.ShouldNotBeNull();
        dto.RentalPrice.RentalUnit.ShouldBe(VesselRentalUnit.Day);
        dto.RentalPrice.UnitPrice.ShouldBe(15000000m);
        dto.RentalPrice.Currency.ShouldBe("VND");
        dto.RentalPrices.Count.ShouldBe(2);
        dto.RentalPrices.First().RentalUnit.ShouldBe(VesselRentalUnit.Hour);
        dto.RentalPrices.Last().RentalUnit.ShouldBe(VesselRentalUnit.Day);
    }

    [TestCase(SeatSetupType.FullStandard)]
    [TestCase(SeatSetupType.StandardAndVip)]
    public void CreateDtoReturnsSeatSetupTypeFromVessel(SeatSetupType expected)
    {
        var vessel = Vessel(seatsConfigured: false);
        vessel.SeatSetupType = expected;

        VesselSupport.CreateDto(vessel).SeatSetupType.ShouldBe(expected);
    }

    [TestCase(null, "VND")]
    [TestCase("", "VND")]
    [TestCase(" usd ", "USD")]
    public void NormalizeCurrencyDefaultsAndUppercasesCurrency(string? currency, string expected)
    {
        VesselSupport.NormalizeCurrency(currency).ShouldBe(expected);
    }

    [Test]
    public void CreateVesselRequestValidatorRejectsDuplicateRentalUnits()
    {
        var validator = new CreateVesselRequestValidator();
        var result = validator.Validate(new CreateVesselRequest(
            "WB01",
            "Waterbus 01",
            VesselStatus.Inactive,
            80,
            1,
            RentalPrices:
            [
                new VesselRentalPriceRequest(VesselRentalUnit.Hour, 2000000m),
                new VesselRentalPriceRequest(VesselRentalUnit.Hour, 2500000m)
            ]));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Mỗi đơn vị thuê tàu chỉ được cấu hình một giá.");
    }

    [Test]
    public void UpdateVesselRequestValidatorRejectsDuplicateRentalUnits()
    {
        var validator = new UpdateVesselRequestValidator();
        var result = validator.Validate(new UpdateVesselRequest(
            Guid.NewGuid(),
            RentalPrices:
            [
                new VesselRentalPriceRequest(VesselRentalUnit.Day, 15000000m),
                new VesselRentalPriceRequest(VesselRentalUnit.Day, 16000000m)
            ]));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Mỗi đơn vị thuê tàu chỉ được cấu hình một giá.");
    }

    private static Vessel Vessel(
        bool seatsConfigured,
        VesselStatus status = VesselStatus.Active) =>
        new()
        {
            Code = "WB01",
            Name = "Waterbus 01",
            Status = status,
            SeatsConfigured = seatsConfigured
        };

}
