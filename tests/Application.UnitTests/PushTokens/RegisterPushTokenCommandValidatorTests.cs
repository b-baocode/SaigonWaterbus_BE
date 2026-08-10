using FluentValidation.TestHelper;
using NUnit.Framework;
using SaigonWaterbus.Application.PushTokens;
using SaigonWaterbus.Application.PushTokens.Validators;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.UnitTests.PushTokens;

[TestFixture]
public sealed class RegisterPushTokenCommandValidatorTests
{
    private readonly RegisterPushTokenCommandValidator _sut = new();

    [Test]
    public void Valid_IosToken_Passes()
    {
        var cmd = new RegisterPushTokenCommand(
            "ExponentPushToken[abc123XYZ-_]",
            PushPlatform.Ios,
            "iPhone15,2-abc",
            "1.2.3");
        _sut.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void Valid_AndroidToken_Passes()
    {
        var cmd = new RegisterPushTokenCommand(
            "ExponentPushToken[xyz456]",
            PushPlatform.Android);
        _sut.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }

    [TestCase("")]
    [TestCase("ExponentPushToken")]
    [TestCase("ExponentPushToken[")]
    [TestCase("ExponentPushToken[abc")]
    [TestCase("not-a-token")]
    public void InvalidToken_Fails(string token)
    {
        var cmd = new RegisterPushTokenCommand(token, PushPlatform.Ios);
        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.ExpoPushToken);
    }

    [Test]
    public void EmptyDeviceId_Passes()
    {
        var cmd = new RegisterPushTokenCommand(
            "ExponentPushToken[abc]",
            PushPlatform.Ios,
            DeviceId: null);
        _sut.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void OversizedDeviceId_Fails()
    {
        var cmd = new RegisterPushTokenCommand(
            "ExponentPushToken[abc]",
            PushPlatform.Ios,
            DeviceId: new string('x', 256));
        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.DeviceId);
    }
}
