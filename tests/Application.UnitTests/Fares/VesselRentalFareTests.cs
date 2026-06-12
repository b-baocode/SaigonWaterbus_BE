using SaigonWaterbus.Application.Fares;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Fares;

public class VesselRentalFareTests
{
    [Test]
    public void GetVesselRentalFaresQueryValidatorAcceptsDefaultQuery()
    {
        var validator = new GetVesselRentalFaresQueryValidator();

        var result = validator.Validate(new GetVesselRentalFaresQuery());

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void GetVesselRentalFaresQueryValidatorRejectsEmptyServiceId()
    {
        var validator = new GetVesselRentalFaresQueryValidator();

        var result = validator.Validate(new GetVesselRentalFaresQuery(Guid.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(GetVesselRentalFaresQuery.ServiceId));
    }

    [Test]
    public void GetVesselRentalFaresQueryValidatorRejectsLongSearch()
    {
        var validator = new GetVesselRentalFaresQueryValidator();

        var result = validator.Validate(new GetVesselRentalFaresQuery(Search: new string('A', 101)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(GetVesselRentalFaresQuery.Search));
    }
}
