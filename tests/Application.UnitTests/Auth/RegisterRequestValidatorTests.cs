using NUnit.Framework;
using SaigonWaterbus.Application.Auth.Register;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Auth;

public class RegisterRequestValidatorTests
{
    private readonly RegisterRequestValidator _validator = new();

    [Test]
    public void ValidateAcceptsEmailOnlyRegistration()
    {
        var result = _validator.Validate(new RegisterRequest(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Password: "P@ssword123",
            Email: "customer@gmail.com"));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void ValidateAcceptsPhoneOnlyRegistration()
    {
        var result = _validator.Validate(new RegisterRequest(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Password: "P@ssword123",
            Phone: "0901234567"));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void ValidateRejectsMissingOtpChannelWhenEmailAndPhoneAreProvided()
    {
        var result = _validator.Validate(new RegisterRequest(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Password: "P@ssword123",
            Phone: "0901234567",
            Email: "customer@gmail.com"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Vui lòng chọn kênh nhận OTP trước khi đăng ký.");
    }

    [Test]
    public void ValidateRejectsMissingEmailAndPhone()
    {
        var result = _validator.Validate(new RegisterRequest(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Password: "P@ssword123"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Email hoặc số điện thoại là bắt buộc.");
    }

    [Test]
    public void ValidateRejectsMissingDateOfBirth()
    {
        var result = _validator.Validate(new RegisterRequest(
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
        var result = _validator.Validate(new RegisterRequest(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Password: "P@ssword123",
            Email: "customer@gmail.com",
            OtpChannel: "phone"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Số điện thoại là bắt buộc khi chọn nhận OTP qua phone.");
    }

    [Test]
    public void ValidateAcceptsEmailOtpWhenEmailAndPhoneAreProvided()
    {
        var result = _validator.Validate(new RegisterRequest(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Password: "P@ssword123",
            Phone: "0901234567",
            Email: "customer@gmail.com",
            OtpChannel: "email"));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void ValidateAcceptsPhoneOtpWhenEmailAndPhoneAreProvided()
    {
        var result = _validator.Validate(new RegisterRequest(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Password: "P@ssword123",
            Phone: "0901234567",
            Email: "customer@gmail.com",
            OtpChannel: "phone"));

        result.IsValid.ShouldBeTrue();
    }

    [TestCase("sms")]
    [TestCase("sdt")]
    [TestCase("mail")]
    [TestCase("e-mail")]
    [TestCase("so-dien-thoai")]
    public void ValidateRejectsOtpChannelAliases(string otpChannel)
    {
        var result = _validator.Validate(new RegisterRequest(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Password: "P@ssword123",
            Phone: "0901234567",
            Email: "customer@gmail.com",
            OtpChannel: otpChannel));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Kênh OTP chỉ được là email hoặc phone.");
    }

    [Test]
    public void ValidateRejectsTooLongGenderAndNationality()
    {
        var result = _validator.Validate(new RegisterRequest(
            FullName: "Nguyen Van A",
            DateOfBirth: new DateOnly(2003, 9, 2),
            Password: "P@ssword123",
            Email: "customer@gmail.com",
            Gender: new string('a', 31),
            Nationality: new string('b', 101)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.ErrorMessage == "Giới tính không được vượt quá 30 ký tự.");
        result.Errors.ShouldContain(x => x.ErrorMessage == "Quốc tịch không được vượt quá 100 ký tự.");
    }
}
