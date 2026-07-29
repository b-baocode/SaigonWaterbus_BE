using NUnit.Framework;
using SaigonWaterbus.Application.Bookings;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Bookings;

public class CreateBookingCommandValidatorTests
{
    private static readonly CreateBookingCommandValidator Validator = new();

    [Test]
    public void InfantWithoutBirthYearIsInvalid()
    {
        var result = Validator.Validate(Command(Adult("A1"), LapInfant(birthYear: null)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("birthYear"));
    }

    [Test]
    public void LapInfantWithoutSeatedCompanionIsInvalid()
    {
        var result = Validator.Validate(Command(LapInfant()));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("đi kèm"));
    }

    [Test]
    public void LapInfantWithSeatedAdultCompanionIsValid()
    {
        var result = Validator.Validate(Command(Adult("A1"), LapInfant()));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void ChildWithoutBirthYearIsInvalid()
    {
        var result = Validator.Validate(Command(Adult("A1"), Child("A2", birthYear: null)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("birthYear"));
    }

    [Test]
    public void ChildWithoutAdultCompanionIsInvalid()
    {
        var result = Validator.Validate(Command(Child("A1")));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("đi kèm"));
    }

    [Test]
    public void ChildWithSeatedAdultCompanionIsValid()
    {
        var result = Validator.Validate(Command(Adult("A1"), Child("A2")));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void MultipleChildrenWithOneSeatedAdultIsValid()
    {
        var result = Validator.Validate(Command(Adult("A1"), Child("A2"), Child("A3")));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void MoreLapInfantsThanSeatedCompanionsIsInvalid()
    {
        var result = Validator.Validate(Command(Adult("A1"), LapInfant(), LapInfant()));

        result.IsValid.ShouldBeFalse();
    }

    [Test]
    public void SeatedInfantWithBirthYearIsValid()
    {
        var result = Validator.Validate(Command(Adult("A1"), LapInfant(birthYear: 2025) with { SeatNumber = "A2" }));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void RoundTripWithReturnTripCodeAndItemsIsValid()
    {
        var result = Validator.Validate(
            Command(Adult("A1")) with { ReturnTripCode = "TR-RET", ReturnItems = [Adult("B1")] });

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void ReturnTripCodeWithoutReturnItemsIsInvalid()
    {
        var result = Validator.Validate(
            Command(Adult("A1")) with { ReturnTripCode = "TR-RET" });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == "ReturnItems");
    }

    [Test]
    public void ReturnItemsWithoutReturnTripCodeIsInvalid()
    {
        var result = Validator.Validate(
            Command(Adult("A1")) with { ReturnItems = [Adult("B1")] });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == "ReturnTripCode");
    }

    [Test]
    public void MoreThanTenReturnItemsIsInvalid()
    {
        var returnItems = Enumerable.Range(1, 11).Select(i => Adult($"A{i}")).ToArray();
        var result = Validator.Validate(
            Command(Adult("A1")) with { ReturnTripCode = "TR-RET", ReturnItems = returnItems });

        result.IsValid.ShouldBeFalse();
    }

    [Test]
    public void LapInfantCompanionRuleIsEvaluatedPerLeg()
    {
        // Chiều đi hợp lệ (người lớn + trẻ), nhưng chiều về chỉ có trẻ ngồi lòng → invalid.
        var result = Validator.Validate(
            Command(Adult("A1"), LapInfant()) with
            {
                ReturnTripCode = "TR-RET",
                ReturnItems = [LapInfant()]
            });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("đi kèm"));
    }

    [Test]
    public void ReturnItemsFollowTheSameItemRules()
    {
        // Vé ADULT chiều về không có ghế → invalid.
        var result = Validator.Validate(
            Command(Adult("A1")) with
            {
                ReturnTripCode = "TR-RET",
                ReturnItems = [Adult("B1") with { SeatNumber = null }]
            });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("seatNumber"));
    }

    [Test]
    public void ItemWithoutStationCodesIsValid()
    {
        // Chuyến ngắm cảnh đi nguyên chuyến không gửi trạm; bắt buộc/khác nhau enforce ở handler
        // vì validator không biết chuyến nào bán theo chặng.
        var result = Validator.Validate(
            Command(Adult("A1") with { FromStationCode = null, ToStationCode = null }));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void ItemWithSameFromAndToStationIsValid()
    {
        // Tuyến vòng lặp có bến đầu = bến cuối nên không còn chặn ở validator.
        var result = Validator.Validate(
            Command(Adult("A1") with { FromStationCode = "BD", ToStationCode = "BD" }));

        result.IsValid.ShouldBeTrue();
    }

    private static CreateBookingCommand Command(params BookingItemRequest[] items) =>
        new("TR-TEST", items, null);

    private static BookingItemRequest Adult(string seat) =>
        new(seat, "ADULT", "BD", "TADA", "Nguyen Van A", null, null, null, null, null);

    private static BookingItemRequest Child(string seat, int? birthYear = 2020) =>
        new(seat, "CHILD", "BD", "TADA", "Be Nguyen Van B", null, birthYear, null, null, null);

    private static BookingItemRequest LapInfant(int? birthYear = 2025) =>
        new(null, "INFANT", "BD", "TADA", "Be Nguyen Van B", null, birthYear, null, null, null);
}
