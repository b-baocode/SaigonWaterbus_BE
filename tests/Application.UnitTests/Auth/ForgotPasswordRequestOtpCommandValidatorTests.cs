using SaigonWaterbus.Application.Auth.Password;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Auth;

public class ForgotPasswordRequestOtpCommandValidatorTests
{
    private readonly ForgotPasswordRequestOtpCommandValidator _validator = new();

    [Test]
    public void ValidateRequiresEmailOrPhone()
    {
        var result = _validator.Validate(new ForgotPasswordRequestOtpCommand());

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Email hoặc số điện thoại là bắt buộc.");
    }

    [Test]
    public void ValidateRejectsInvalidEmailOrPhone()
    {
        var result = _validator.Validate(new ForgotPasswordRequestOtpCommand(
            EmailOrPhone: "invalid"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Vui lòng nhập email đúng định dạng hoặc số điện thoại hợp lệ.");
    }

    [Test]
    public void ValidateAcceptsEmail()
    {
        var result = _validator.Validate(new ForgotPasswordRequestOtpCommand(
            EmailOrPhone: "customer@example.com"));

        result.IsValid.ShouldBeTrue();
    }

    [TestCase("+84901234567")]
    [TestCase("+14155552671")]
    public void ValidateAcceptsInternationalPhoneOnly(string phone)
    {
        var result = _validator.Validate(new ForgotPasswordRequestOtpCommand(
            EmailOrPhone: phone));

        result.IsValid.ShouldBeTrue();
    }
}
