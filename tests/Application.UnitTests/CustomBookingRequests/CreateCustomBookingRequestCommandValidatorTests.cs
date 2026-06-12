using SaigonWaterbus.Application.CustomBookingRequests;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CustomBookingRequests;

public class CreateCustomBookingRequestCommandValidatorTests
{
    [Test]
    public void ValidatorAcceptsMinimalCustomBookingRequest()
    {
        var validator = new CreateCustomBookingRequestCommandValidator();

        var result = validator.Validate(ValidCommand());

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void ValidatorRejectsDuplicateStopOrder()
    {
        var validator = new CreateCustomBookingRequestCommandValidator();
        var command = ValidCommand() with
        {
            ItineraryStops =
            [
                new CreateCustomBookingItineraryStopRequest(1, Guid.NewGuid(), 30),
                new CreateCustomBookingItineraryStopRequest(1, Guid.NewGuid(), 60)
            ]
        };

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(CreateCustomBookingRequestCommand.ItineraryStops));
    }

    [Test]
    public void ValidatorRejectsNonSequentialStopOrder()
    {
        var validator = new CreateCustomBookingRequestCommandValidator();
        var command = ValidCommand() with
        {
            ItineraryStops =
            [
                new CreateCustomBookingItineraryStopRequest(2, Guid.NewGuid(), 30)
            ]
        };

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(CreateCustomBookingRequestCommand.ItineraryStops));
    }

    [Test]
    public void ValidatorRequiresAdultPassenger()
    {
        var validator = new CreateCustomBookingRequestCommandValidator();
        var command = ValidCommand() with { AdultCount = 0 };

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(CreateCustomBookingRequestCommand.AdultCount));
    }

    private static CreateCustomBookingRequestCommand ValidCommand() =>
        new(
            PreferredVesselId: Guid.NewGuid(),
            DepartureDate: new DateOnly(2026, 6, 20),
            PreferredStartTime: new TimeOnly(8, 0),
            FromStationId: Guid.NewGuid(),
            ToStationId: Guid.NewGuid(),
            AdultCount: 2,
            ChildCount: 1,
            ItineraryStops:
            [
                new CreateCustomBookingItineraryStopRequest(1, Guid.NewGuid(), 90)
            ]);
}
