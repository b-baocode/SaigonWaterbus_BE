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

        var result = validator.Validate(new GenerateSeatsRequest(Guid.NewGuid(), null!));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(GenerateSeatsRequest.Decks));
    }

    [Test]
    public void GenerateRejectsDuplicateDeckNumbers()
    {
        var validator = new GenerateSeatsRequestValidator();

        var result = validator.Validate(new GenerateSeatsRequest(
            Guid.NewGuid(),
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
            Guid.NewGuid(),
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
            Guid.NewGuid(),
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
    public void GenerateAcceptsCellsAsOverridesWithoutListingEverySeat()
    {
        var validator = new GenerateSeatsRequestValidator();

        var result = validator.Validate(new GenerateSeatsRequest(
            Guid.NewGuid(),
            [
                new DeckConfigDto(
                    1,
                    5,
                    6,
                    Cells:
                    [
                        new LayoutCellConfigDto(1, 3, SeatLayoutCellType.Aisle),
                        new LayoutCellConfigDto(2, 3, SeatLayoutCellType.Aisle),
                        new LayoutCellConfigDto(3, 1, SeatLayoutCellType.Empty),
                        new LayoutCellConfigDto(4, 1, SeatLayoutCellType.Toilet, RowSpan: 1, ColumnSpan: 2),
                        new LayoutCellConfigDto(5, 4, SeatLayoutCellType.Seat, SeatTypeCode: "VIP")
                    ])
            ]));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void GenerateRejectsMixedCellsAndSeatBlocks()
    {
        var validator = new GenerateSeatsRequestValidator();

        var result = validator.Validate(new GenerateSeatsRequest(
            Guid.NewGuid(),
            [
                new DeckConfigDto(
                    1,
                    5,
                    6,
                    [new SeatBlockDto(1, 1, 5, 6)],
                    Cells:
                    [
                        new LayoutCellConfigDto(1, 3, SeatLayoutCellType.Aisle)
                    ])
            ]));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Khi dùng cells thì không gửi seatBlocks/facilities để tránh cấu hình bị lẫn logic.");
    }

    [Test]
    public void GenerateRejectsToiletThatDoesNotUseExactlyTwoCells()
    {
        var validator = new GenerateSeatsRequestValidator();

        var result = validator.Validate(new GenerateSeatsRequest(
            Guid.NewGuid(),
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

        var result = validator.Validate(new UpdateSeatStatusRequest(Guid.NewGuid(), Guid.NewGuid(), null));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(UpdateSeatStatusRequest.IsActive));
    }
}
