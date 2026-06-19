using NUnit.Framework;
using SaigonWaterbus.Application.CustomBookingRequests;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.CustomBookingRequests;

public class QuoteCustomBookingRequestCommandValidatorTests
{
    [TestCase(1)]
    [TestCase(50)]
    [TestCase(100)]
    public void ValidatorAcceptsValidDepositPercent(decimal depositPercent)
    {
        var validator = new QuoteCustomBookingRequestCommandValidator();

        var result = validator.Validate(ValidCommand(depositPercent));

        result.IsValid.ShouldBeTrue();
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(100.01)]
    public void ValidatorRejectsInvalidDepositPercent(decimal depositPercent)
    {
        var validator = new QuoteCustomBookingRequestCommandValidator();

        var result = validator.Validate(ValidCommand(depositPercent));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(QuoteCustomBookingRequestCommand.DepositPercent));
    }

    [TestCase(0)]
    [TestCase(400000)]
    [TestCase(123)]
    public void ValidatorAcceptsValidServiceFeeAmount(decimal serviceFeeAmount)
    {
        var validator = new QuoteCustomBookingRequestCommandValidator();

        var result = validator.Validate(ValidCommand(50) with { ServiceFeeAmount = serviceFeeAmount });

        result.IsValid.ShouldBeTrue();
    }

    [TestCase(-1)]
    [TestCase(123.45)]
    [TestCase(123.456)]
    public void ValidatorRejectsInvalidServiceFeeAmount(decimal serviceFeeAmount)
    {
        var validator = new QuoteCustomBookingRequestCommandValidator();

        var result = validator.Validate(ValidCommand(50) with { ServiceFeeAmount = serviceFeeAmount });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(QuoteCustomBookingRequestCommand.ServiceFeeAmount));
    }

    private static QuoteCustomBookingRequestCommand ValidCommand(decimal depositPercent) =>
        new(
            Guid.NewGuid(),
            DepositPercent: depositPercent,
            PriceNote: null);
}
