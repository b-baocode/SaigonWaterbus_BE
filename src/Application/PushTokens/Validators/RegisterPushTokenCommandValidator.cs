using FluentValidation;
using SaigonWaterbus.Application.PushTokens;

namespace SaigonWaterbus.Application.PushTokens.Validators;

public sealed class RegisterPushTokenCommandValidator : AbstractValidator<RegisterPushTokenCommand>
{
    private const string ExpoTokenPrefix = "ExponentPushToken[";

    public RegisterPushTokenCommandValidator()
    {
        RuleFor(x => x.ExpoPushToken)
            .NotEmpty()
            .MaximumLength(255)
            .Must(BeValidExpoToken)
            .WithMessage("expoPushToken phải có định dạng 'ExponentPushToken[...]'.");

        RuleFor(x => x.Platform)
            .IsInEnum()
            .WithMessage("platform phải là 'Ios' hoặc 'Android'.");

        RuleFor(x => x.DeviceId).MaximumLength(255);
        RuleFor(x => x.AppVersion).MaximumLength(50);
    }

    private static bool BeValidExpoToken(string token) =>
        !string.IsNullOrWhiteSpace(token)
            && token.StartsWith(ExpoTokenPrefix, StringComparison.Ordinal)
            && token.EndsWith("]", StringComparison.Ordinal);
}

public sealed class UnregisterPushTokenCommandValidator : AbstractValidator<UnregisterPushTokenCommand>
{
    public UnregisterPushTokenCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
