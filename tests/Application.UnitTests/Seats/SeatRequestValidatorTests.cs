using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Domain.Enums;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Seats;

public class SeatRequestValidatorTests
{
    [Test]
    public void GenerateRejectsNullDecksWithoutThrowing()
    {
        var validator = new GenerateSeatsRequestValidator();

        var result = validator.Validate(new GenerateSeatsRequest(1, null!));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(GenerateSeatsRequest.Decks));
    }

    [Test]
    public void GenerateRejectsDuplicateDeckNumbers()
    {
        var validator = new GenerateSeatsRequestValidator();

        var result = validator.Validate(new GenerateSeatsRequest(
            1,
            [
                new DeckConfigDto(1, 2, 10),
                new DeckConfigDto(1, 2, 10)
            ]));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(GenerateSeatsRequest.Decks));
    }

    [Test]
    public void GenerateAcceptsValidDeckConfiguration()
    {
        var validator = new GenerateSeatsRequestValidator();

        var result = validator.Validate(new GenerateSeatsRequest(
            1,
            [
                new DeckConfigDto(1, 2, 10),
                new DeckConfigDto(2, 2, 10)
            ]));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void GenerateAcceptsLayoutWithSeatBlocksAndToilet()
    {
        var validator = new GenerateSeatsRequestValidator();

        var result = validator.Validate(new GenerateSeatsRequest(
            1,
            [
                new DeckConfigDto(
                    1,
                    20,
                    8,
                    [
                        new SeatBlockDto(1, 1, 10, 4),
                        new SeatBlockDto(1, 5, 10, 4)
                    ],
                    [
                        new FacilityConfigDto(VesselFacilityType.Toilet, 15, 1, 1, 2)
                    ])
            ]));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void GenerateRejectsToiletThatDoesNotUseExactlyTwoCells()
    {
        var validator = new GenerateSeatsRequestValidator();

        var result = validator.Validate(new GenerateSeatsRequest(
            1,
            [
                new DeckConfigDto(
                    1,
                    20,
                    8,
                    [new SeatBlockDto(1, 1, 10, 8)],
                    [new FacilityConfigDto(VesselFacilityType.Toilet, 15, 1, 2, 2)])
            ]));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "WC phải chiếm đúng 2 ô, theo chiều ngang hoặc chiều dọc.");
    }

    [Test]
    public void UpdateStatusRejectsMissingIsActive()
    {
        var validator = new UpdateSeatStatusRequestValidator();

        var result = validator.Validate(new UpdateSeatStatusRequest(1, 1, null));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(UpdateSeatStatusRequest.IsActive));
    }
}
