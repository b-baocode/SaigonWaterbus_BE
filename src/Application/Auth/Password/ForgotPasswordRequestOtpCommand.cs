using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Password;

public sealed record ForgotPasswordRequestOtpCommand(string? EmailOrPhone = null) : IRequest<OtpChallengeDto>;

public sealed class ForgotPasswordRequestOtpCommandValidator : AbstractValidator<ForgotPasswordRequestOtpCommand>
{
    private static readonly System.ComponentModel.DataAnnotations.EmailAddressAttribute EmailAddressValidator = new();

    public ForgotPasswordRequestOtpCommandValidator()
    {
        RuleFor(x => x.EmailOrPhone)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Email hoặc số điện thoại là bắt buộc.")
            .MaximumLength(255)
            .WithMessage("Email hoặc số điện thoại không được vượt quá 255 ký tự.")
            .Must(IsValidEmailOrPhone)
            .WithMessage("Vui lòng nhập email đúng định dạng hoặc số điện thoại hợp lệ.");
    }

    private static bool IsValidEmailOrPhone(string? emailOrPhone)
    {
        if (string.IsNullOrWhiteSpace(emailOrPhone))
        {
            return false;
        }

        var trimmedEmailOrPhone = emailOrPhone.Trim();
        return IsEmailInput(trimmedEmailOrPhone)
            ? EmailAddressValidator.IsValid(trimmedEmailOrPhone)
            : PhoneRules.IsValid(trimmedEmailOrPhone);
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
        var otpChannel = IsEmailInput(emailOrPhone) ? OtpChannel.Email : OtpChannel.Phone;
        var normalizedPhone = otpChannel == OtpChannel.Phone
            ? _identityNormalizer.NormalizePhone(emailOrPhone)
            : null;
        var normalizedEmail = otpChannel == OtpChannel.Email
            ? _identityNormalizer.NormalizeEmail(emailOrPhone)
            : null;
        var lookupProperty = nameof(request.EmailOrPhone);

        var challengeResult = await _context.ExecuteInTransactionAsync(async ct =>
        {
            var usersQuery = _context.Set<User>().AsQueryable();
            if (normalizedPhone is not null)
            {
                usersQuery = usersQuery.Where(x => x.NormalizedPhoneNumber == normalizedPhone);
            }
            else
            {
                usersQuery = usersQuery.Where(x => x.NormalizedEmail == normalizedEmail);
            }

            var user = await usersQuery.SingleOrDefaultAsync(ct);

            if (user is null)
            {
                throw AuthSupport.CreateValidationException(lookupProperty, "Tài khoản chưa được đăng ký.");
            }

            AuthSupport.EnsureUserCanLogin(user, lookupProperty);

            if (otpChannel == OtpChannel.Phone && string.IsNullOrWhiteSpace(user.NormalizedPhoneNumber))
            {
                throw AuthSupport.CreateValidationException(lookupProperty, "Tài khoản này chưa có số điện thoại.");
            }

            if (otpChannel == OtpChannel.Email && string.IsNullOrWhiteSpace(user.Email))
            {
                throw AuthSupport.CreateValidationException(lookupProperty, "Tài khoản này chưa có email.");
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
            var destination = otpChannel == OtpChannel.Phone
                ? user.NormalizedPhoneNumber!
                : user.Email!.Trim();
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

        if (challengeResult.Channel == OtpChannel.Phone)
        {
            await _smsOtpSender.SendAsync(
                challengeResult.Destination,
                challengeResult.Code,
                OtpPurpose.ForgotPassword,
                challengeResult.FullName,
                cancellationToken);
        }
        else
        {
            await _otpSender.SendAsync(
                challengeResult.Destination,
                challengeResult.Code,
                OtpPurpose.ForgotPassword,
                challengeResult.FullName,
                cancellationToken);
        }

        return new OtpChallengeDto(
            challengeResult.Id,
            challengeResult.Channel == OtpChannel.Phone
                ? _otpCodeService.MaskPhone(challengeResult.Destination)
                : _otpCodeService.MaskEmail(challengeResult.Destination),
            challengeResult.ExpiresAt,
            challengeResult.ResendAvailableAt)
        {
            Channel = challengeResult.Channel
        };
    }

    private static bool IsEmailInput(string emailOrPhone) =>
        emailOrPhone.Contains('@', StringComparison.Ordinal);
}
