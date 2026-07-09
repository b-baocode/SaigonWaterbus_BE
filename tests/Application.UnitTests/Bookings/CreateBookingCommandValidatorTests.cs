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

    private static CreateBookingCommand Command(params BookingItemRequest[] items) =>
        new("TR-TEST", items, null);

    private static BookingItemRequest Adult(string seat) =>
        new(seat, "ADULT", "BD", "TADA", "Nguyen Van A", null, null, null, null, null);

    private static BookingItemRequest LapInfant(int? birthYear = 2025) =>
        new(null, "INFANT", "BD", "TADA", "Be Nguyen Van B", null, birthYear, null, null, null);
}
