using NUnit.Framework;
using SaigonWaterbus.Application.Auth.Login;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Auth;

public class VerifyGooglePhoneRequestValidatorTests
{
    private readonly VerifyGooglePhoneRequestValidator _validator = new();

    [Test]
    public void ValidateRequiresTempToken()
    {
        var result = _validator.Validate(new VerifyGooglePhoneRequest(string.Empty, "123456"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Temp token là bắt buộc.");
    }

    [Test]
    public void ValidateRequiresOtp()
    {
        var result = _validator.Validate(new VerifyGooglePhoneRequest("temp-token", string.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Mã OTP là bắt buộc.");
    }

    [Test]
    public void ValidateRejectsInvalidOtpLength()
    {
        var result = _validator.Validate(new VerifyGooglePhoneRequest("temp-token", "123"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Mã OTP không hợp lệ.");
    }

    [Test]
    public void ValidateAcceptsOtpWithTempTokenOnly()
    {
        var result = _validator.Validate(new VerifyGooglePhoneRequest("temp-token", "123456"));

        result.IsValid.ShouldBeTrue();
    }
}
