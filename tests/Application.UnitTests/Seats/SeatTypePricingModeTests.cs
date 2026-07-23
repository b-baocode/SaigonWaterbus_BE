using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Seats;

public class SeatTypePricingModeTests
{
    [Test]
    public async Task SeatTypeListMarksStandardAsDistanceFareAndVipTypesAsBasePrice()
    {
        await using var context = SeatFlowTestData.CreateContext();

        var rows = await new GetSeatTypeListQueryHandler(context)
            .Handle(new GetSeatTypeListQuery(), CancellationToken.None);

        var standard = rows.Single(x => x.Code == "STANDARD");
        standard.PricingMode.ShouldBe(SeatSupport.DistanceFarePricingMode);
        standard.PriceNote.ShouldNotBeNull();
        standard.PriceNote!.ShouldContain("/api/fare-policy");

        rows.Single(x => x.Code == "CABIN")
            .PricingMode.ShouldBe(SeatSupport.SeatTypeBasePricePricingMode);
    }

    [Test]
    public async Task UpdatingStandardSeatTypePriceIsRejected()
    {
        await using var context = SeatFlowTestData.CreateContext();

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            new UpdateSeatTypePriceCommandHandler(context).Handle(
                new UpdateSeatTypePriceCommand("STANDARD", 20_000m),
                CancellationToken.None));

        exception.Errors.SelectMany(x => x.Value)
            .ShouldContain(x => x.Contains("/api/fare-policy"));
    }
}
