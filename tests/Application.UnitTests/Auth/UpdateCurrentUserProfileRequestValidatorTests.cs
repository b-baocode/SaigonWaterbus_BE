using SaigonWaterbus.Application.Auth.Profile;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Auth;

public class UpdateCurrentUserProfileRequestValidatorTests
{
    private readonly UpdateCurrentUserProfileRequestValidator _validator = new();

    [Test]
    public void ValidateRejectsTooLongGenderAndNationality()
    {
        var result = _validator.Validate(new UpdateCurrentUserProfileRequest(
            Gender: new string('a', 31),
            Nationality: new string('b', 101)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Giới tính không được vượt quá 30 ký tự.");
        result.Errors.ShouldContain(x => x.ErrorMessage == "Quốc tịch không được vượt quá 100 ký tự.");
    }
}
