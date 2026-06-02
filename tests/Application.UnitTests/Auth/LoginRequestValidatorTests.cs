using SaigonWaterbus.Application.Auth.Login;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Auth;

public class LoginRequestValidatorTests
{
    private readonly LoginRequestValidator _validator = new();

    [Test]
    public void ValidateRequiresEmailOrPhone()
    {
        var result = _validator.Validate(new LoginRequest(
            EmailOrPhone: string.Empty,
            Password: "P@ssword123"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Email hoặc số điện thoại là bắt buộc.");
    }

    [Test]
    public void ValidateAcceptsEmail()
    {
        var result = _validator.Validate(new LoginRequest(
            EmailOrPhone: "customer@gmail.com",
            Password: "P@ssword123"));

        result.IsValid.ShouldBeTrue();
    }

    [TestCase("0901234567")]
    [TestCase("+84901234567")]
    public void ValidateAcceptsVietnamesePhone(string phone)
    {
        var result = _validator.Validate(new LoginRequest(
            EmailOrPhone: phone,
            Password: "P@ssword123"));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void ValidateRejectsNonVietnamesePhone()
    {
        var result = _validator.Validate(new LoginRequest(
            EmailOrPhone: "+14155552671",
            Password: "P@ssword123"));

        result.IsValid.ShouldBeFalse();
    }
}
