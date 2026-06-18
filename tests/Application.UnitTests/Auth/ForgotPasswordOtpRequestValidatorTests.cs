using NUnit.Framework;
using SaigonWaterbus.Application.Auth.Password;
using SaigonWaterbus.Domain.Constants;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Auth;

public class ForgotPasswordOtpRequestValidatorTests
{
    private readonly ForgotPasswordOtpRequestValidator _validator = new();

    [Test]
    public void ValidateRequiresEmailOrPhone()
    {
        var result = _validator.Validate(new ForgotPasswordOtpRequest());

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Email hoặc số điện thoại là bắt buộc.");
    }

    [Test]
    public void ValidateRejectsInvalidEmailOrPhone()
    {
        var result = _validator.Validate(new ForgotPasswordOtpRequest(
            EmailOrPhone: "invalid"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Vui lòng nhập email được hỗ trợ đúng định dạng hoặc số điện thoại hợp lệ.");
    }

    [Test]
    public void ValidateAcceptsEmail()
    {
        var result = _validator.Validate(new ForgotPasswordOtpRequest(
            EmailOrPhone: "customer@gmail.com"));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void ValidateRejectsUnsupportedEmailDomain()
    {
        var result = _validator.Validate(new ForgotPasswordOtpRequest(
            EmailOrPhone: "customer@yahoo.com"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == EmailRules.AllowedEmailDomainMessage);
    }

    [TestCase("+84901234567")]
    [TestCase("0901234567")]
    public void ValidateAcceptsVietnamesePhoneOnly(string phone)
    {
        var result = _validator.Validate(new ForgotPasswordOtpRequest(
            EmailOrPhone: phone));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void ValidateRejectsNonVietnamesePhone()
    {
        var result = _validator.Validate(new ForgotPasswordOtpRequest(
            EmailOrPhone: "+14155552671"));

        result.IsValid.ShouldBeFalse();
    }
}
