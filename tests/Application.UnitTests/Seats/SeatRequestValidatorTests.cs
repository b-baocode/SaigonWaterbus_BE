using NUnit.Framework;
using SaigonWaterbus.Application.Seats;
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
    public void GenerateAcceptsLayoutCellsForSpecialPositions()
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
                        new LayoutCellConfigDto(5, 4, SeatLayoutCellType.Seat, SeatTypeCode: "CABIN")
                    ])
            ]));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void GenerateRejectsDuplicateLayoutCells()
    {
        var validator = new GenerateSeatsRequestValidator();

        var result = validator.Validate(new GenerateSeatsRequest(
            Guid.NewGuid(),
            [
                new DeckConfigDto(
                    1,
                    2,
                    2,
                    Cells:
                    [
                        new LayoutCellConfigDto(1, 1, SeatLayoutCellType.Aisle),
                        new LayoutCellConfigDto(1, 1, SeatLayoutCellType.Empty)
                    ])
            ]));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Các ô layout trong cùng tầng không được trùng vị trí.");
    }

    [Test]
    public void GenerateRejectsLayoutCellsOutsideDeckSize()
    {
        var validator = new GenerateSeatsRequestValidator();

        var result = validator.Validate(new GenerateSeatsRequest(
            Guid.NewGuid(),
            [
                new DeckConfigDto(
                    1,
                    2,
                    2,
                    Cells:
                    [
                        new LayoutCellConfigDto(3, 1, SeatLayoutCellType.Aisle)
                    ])
            ]));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Ô layout không được vượt quá kích thước tầng.");
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
