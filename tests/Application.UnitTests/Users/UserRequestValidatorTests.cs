using NUnit.Framework;
using SaigonWaterbus.Application.Users;
using SaigonWaterbus.Domain.Constants;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Users;

public class UserRequestValidatorTests
{
    [Test]
    public void CreateUserValidateRejectsTooLongGenderAndNationality()
    {
        var validator = new CreateUserRequestValidator();
        var result = validator.Validate(new CreateUserRequest(
            FullName: "Tran Thi B",
            DateOfBirth: new DateOnly(1998, 5, 10),
            PhoneNumber: "0912345678",
            Email: "thib@gmail.com",
            RoleId: Guid.NewGuid(),
            Gender: new string('a', 31),
            Nationality: new string('b', 101)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Giới tính không được vượt quá 30 ký tự.");
        result.Errors.ShouldContain(x => x.ErrorMessage == "Quốc tịch không được vượt quá 100 ký tự.");
    }

    [Test]
    public void UpdateUserValidateRejectsTooLongGenderAndNationality()
    {
        var validator = new UpdateUserRequestValidator();
        var result = validator.Validate(new UpdateUserRequest(
            UserId: Guid.NewGuid(),
            FullName: null,
            DateOfBirth: null,
            PhoneNumber: null,
            Email: null,
            RoleId: null,
            Gender: new string('a', 31),
            Nationality: new string('b', 101)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Giới tính không được vượt quá 30 ký tự.");
        result.Errors.ShouldContain(x => x.ErrorMessage == "Quốc tịch không được vượt quá 100 ký tự.");
    }

    [Test]
    public void ManagedUserPasswordSupportGeneratesStrongPassword()
    {
        var password = ManagedUserPasswordSupport.GeneratePassword();

        password.Length.ShouldBe(12);
        PasswordRules.IsStrong(password).ShouldBeTrue();
    }
}
