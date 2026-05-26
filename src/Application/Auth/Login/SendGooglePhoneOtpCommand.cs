using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Login;

public sealed record SendGooglePhoneOtpCommand(string TempToken, string Phone) : IRequest<GooglePhoneOtpSentDto>;

public sealed class SendGooglePhoneOtpCommandValidator : AbstractValidator<SendGooglePhoneOtpCommand>
{
    public SendGooglePhoneOtpCommandValidator()
    {
        RuleFor(x => x.TempToken)
            .NotEmpty()
            .WithMessage("Temp token là bắt buộc.");

        RuleFor(x => x.Phone)
            .NotEmpty()
            .WithMessage("Số điện thoại là bắt buộc.")
            .Must(PhoneRules.IsValid)
            .WithMessage(PhoneRules.InvalidInternationalPhoneMessage);
    }
}

public sealed class SendGooglePhoneOtpCommandHandler : IRequestHandler<SendGooglePhoneOtpCommand, GooglePhoneOtpSentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IGoogleLoginTempStore _tempStore;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly ISecretHasher _secretHasher;
    private readonly IOtpCodeService _otpCodeService;
    private readonly ISmsOtpSender _smsOtpSender;
    private readonly IOtpPolicy _otpPolicy;
    private readonly TimeProvider _timeProvider;

    public SendGooglePhoneOtpCommandHandler(
        IApplicationDbContext context,
        IGoogleLoginTempStore tempStore,
        IIdentityNormalizer identityNormalizer,
        ISecretHasher secretHasher,
        IOtpCodeService otpCodeService,
        ISmsOtpSender smsOtpSender,
        IOtpPolicy otpPolicy,
        TimeProvider timeProvider)
    {
        _context = context;
        _tempStore = tempStore;
        _identityNormalizer = identityNormalizer;
        _secretHasher = secretHasher;
        _otpCodeService = otpCodeService;
        _smsOtpSender = smsOtpSender;
        _otpPolicy = otpPolicy;
        _timeProvider = timeProvider;
    }

    public async Task<GooglePhoneOtpSentDto> Handle(SendGooglePhoneOtpCommand request, CancellationToken cancellationToken)
    {
        var session = await _tempStore.GetAsync(request.TempToken, cancellationToken)
            ?? throw AuthSupport.CreateValidationException(nameof(request.TempToken), "Temp token không hợp lệ hoặc đã hết hạn.");

        var normalizedPhone = _identityNormalizer.NormalizePhone(request.Phone);
        var phoneNumber = PhoneRules.ToInternationalFormat(request.Phone);

        if (await IsPhoneUsedByAnotherUserAsync(normalizedPhone, session.ExistingUserId, cancellationToken))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Phone), "Số điện thoại đã được đăng ký.");
        }

        var now = _timeProvider.GetUtcNow();
        if (session.NormalizedPhoneNumber == normalizedPhone
            && session.OtpResendAvailableAt is not null
            && session.OtpResendAvailableAt > now)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Phone), "OTP vừa được gửi, vui lòng chờ trước khi gửi lại.");
        }

        var otpCode = _otpCodeService.GenerateCode();
        var updatedSession = session with
        {
            PhoneNumber = phoneNumber,
            NormalizedPhoneNumber = normalizedPhone,
            OtpCodeHash = _secretHasher.Hash(otpCode),
            OtpExpiresAt = now.AddMinutes(_otpPolicy.ExpirationMinutes),
            OtpResendAvailableAt = now.AddSeconds(_otpPolicy.ResendSeconds),
            OtpAttemptCount = 0,
            OtpMaxAttempts = _otpPolicy.MaxAttempts
        };

        await _tempStore.SaveAsync(updatedSession, cancellationToken);
        await _smsOtpSender.SendAsync(
            normalizedPhone,
            otpCode,
            OtpPurpose.Register,
            session.Name,
            cancellationToken);

        return new GooglePhoneOtpSentDto(
            "OTP_SENT",
            _otpCodeService.MaskPhone(normalizedPhone),
            updatedSession.OtpExpiresAt.Value,
            updatedSession.OtpResendAvailableAt.Value);
    }

    private async Task<bool> IsPhoneUsedByAnotherUserAsync(
        string normalizedPhone,
        int? existingUserId,
        CancellationToken cancellationToken)
    {
        var query = _context.Set<User>().Where(x => x.NormalizedPhoneNumber == normalizedPhone);
        if (existingUserId.HasValue)
        {
            query = query.Where(x => x.Id != existingUserId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }
}
