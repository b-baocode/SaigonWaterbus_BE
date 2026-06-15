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
    public void CreateDtoIncludesDayRentalPriceWhenConfigured()
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

        var dto = VesselSupport.CreateDto(vessel);

        dto.RentalPrice.ShouldNotBeNull();
        dto.RentalPrice.RentalUnit.ShouldBe(VesselRentalUnit.Day);
        dto.RentalPrice.UnitPrice.ShouldBe(15000000m);
        dto.RentalPrice.Currency.ShouldBe("VND");
    }

    [TestCase(SeatSetupType.FullStandard)]
    [TestCase(SeatSetupType.StandardAndVip)]
    public void CreateDtoReturnsSeatSetupTypeFromVessel(SeatSetupType expected)
    {
        var vessel = Vessel(seatsConfigured: false);
        vessel.SeatSetupType = expected;

        VesselSupport.CreateDto(vessel).SeatSetupType.ShouldBe(expected);
    }

    [Test]
    public void EnsureServiceSupportsSeatSetupAcceptsStandardAndVipWhenBothPricesExist()
    {
        var service = ServiceWithSeatPrices("STANDARD", "VIP");

        Should.NotThrow(() => VesselSupport.EnsureServiceSupportsSeatSetup(
            service,
            SeatSetupType.StandardAndVip,
            "ServiceId"));
    }

    [Test]
    public void EnsureServiceSupportsSeatSetupRejectsMissingVipPrice()
    {
        var exception = Should.Throw<ValidationException>(() =>
            VesselSupport.EnsureServiceSupportsSeatSetup(
                ServiceWithSeatPrices("STANDARD"),
                SeatSetupType.StandardAndVip,
                "ServiceId"));

        exception.Errors.Keys.ShouldContain("serviceId");
    }

    [TestCase(null, "VND")]
    [TestCase("", "VND")]
    [TestCase(" usd ", "USD")]
    public void NormalizeCurrencyDefaultsAndUppercasesCurrency(string? currency, string expected)
    {
        VesselSupport.NormalizeCurrency(currency).ShouldBe(expected);
    }

    [Test]
    public void UpdateVesselRentalPriceValidatorRejectsInvalidPrice()
    {
        var validator = new UpdateVesselRentalPriceRequestValidator();

        var result = validator.Validate(new UpdateVesselRentalPriceRequest(Guid.NewGuid(), 0));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(UpdateVesselRentalPriceRequest.UnitPrice));
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

    private static WaterbusService ServiceWithSeatPrices(params string[] seatTypeCodes)
    {
        var service = new WaterbusService
        {
            Code = "WS",
            Name = "WaterSightseeing",
            IsActive = true
        };

        service.SeatTypePrices = seatTypeCodes
            .Select((code, index) =>
            {
                var seatType = new SeatType
                {
                    Code = code,
                    Name = code,
                    DisplayOrder = index + 1,
                    IsActive = true
                };

                return new ServiceSeatTypePrice
                {
                    WaterbusServiceId = service.Id,
                    WaterbusService = service,
                    SeatTypeId = seatType.Id,
                    SeatType = seatType,
                    PriceModifier = code == "VIP" ? 1.5m : 1m,
                    IsActive = true
                };
            })
            .ToList();

        return service;
    }
}
