using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Password;

public sealed record ForgotPasswordRequestOtpCommand(string? EmailOrPhone = null) : IRequest<OtpChallengeDto>;

public sealed class ForgotPasswordRequestOtpCommandValidator : AbstractValidator<ForgotPasswordRequestOtpCommand>
{
    public ForgotPasswordRequestOtpCommandValidator()
    {
        RuleFor(x => x.EmailOrPhone)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Email hoặc số điện thoại là bắt buộc.")
            .MaximumLength(255)
            .WithMessage("Email hoặc số điện thoại không được vượt quá 255 ký tự.")
            .Must(IsValidEmailOrPhone)
            .WithMessage("Vui lòng nhập email được hỗ trợ đúng định dạng hoặc số điện thoại hợp lệ.")
            .Must(HasAllowedEmailDomainOrPhone)
            .WithMessage(EmailRules.AllowedEmailDomainMessage);
    }

    private static bool IsValidEmailOrPhone(string? emailOrPhone)
    {
        if (string.IsNullOrWhiteSpace(emailOrPhone))
        {
            return false;
        }

        var trimmedEmailOrPhone = emailOrPhone.Trim();
        return IsEmailInput(trimmedEmailOrPhone)
            ? new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(trimmedEmailOrPhone)
            : PhoneRules.IsValid(trimmedEmailOrPhone);
    }

    private static bool HasAllowedEmailDomainOrPhone(string? emailOrPhone)
    {
        if (string.IsNullOrWhiteSpace(emailOrPhone))
        {
            return false;
        }

        var trimmedEmailOrPhone = emailOrPhone.Trim();
        return !IsEmailInput(trimmedEmailOrPhone)
            || EmailRules.HasAllowedRegistrationDomain(trimmedEmailOrPhone);
    }

    private static bool IsEmailInput(string emailOrPhone) =>
        emailOrPhone.Contains('@', StringComparison.Ordinal);
}

public sealed class ForgotPasswordRequestOtpCommandHandler : IRequestHandler<ForgotPasswordRequestOtpCommand, OtpChallengeDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly ISecretHasher _secretHasher;
    private readonly IOtpCodeService _otpCodeService;
    private readonly IOtpSender _otpSender;
    private readonly ISmsOtpSender _smsOtpSender;
    private readonly IOtpPolicy _otpPolicy;
    private readonly TimeProvider _timeProvider;

    public ForgotPasswordRequestOtpCommandHandler(
        IApplicationDbContext context,
        IIdentityNormalizer identityNormalizer,
        ISecretHasher secretHasher,
        IOtpCodeService otpCodeService,
        IOtpSender otpSender,
        ISmsOtpSender smsOtpSender,
        IOtpPolicy otpPolicy,
        TimeProvider timeProvider)
    {
        _context = context;
        _identityNormalizer = identityNormalizer;
        _secretHasher = secretHasher;
        _otpCodeService = otpCodeService;
        _otpSender = otpSender;
        _smsOtpSender = smsOtpSender;
        _otpPolicy = otpPolicy;
        _timeProvider = timeProvider;
    }

    public async Task<OtpChallengeDto> Handle(ForgotPasswordRequestOtpCommand request, CancellationToken cancellationToken)
    {
        var emailOrPhone = request.EmailOrPhone!.Trim();
        var isEmailInput = emailOrPhone.Contains('@', StringComparison.Ordinal);
        var normalizedEmail = isEmailInput ? _identityNormalizer.NormalizeEmail(emailOrPhone) : null;
        var normalizedPhone = isEmailInput ? null : _identityNormalizer.NormalizePhone(emailOrPhone);
        var lookupProperty = nameof(request.EmailOrPhone);

        var challengeResult = await _context.ExecuteInTransactionAsync(async ct =>
        {
            var user = isEmailInput
                ? await _context.Set<User>().SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, ct)
                : await _context.Set<User>().SingleOrDefaultAsync(x => x.NormalizedPhoneNumber == normalizedPhone, ct);

            if (user is null)
            {
                throw AuthSupport.CreateValidationException(lookupProperty, "Tài khoản chưa được đăng ký.");
            }

            AuthSupport.EnsureUserCanLogin(user, lookupProperty);

            var otpChannel = ResolveForgotPasswordOtpChannel(emailOrPhone);
            if (otpChannel == OtpChannel.Email && !EmailRules.HasAllowedRegistrationDomain(user.Email))
            {
                throw AuthSupport.CreateValidationException(lookupProperty, "Số điện thoại quốc tế bắt buộc tài khoản có email được hỗ trợ để nhận OTP.");
            }

            var now = _timeProvider.GetUtcNow();
            var existingActiveChallenge = await _context.Set<OtpChallenge>()
                .Where(x => x.UserId == user.Id
                         && x.Purpose == OtpPurpose.ForgotPassword
                         && x.ConsumedAt == null
                         && x.ExpiresAt > now)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            if (existingActiveChallenge is not null && existingActiveChallenge.ResendAvailableAt > now)
            {
                throw AuthSupport.CreateValidationException(lookupProperty, "OTP vừa được gửi, vui lòng chờ trước khi gửi lại.");
            }

            await AuthSupport.RetirePendingOtpChallengesAsync(_context, user.Id, OtpPurpose.ForgotPassword, now, ct);

            var otpCode = _otpCodeService.GenerateCode();
            var destination = otpChannel == OtpChannel.Email
                ? user.Email!.Trim()
                : user.NormalizedPhoneNumber!;
            var challenge = new OtpChallenge
            {
                UserId = user.Id,
                Purpose = OtpPurpose.ForgotPassword,
                Email = destination,
                CodeHash = _secretHasher.Hash(otpCode),
                ExpiresAt = now.AddMinutes(_otpPolicy.ExpirationMinutes),
                ResendAvailableAt = now.AddSeconds(_otpPolicy.ResendSeconds),
                MaxAttempts = _otpPolicy.MaxAttempts
            };

            _context.Set<OtpChallenge>().Add(challenge);
            await _context.SaveChangesAsync(ct);

            return (
                Id: challenge.Id,
                Destination: destination,
                Channel: otpChannel,
                FullName: user.FullName,
                Code: otpCode,
                ExpiresAt: challenge.ExpiresAt,
                ResendAvailableAt: challenge.ResendAvailableAt);
        }, cancellationToken);

        if (challengeResult.Channel == OtpChannel.Email)
        {
            await _otpSender.SendAsync(
                challengeResult.Destination,
                challengeResult.Code,
                OtpPurpose.ForgotPassword,
                challengeResult.FullName,
                cancellationToken);
        }
        else
        {
            await _smsOtpSender.SendAsync(
                challengeResult.Destination,
                challengeResult.Code,
                OtpPurpose.ForgotPassword,
                challengeResult.FullName,
                cancellationToken);
        }

        return new OtpChallengeDto(
            challengeResult.Id,
            challengeResult.Channel == OtpChannel.Email
                ? _otpCodeService.MaskEmail(challengeResult.Destination)
                : _otpCodeService.MaskPhone(challengeResult.Destination),
            challengeResult.ExpiresAt,
            challengeResult.ResendAvailableAt)
        {
            Channel = challengeResult.Channel
        };
    }

    private static OtpChannel ResolveForgotPasswordOtpChannel(string emailOrPhone)
    {
        if (emailOrPhone.Contains('@', StringComparison.Ordinal))
        {
            return OtpChannel.Email;
        }

        return PhoneRules.IsVietnamPhone(emailOrPhone)
            ? OtpChannel.Phone
            : OtpChannel.Email;
    }
}
