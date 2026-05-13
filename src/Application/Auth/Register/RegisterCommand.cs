using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Register;

public sealed record RegisterCommand(
    string FullName,
    DateOnly DateOfBirth,
    string Phone,
    string Password,
    string? Email = null,
    string? OtpChannel = null) : IRequest<OtpChallengeDto>;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty()
            .WithMessage("Họ và tên không được để trống.")
            .MaximumLength(150)
            .WithMessage("Họ và tên không được vượt quá 150 ký tự.");

        RuleFor(x => x.DateOfBirth)
            .Must(x => x <= DateOnly.FromDateTime(DateTime.UtcNow.Date))
            .WithMessage("Ngày sinh không được lớn hơn ngày hiện tại.");

        RuleFor(x => x.Phone)
            .NotEmpty()
            .WithMessage("Số điện thoại là bắt buộc.")
            .Must(PhoneRules.IsValid)
            .WithMessage(PhoneRules.InvalidInternationalPhoneMessage);

        RuleFor(x => x.Email)
            .Cascade(CascadeMode.Stop)
            .MaximumLength(255)
            .WithMessage("Email không được vượt quá 255 ký tự.")
            .EmailAddress()
            .WithMessage("Email không đúng định dạng.")
            .Must(EmailRules.HasAllowedRegistrationDomain)
            .WithMessage(EmailRules.AllowedEmailDomainMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Email)
            .NotEmpty()
            .When(x => PhoneRules.IsValid(x.Phone) && !PhoneRules.IsVietnamPhone(x.Phone))
            .WithMessage("Số điện thoại quốc tế bắt buộc nhập email được hỗ trợ để nhận OTP.");

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("Mật khẩu là bắt buộc.")
            .Must(PasswordRules.IsStrong)
            .WithMessage(PasswordRules.StrongPasswordMessage);

        RuleFor(x => x.OtpChannel)
            .Must(x => string.IsNullOrWhiteSpace(x)
                || string.Equals(x, "email", StringComparison.OrdinalIgnoreCase)
                || string.Equals(x, "mail", StringComparison.OrdinalIgnoreCase)
                || string.Equals(x, "e-mail", StringComparison.OrdinalIgnoreCase)
                || string.Equals(x, "phone", StringComparison.OrdinalIgnoreCase)
                || string.Equals(x, "sms", StringComparison.OrdinalIgnoreCase)
                || string.Equals(x, "sdt", StringComparison.OrdinalIgnoreCase)
                || string.Equals(x, "so-dien-thoai", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Kênh OTP chỉ được là email hoặc phone.");

        RuleFor(x => x.OtpChannel)
            .Must((command, otpChannel) =>
                !PhoneRules.IsValid(command.Phone)
                || PhoneRules.IsVietnamPhone(command.Phone)
                || string.IsNullOrWhiteSpace(otpChannel)
                || IsEmailOtpChannel(otpChannel))
            .WithMessage("Số điện thoại quốc tế chỉ hỗ trợ OTP qua email.");

        RuleFor(x => x.OtpChannel)
            .Must((command, otpChannel) =>
                !IsEmailOtpChannel(otpChannel)
                || !string.IsNullOrWhiteSpace(command.Email))
            .WithMessage("Email là bắt buộc khi chọn nhận OTP qua email.");
    }

    private static bool IsEmailOtpChannel(string? otpChannel) =>
        !string.IsNullOrWhiteSpace(otpChannel)
        && otpChannel.Trim().ToLowerInvariant() is "email" or "mail" or "e-mail";
}

public sealed class RegisterCommandHandler : IRequestHandler<RegisterCommand, OtpChallengeDto>
{
    private sealed record PendingRegistrationOtp(
        int ChallengeId,
        string Destination,
        OtpChannel Channel,
        string FullName,
        string Code,
        DateTimeOffset ExpiresAt,
        DateTimeOffset ResendAvailableAt);

    private readonly IApplicationDbContext _context;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly ISecretHasher _secretHasher;
    private readonly IOtpCodeService _otpCodeService;
    private readonly IOtpSender _otpSender;
    private readonly ISmsOtpSender _smsOtpSender;
    private readonly IOtpPolicy _otpPolicy;
    private readonly TimeProvider _timeProvider;

    public RegisterCommandHandler(
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

    public async Task<OtpChallengeDto> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var normalizedPhone = _identityNormalizer.NormalizePhone(request.Phone);
        var isVietnamPhone = PhoneRules.IsVietnamPhone(request.Phone);
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        var normalizedEmail = email is null ? null : _identityNormalizer.NormalizeEmail(email);
        var otpChannel = ResolveRegisterOtpChannel(request.OtpChannel, email, isVietnamPhone);
        var now = _timeProvider.GetUtcNow();

        var pendingRegistration = await _context.ExecuteInTransactionAsync(async ct =>
        {
            if (await AuthSupport.RemoveExpiredPendingRegistrationUsersByIdentityAsync(
                    _context,
                    normalizedPhone,
                    normalizedEmail,
                    now,
                    ct))
            {
                await _context.SaveChangesAsync(ct);
            }

            var customerRole = await AuthSupport.GetRoleByCodeAsync(
                _context,
                Domain.Constants.Roles.CustomerCode,
                ct);

            var matchingUsers = await _context.Set<User>()
                .Include(x => x.OtpChallenges)
                .Where(x => x.NormalizedPhoneNumber == normalizedPhone
                         || (normalizedEmail != null && x.NormalizedEmail == normalizedEmail))
                .ToListAsync(ct);

            var pendingUser = matchingUsers.SingleOrDefault(x =>
                x.Status == UserStatus.PendingVerification
                && x.NormalizedPhoneNumber == normalizedPhone
                && x.NormalizedEmail == normalizedEmail);

            foreach (var matchingUser in matchingUsers)
            {
                if (pendingUser is not null && matchingUser.Id == pendingUser.Id)
                {
                    continue;
                }

                if (matchingUser.NormalizedPhoneNumber == normalizedPhone)
                {
                    var message = matchingUser.Status == UserStatus.PendingVerification
                        ? "Số điện thoại đang chờ xác thực OTP. Vui lòng xác thực OTP hoặc chờ OTP hết hạn để đăng ký lại."
                        : "Số điện thoại đã được đăng ký.";
                    throw AuthSupport.CreateValidationException(nameof(request.Phone), message);
                }

                if (normalizedEmail is not null && matchingUser.NormalizedEmail == normalizedEmail)
                {
                    var message = matchingUser.Status == UserStatus.PendingVerification
                        ? "Email đang chờ xác thực OTP. Vui lòng xác thực OTP hoặc chờ OTP hết hạn để đăng ký lại."
                        : "Email đã được đăng ký.";
                    throw AuthSupport.CreateValidationException(nameof(request.Email), message);
                }
            }

            if (pendingUser is not null)
            {
                var latestPendingChallenge = pendingUser.OtpChallenges
                    .Where(x => x.Purpose == OtpPurpose.Register && x.ConsumedAt == null)
                    .OrderByDescending(x => x.Id)
                    .FirstOrDefault();

                if (latestPendingChallenge is not null && latestPendingChallenge.ResendAvailableAt > now)
                {
                    throw AuthSupport.CreateValidationException(nameof(request.Phone), "OTP vừa được gửi, vui lòng chờ trước khi gửi lại.");
                }
            }

            var otpCode = _otpCodeService.GenerateCode();

            var user = pendingUser ?? new User
            {
                Status = UserStatus.PendingVerification
            };

            user.FullName = request.FullName.Trim();
            user.DateOfBirth = request.DateOfBirth;
            user.PhoneNumber = PhoneRules.ToInternationalFormat(request.Phone);
            user.NormalizedPhoneNumber = normalizedPhone;
            user.Email = email;
            user.NormalizedEmail = normalizedEmail;
            user.PasswordHash = _secretHasher.Hash(request.Password);
            user.RoleId = customerRole.Id;
            user.Status = UserStatus.PendingVerification;

            if (pendingUser is null)
            {
                _context.Set<User>().Add(user);
            }
            else
            {
                await AuthSupport.RetirePendingOtpChallengesAsync(_context, user.Id, OtpPurpose.Register, now, ct);
            }

            var otpDestination = otpChannel == OtpChannel.Email
                ? email!
                : normalizedPhone;

            var challenge = new OtpChallenge
            {
                User = user,
                Purpose = OtpPurpose.Register,
                Email = otpDestination,
                CodeHash = _secretHasher.Hash(otpCode),
                ExpiresAt = now.AddMinutes(_otpPolicy.ExpirationMinutes),
                ResendAvailableAt = now.AddSeconds(_otpPolicy.ResendSeconds),
                MaxAttempts = _otpPolicy.MaxAttempts
            };

            _context.Set<OtpChallenge>().Add(challenge);

            await _context.SaveChangesAsync(ct);
            return new PendingRegistrationOtp(
                challenge.Id,
                otpDestination,
                otpChannel,
                user.FullName,
                otpCode,
                challenge.ExpiresAt,
                challenge.ResendAvailableAt);
        }, cancellationToken);

        if (pendingRegistration.Channel == OtpChannel.Email)
        {
            await _otpSender.SendAsync(
                pendingRegistration.Destination,
                pendingRegistration.Code,
                OtpPurpose.Register,
                pendingRegistration.FullName,
                cancellationToken);
        }
        else
        {
            await _smsOtpSender.SendAsync(
                pendingRegistration.Destination,
                pendingRegistration.Code,
                OtpPurpose.Register,
                pendingRegistration.FullName,
                cancellationToken);
        }

        return new OtpChallengeDto(
            pendingRegistration.ChallengeId,
            pendingRegistration.Channel == OtpChannel.Email
                ? _otpCodeService.MaskEmail(pendingRegistration.Destination)
                : _otpCodeService.MaskPhone(pendingRegistration.Destination),
            pendingRegistration.ExpiresAt,
            pendingRegistration.ResendAvailableAt)
        {
            Channel = pendingRegistration.Channel
        };
    }

    private static OtpChannel ResolveRegisterOtpChannel(string? otpChannel, string? email, bool isVietnamPhone)
    {
        var hasEmail = !string.IsNullOrWhiteSpace(email);
        if (!isVietnamPhone)
        {
            return OtpChannel.Email;
        }

        if (string.IsNullOrWhiteSpace(otpChannel))
        {
            return hasEmail ? OtpChannel.Email : OtpChannel.Phone;
        }

        var resolvedChannel = AuthSupport.ResolveOtpChannel(otpChannel, OtpChannel.Email, nameof(RegisterCommand.OtpChannel));
        if (resolvedChannel == OtpChannel.Email && !hasEmail)
        {
            throw AuthSupport.CreateValidationException(nameof(RegisterCommand.OtpChannel), "Email là bắt buộc khi chọn nhận OTP qua email.");
        }

        return resolvedChannel;
    }

}
