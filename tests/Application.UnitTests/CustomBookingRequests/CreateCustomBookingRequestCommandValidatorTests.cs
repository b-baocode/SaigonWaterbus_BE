using NUnit.Framework;
using SaigonWaterbus.Application.CustomBookingRequests;
using SaigonWaterbus.Domain.Enums;
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

    [Test]
    public void ValidatorAllowsMissingContactEmailWhenUsingAccountContact()
    {
        var validator = new CreateCustomBookingRequestCommandValidator();
        var command = ValidCommand() with { ContactEmail = null };

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void ValidatorRequiresContactEmailWhenNotUsingAccountContact()
    {
        var validator = new CreateCustomBookingRequestCommandValidator();
        var command = ValidCommand() with
        {
            UseAccountContact = false,
            ContactName = "Customer",
            ContactPhone = "0900000000",
            ContactEmail = null
        };

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x =>
            x.PropertyName == nameof(CreateCustomBookingRequestCommand.ContactEmail)
            && x.ErrorMessage == "Email liên hệ là bắt buộc.");
    }

    [Test]
    public void ValidatorRejectsUnsupportedContactEmail()
    {
        var validator = new CreateCustomBookingRequestCommandValidator();
        var command = ValidCommand() with { ContactEmail = "customer@example.com" };

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x =>
            x.PropertyName == nameof(CreateCustomBookingRequestCommand.ContactEmail));
    }

    [Test]
    public void ResolveContactEmailUsesProfileEmailWhenAvailable()
    {
        var command = ValidCommand() with { ContactEmail = null };
        var user = new SaigonWaterbus.Domain.Entities.User { Email = "profile@gmail.com" };

        var email = CreateCustomBookingRequestCommandHandler.ResolveContactEmail(command, user);

        email.ShouldBe("profile@gmail.com");
    }

    [Test]
    public void ResolveContactEmailUsesBookingEmailWhenProfileHasNoEmail()
    {
        var command = ValidCommand() with { ContactEmail = "booking@gmail.com" };
        var user = new SaigonWaterbus.Domain.Entities.User { Email = null };

        var email = CreateCustomBookingRequestCommandHandler.ResolveContactEmail(command, user);

        email.ShouldBe("booking@gmail.com");
        user.Email.ShouldBeNull();
    }

    [Test]
    public void ResolveContactEmailExplainsWhenProfileAndBookingHaveNoEmail()
    {
        var command = ValidCommand() with { ContactEmail = null };
        var user = new SaigonWaterbus.Domain.Entities.User { Email = null };

        var exception = Should.Throw<SaigonWaterbus.Application.Common.Exceptions.ValidationException>(() =>
            CreateCustomBookingRequestCommandHandler.ResolveContactEmail(command, user));

        exception.Errors["contactEmail"].ShouldContain(
            "Tài khoản chưa có email. Vui lòng nhập email nhận thông tin vé cho yêu cầu này.");
    }

    [Test]
    public void ValidatorRequiresPositiveRequestedNumberOfDecks()
    {
        var validator = new CreateCustomBookingRequestCommandValidator();
        var command = ValidCommand() with { RequestedNumberOfDecks = 0 };

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x =>
            x.PropertyName == nameof(CreateCustomBookingRequestCommand.RequestedNumberOfDecks));
    }

    [Test]
    public void ValidatorRejectsUnknownRequestedSeatSetupType()
    {
        var validator = new CreateCustomBookingRequestCommandValidator();
        var command = ValidCommand() with { RequestedSeatSetupType = (SeatSetupType)999 };

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x =>
            x.PropertyName == nameof(CreateCustomBookingRequestCommand.RequestedSeatSetupType));
    }

    [Test]
    public void ValidatorRejectsTooManyPassengersAndLongSpecialRequest()
    {
        var validator = new CreateCustomBookingRequestCommandValidator();
        var command = ValidCommand() with
        {
            AdultCount = 500,
            ChildCount = 1,
            SpecialRequests = new string('A', 1001)
        };

        var result = validator.Validate(command);

        result.Errors.ShouldContain(x => x.PropertyName == nameof(CreateCustomBookingRequestCommand.AdultCount));
        result.Errors.ShouldContain(x => x.PropertyName == nameof(CreateCustomBookingRequestCommand.SpecialRequests));
    }

    private static CreateCustomBookingRequestCommand ValidCommand() =>
        new(
            RequestedNumberOfDecks: 2,
            RequestedSeatSetupType: SeatSetupType.StandardAndVip,
            DepartureDate: new DateOnly(2026, 6, 20),
            PreferredStartTime: new TimeOnly(8, 0),
            FromStationId: Guid.NewGuid(),
            ToStationId: Guid.NewGuid(),
            AdultCount: 2,
            ChildCount: 1,
            ContactEmail: "customer@gmail.com",
            ItineraryStops:
            [
                new CreateCustomBookingItineraryStopRequest(1, Guid.NewGuid(), 90)
            ]);
}
