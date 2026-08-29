using NUnit.Framework;
using SaigonWaterbus.Application.Users;
using SaigonWaterbus.Domain.Constants;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Users;

public class UserRequestValidatorTests
{
    [TestCase("@@@@@!!")]
    [TestCase("Nguyen Van A123")]
    [TestCase("Nguyen-Van-A")]
    public void CreateUserRejectsFullNameContainingNonLetters(string fullName)
    {
        var result = new CreateUserRequestValidator().Validate(new CreateUserRequest(
            FullName: fullName,
            DateOfBirth: new DateOnly(1998, 5, 10),
            PhoneNumber: "0912345678",
            Email: "staff@gmail.com",
            RoleId: Guid.NewGuid()));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x =>
            x.PropertyName == nameof(CreateUserRequest.FullName)
            && x.ErrorMessage == "Họ và tên chỉ được chứa chữ cái và khoảng trắng.");
    }

    [Test]
    public void CreateUserAcceptsVietnameseFullName()
    {
        var result = new CreateUserRequestValidator().Validate(new CreateUserRequest(
            FullName: "Nguyễn Thị Bích Ngọc",
            DateOfBirth: new DateOnly(1998, 5, 10),
            PhoneNumber: "0912345678",
            Email: "staff@gmail.com",
            RoleId: Guid.NewGuid()));

        result.Errors.ShouldNotContain(x => x.PropertyName == nameof(CreateUserRequest.FullName));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void CreateUserRequiresPhoneNumber(string? phoneNumber)
    {
        var result = new CreateUserRequestValidator().Validate(new CreateUserRequest(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(1998, 5, 10),
            PhoneNumber: phoneNumber,
            Email: "staff@gmail.com",
            RoleId: Guid.NewGuid()));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x =>
            x.PropertyName == nameof(CreateUserRequest.PhoneNumber)
            && x.ErrorMessage == "Số điện thoại là bắt buộc.");
    }

    [Test]
    public void UpdateUserRejectsFullNameContainingSpecialCharacters()
    {
        var result = new UpdateUserRequestValidator().Validate(new UpdateUserRequest(
            UserId: Guid.NewGuid(),
            FullName: "@@@@@!!",
            DateOfBirth: null,
            PhoneNumber: null,
            Email: null,
            RoleId: null));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x =>
            x.PropertyName == nameof(UpdateUserRequest.FullName)
            && x.ErrorMessage == "Họ và tên chỉ được chứa chữ cái và khoảng trắng.");
    }

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
