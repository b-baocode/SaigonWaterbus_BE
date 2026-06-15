using SaigonWaterbus.Application.Fares;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Fares;

public class GetFareByServiceSeatTypeQueryValidatorTests
{
    [Test]
    public void RejectsMissingServiceAndSeatType()
    {
        var validator = new GetFareByServiceSeatTypeQueryValidator();

        var result = validator.Validate(new GetFareByServiceSeatTypeQuery(
            "R01",
            "BD",
            "LD",
            Guid.Empty,
            string.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x =>
            x.PropertyName == nameof(GetFareByServiceSeatTypeQuery.ServiceId));
        result.Errors.ShouldContain(x =>
            x.PropertyName == nameof(GetFareByServiceSeatTypeQuery.SeatTypeCode));
    }
}
