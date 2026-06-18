using NUnit.Framework;
using SaigonWaterbus.Application.WaterbusServices;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.WaterbusServices;

public class WaterbusServiceRequestValidatorTests
{
    [Test]
    public void CreateRejectsInvalidCode()
    {
        var validator = new CreateWaterbusServiceRequestValidator();

        var result = validator.Validate(new CreateWaterbusServiceRequest(
            "TOURIST-WATERBUS",
            "WaterBus du lich"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(CreateWaterbusServiceRequest.Code));
    }

    [Test]
    public void CreateAcceptsShortDatabaseDrivenCode()
    {
        var validator = new CreateWaterbusServiceRequestValidator();

        var result = validator.Validate(new CreateWaterbusServiceRequest(
            "TOURIST",
            "WaterBus du lich",
            "Dich vu WaterBus du lich theo plan.",
            true,
            2));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void UpdateStatusRejectsInvalidServiceId()
    {
        var validator = new UpdateWaterbusServiceStatusRequestValidator();

        var result = validator.Validate(new UpdateWaterbusServiceStatusRequest(Guid.Empty, false));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(UpdateWaterbusServiceStatusRequest.ServiceId));
    }

    [Test]
    public void GetSeatTypesRejectsInvalidServiceId()
    {
        var validator = new GetWaterbusServiceSeatTypesRequestValidator();

        var result = validator.Validate(new GetWaterbusServiceSeatTypesRequest(Guid.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x =>
            x.PropertyName == nameof(GetWaterbusServiceSeatTypesRequest.ServiceId));
    }

    [Test]
    public void UpdateRejectsEmptyNameWhenNameIsProvided()
    {
        var validator = new UpdateWaterbusServiceRequestValidator();

        var result = validator.Validate(new UpdateWaterbusServiceRequest(
            Guid.NewGuid(),
            Name: " "));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(UpdateWaterbusServiceRequest.Name));
    }
}
