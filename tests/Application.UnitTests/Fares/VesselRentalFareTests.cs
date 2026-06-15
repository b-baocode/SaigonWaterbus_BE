using SaigonWaterbus.Application.Fares;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
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
    public void GetVesselRentalFaresQueryValidatorRejectsLongSearch()
    {
        var validator = new GetVesselRentalFaresQueryValidator();

        var result = validator.Validate(new GetVesselRentalFaresQuery(Search: new string('A', 101)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(GetVesselRentalFaresQuery.Search));
    }

    [Test]
    public async Task RentalListIncludesReadyVesselWithoutAssignedService()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var vessel = SeatFlowTestData.Vessel(
            SeatSetupType.StandardAndVip,
            seatsConfigured: true,
            status: VesselStatus.Active);
        vessel.RentalPrices.Add(new VesselRentalPrice
        {
            VesselId = vessel.Id,
            Vessel = vessel,
            RentalUnit = VesselRentalUnit.Day,
            UnitPrice = 15000000m,
            Currency = "VND"
        });
        context.Add(vessel);
        await context.SaveChangesAsync();

        var result = await new GetVesselRentalFaresQueryHandler(context)
            .Handle(new GetVesselRentalFaresQuery(), CancellationToken.None);

        result.Single().VesselId.ShouldBe(vessel.Id);
        result.Single().UnitPrice.ShouldBe(15000000m);
    }
}
