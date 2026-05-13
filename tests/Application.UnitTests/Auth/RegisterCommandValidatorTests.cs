using SaigonWaterbus.Application.Auth.Register;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Auth;

public class RegisterCommandValidatorTests
{
    private readonly RegisterCommandValidator _validator = new();

    [Test]
    public void ValidateAcceptsInternationalPhoneAndSupportedEmailOtp()
    {
        var result = _validator.Validate(new RegisterCommand(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Phone: "+12025550123",
            Password: "P@ssword123",
            Email: "customer@gmail.com"));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void ValidateAcceptsVietnamPhoneWithoutEmail()
    {
        var result = _validator.Validate(new RegisterCommand(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Phone: "+84901234567",
            Password: "P@ssword123"));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void ValidateAcceptsVietnamPhoneOtpChannel()
    {
        var result = _validator.Validate(new RegisterCommand(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Phone: "+84901234567",
            Password: "P@ssword123",
            Email: "customer@gmail.com",
            OtpChannel: "phone"));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void ValidateRejectsInternationalPhoneWithoutSupportedEmail()
    {
        var result = _validator.Validate(new RegisterCommand(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Phone: "+12025550123",
            Password: "P@ssword123"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Số điện thoại quốc tế bắt buộc nhập email được hỗ trợ để nhận OTP.");
    }

    [Test]
    public void ValidateRejectsInternationalPhoneOtpChannel()
    {
        var result = _validator.Validate(new RegisterCommand(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Phone: "+12025550123",
            Password: "P@ssword123",
            Email: "customer@gmail.com",
            OtpChannel: "phone"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Số điện thoại quốc tế chỉ hỗ trợ OTP qua email.");
    }

    [Test]
    public void ValidateRequiresSupportedEmailDomain()
    {
        var result = _validator.Validate(new RegisterCommand(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Phone: "+84901234567",
            Password: "P@ssword123",
            Email: "customer@yahoo.com"));

        result.IsValid.ShouldBeFalse();
    }
}
