using SaigonWaterbus.Application.Auth.Register;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Auth;

public class RegisterCommandValidatorTests
{
    private readonly RegisterCommandValidator _validator = new();

    [Test]
    public void ValidateAcceptsEmailOnlyRegistration()
    {
        var result = _validator.Validate(new RegisterCommand(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Password: "P@ssword123",
            Email: "customer@gmail.com"));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void ValidateAcceptsPhoneOnlyRegistration()
    {
        var result = _validator.Validate(new RegisterCommand(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Password: "P@ssword123",
            Phone: "0901234567"));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void ValidateRejectsMissingEmailAndPhone()
    {
        var result = _validator.Validate(new RegisterCommand(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Password: "P@ssword123"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Email hoặc số điện thoại là bắt buộc.");
    }

    [Test]
    public void ValidateRejectsMissingDateOfBirth()
    {
        var result = _validator.Validate(new RegisterCommand(
            FullName: "Nguyen Van A",
            DateOfBirth: default,
            Password: "P@ssword123",
            Email: "customer@gmail.com"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Ngày sinh là bắt buộc.");
    }

    [Test]
    public void ValidateRejectsPhoneOtpWithoutPhone()
    {
        var result = _validator.Validate(new RegisterCommand(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Password: "P@ssword123",
            Email: "customer@gmail.com",
            OtpChannel: "phone"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Số điện thoại là bắt buộc khi chọn nhận OTP qua phone.");
    }

    [Test]
    public void ValidateRejectsEmailOtpWhenPhoneIsProvided()
    {
        var result = _validator.Validate(new RegisterCommand(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Password: "P@ssword123",
            Phone: "0901234567",
            Email: "customer@gmail.com",
            OtpChannel: "email"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "OTP qua email chỉ hỗ trợ khi không có số điện thoại.");
    }
}
