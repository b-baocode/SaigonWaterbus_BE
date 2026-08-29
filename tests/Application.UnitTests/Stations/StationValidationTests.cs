using NUnit.Framework;
using SaigonWaterbus.Application.Stations;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using Shouldly;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.UnitTests.Stations;

public class StationValidationTests
{
    [TestCase("ST-BD-01")]
    [TestCase("st-01")]
    public void CreateValidatorAcceptsAlphanumericStationCodeWithHyphens(string stationCode)
    {
        var result = new CreateStationCommandValidator().Validate(CreateCommand(
            stationCode,
            "Bến Bạch Đằng 1"));

        result.IsValid.ShouldBeTrue();
    }

    [TestCase("ST_TEST")]
    [TestCase("ST%01")]
    [TestCase("-ST01")]
    [TestCase("ST01-")]
    [TestCase("ST--01")]
    public void CreateValidatorRejectsInvalidStationCodeCharacters(string stationCode)
    {
        var result = new CreateStationCommandValidator().Validate(CreateCommand(
            stationCode,
            "Bến Bạch Đằng"));

        result.Errors.ShouldContain(x => x.PropertyName == nameof(CreateStationCommand.StationCode));
    }

    [TestCase("Bến Bạch Đằng %")]
    [TestCase("Bến @ Bạch Đằng")]
    [TestCase("Nhà ga #1")]
    [TestCase("Nhà-ga")]
    public void CreateAndUpdateValidatorsRejectSpecialCharactersInStationName(string stationName)
    {
        var createResult = new CreateStationCommandValidator().Validate(CreateCommand("ST-01", stationName));
        var updateResult = new UpdateStationCommandValidator().Validate(UpdateCommand(Guid.NewGuid(), stationName));

        createResult.Errors.ShouldContain(x => x.PropertyName == nameof(CreateStationCommand.StationName));
        updateResult.Errors.ShouldContain(x => x.PropertyName == nameof(UpdateStationCommand.StationName));
    }

    [Test]
    public async Task CreateRejectsDuplicateStationNameIgnoringCaseAndOuterSpaces()
    {
        await using var context = SeatFlowTestData.CreateContext();
        context.Stations.Add(Station("BD", "Bến Bạch Đằng"));
        await context.SaveChangesAsync();

        var act = () => new CreateStationCommandHandler(context)
            .Handle(CreateCommand("BD-02", "  bến bạch đằng  "), CancellationToken.None);

        var exception = await act.ShouldThrowAsync<ValidationException>();
        exception.Errors["stationName"].Single().ShouldBe("Tên nhà ga đã tồn tại.");
    }

    [Test]
    public async Task UpdateRejectsAnotherStationDuplicateNameButAllowsItsCurrentName()
    {
        await using var context = SeatFlowTestData.CreateContext();
        var first = Station("BD", "Bến Bạch Đằng");
        var second = Station("TD", "Bến Thủ Đức");
        context.Stations.AddRange(first, second);
        await context.SaveChangesAsync();

        var handler = new UpdateStationCommandHandler(context);
        await handler.Handle(UpdateCommand(first.Id, "  Bến   Bạch Đằng  "), CancellationToken.None);
        first.StationName.ShouldBe("Bến Bạch Đằng");

        var act = () => handler.Handle(
            UpdateCommand(second.Id, "bến bạch đằng"),
            CancellationToken.None);
        var exception = await act.ShouldThrowAsync<ValidationException>();
        exception.Errors["stationName"].Single().ShouldBe("Tên nhà ga đã tồn tại.");
    }

    private static CreateStationCommand CreateCommand(string code, string name) =>
        new(code, name, null, 10.77m, 106.70m);

    private static UpdateStationCommand UpdateCommand(Guid id, string name) =>
        new(
            id,
            name,
            null,
            null,
            null,
            null,
            StationStatus.Active,
            null,
            null,
            null,
            null,
            null);

    private static Station Station(string code, string name) =>
        new()
        {
            StationCode = code,
            StationName = name,
            Status = StationStatus.Active
        };
}
