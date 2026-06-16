using SaigonWaterbus.Application.CustomBookingRequests;
using NUnit.Framework;
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

    private static QuoteCustomBookingRequestCommand ValidCommand(decimal depositPercent) =>
        new(
            Guid.NewGuid(),
            QuotedPrice: 15000000m,
            DepositPercent: depositPercent,
            Currency: "VND",
            PriceNote: null);
}
