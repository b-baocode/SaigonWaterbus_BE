using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Register;

public sealed record RegisterRequest(
    string FullName,
    DateOnly DateOfBirth,
    string Password,
    string? Phone = null,
    string? Email = null,
    string? OtpChannel = null,
    string? Gender = null,
    string? Nationality = null);

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty()
            .WithMessage("Họ và tên không được để trống.")
            .MaximumLength(150)
            .WithMessage("Họ và tên không được vượt quá 150 ký tự.");

        RuleFor(x => x.DateOfBirth)
            .Must(x => x != default)
            .WithMessage("Ngày sinh là bắt buộc.")
            .Must(x => x <= DateOnly.FromDateTime(DateTime.UtcNow.Date))
            .WithMessage("Ngày sinh không được lớn hơn ngày hiện tại.");

        RuleFor(x => x.Gender)
            .MaximumLength(30)
            .WithMessage("Giới tính không được vượt quá 30 ký tự.");

        RuleFor(x => x.Nationality)
            .MaximumLength(100)
            .WithMessage("Quốc tịch không được vượt quá 100 ký tự.");

        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Phone) || !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Email hoặc số điện thoại là bắt buộc.")
            .OverridePropertyName(nameof(RegisterRequest.Email));

        RuleFor(x => x.Phone)
            .Must(phone => string.IsNullOrWhiteSpace(phone) || PhoneRules.IsValid(phone))
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

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("Mật khẩu là bắt buộc.")
            .Must(PasswordRules.IsStrong)
            .WithMessage(PasswordRules.StrongPasswordMessage);

        RuleFor(x => x.OtpChannel)
            .Must(x => string.IsNullOrWhiteSpace(x)
                || string.Equals(x, "email", StringComparison.OrdinalIgnoreCase)
                || string.Equals(x, "phone", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Kênh OTP chỉ được là email hoặc phone.");

        RuleFor(x => x.OtpChannel)
            .Must(x => !string.IsNullOrWhiteSpace(x))
            .When(x => !string.IsNullOrWhiteSpace(x.Phone) && !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Vui lòng chọn kênh nhận OTP trước khi đăng ký.");

        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Phone))
            .When(x => IsPhoneOtpChannel(x.OtpChannel))
            .WithMessage("Số điện thoại là bắt buộc khi chọn nhận OTP qua phone.")
            .OverridePropertyName(nameof(RegisterRequest.Phone));
    }

    private static bool IsPhoneOtpChannel(string? otpChannel) =>
        string.Equals(otpChannel, "phone", StringComparison.OrdinalIgnoreCase);
}

public sealed class RegisterRequestUseCase
{
    private sealed record PendingRegistrationOtp(
        Guid ChallengeId,
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
    private readonly IOtpCache _otpCache;
    private readonly TimeProvider _timeProvider;

    public RegisterRequestUseCase(
        IApplicationDbContext context,
        IIdentityNormalizer identityNormalizer,
        ISecretHasher secretHasher,
        IOtpCodeService otpCodeService,
        IOtpSender otpSender,
        ISmsOtpSender smsOtpSender,
        IOtpPolicy otpPolicy,
        TimeProvider timeProvider,
        IOtpCache? otpCache = null)
    {
        _context = context;
        _identityNormalizer = identityNormalizer;
        _secretHasher = secretHasher;
        _otpCodeService = otpCodeService;
        _otpSender = otpSender;
        _smsOtpSender = smsOtpSender;
        _otpPolicy = otpPolicy;
        _otpCache = otpCache ?? NullOtpCache.Instance;
        _timeProvider = timeProvider;
    }

    public async Task<OtpChallengeDto> ExecuteAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var hasPhone = !string.IsNullOrWhiteSpace(request.Phone);
        var normalizedPhone = hasPhone ? _identityNormalizer.NormalizePhone(request.Phone!) : null;
        var hasEmail = !string.IsNullOrWhiteSpace(request.Email);
        var email = hasEmail ? request.Email!.Trim() : null;
        var normalizedEmail = hasEmail ? _identityNormalizer.NormalizeEmail(email!) : null;
        var otpChannel = ResolveRegisterOtpChannel(request.OtpChannel, hasPhone, hasEmail);
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

            var matchingUsers = await AuthSupport.WhereUserIdentityMatches(
                    _context.Set<User>().Include(x => x.OtpChallenges),
                    normalizedPhone,
                    normalizedEmail)
                .ToListAsync(ct);

            var pendingUser = matchingUsers.SingleOrDefault(x =>
                x.Status == UserStatus.PendingVerification
                && RegistrationIdentityMatches(x, normalizedPhone, normalizedEmail));

            foreach (var matchingUser in matchingUsers)
            {
                if (pendingUser is not null && matchingUser.Id == pendingUser.Id)
                {
                    continue;
                }

                if (normalizedPhone is not null && AuthSupport.UserPhoneMatches(matchingUser, normalizedPhone))
                {
                    throw AuthSupport.CreateValidationException(nameof(request.Phone), "Số điện thoại đã được đăng ký.");
                }

                if (normalizedEmail is not null && AuthSupport.UserEmailMatches(matchingUser, normalizedEmail))
                {
                    throw AuthSupport.CreateValidationException(nameof(request.Email), "Email đã được đăng ký.");
                }
            }

            if (pendingUser is not null)
            {
                var latestPendingChallenge = pendingUser.OtpChallenges
                    .Where(x => x.Purpose == OtpPurpose.Register && x.ConsumedAt == null)
                    .OrderByDescending(x => x.Created)
                    .ThenByDescending(x => x.Id)
                    .FirstOrDefault();

                if (latestPendingChallenge is not null && latestPendingChallenge.ResendAvailableAt > now)
                {
                    throw AuthSupport.CreateValidationException(GetRegisterIdentifierProperty(normalizedPhone), "OTP vừa được gửi, vui lòng chờ trước khi gửi lại.");
                }
            }

            var otpCode = _otpCodeService.GenerateCode();

            var user = pendingUser ?? new User
            {
                Status = UserStatus.PendingVerification
            };

            user.FullName = request.FullName.Trim();
            user.DateOfBirth = request.DateOfBirth;
            user.Gender = AuthSupport.NormalizeOptionalText(request.Gender);
            user.Nationality = AuthSupport.NormalizeOptionalText(request.Nationality);
            user.PhoneNumber = hasPhone ? PhoneRules.ToInternationalFormat(request.Phone!) : null;
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

            var otpDestination = otpChannel == OtpChannel.Phone
                ? normalizedPhone!
                : email!;

            var challenge = new OtpChallenge
            {
                User = user,
                Purpose = OtpPurpose.Register,
                Channel = otpChannel,
                Email = otpDestination,
                CodeHash = _secretHasher.Hash(otpCode),
                ExpiresAt = now.AddMinutes(_otpPolicy.ExpirationMinutes),
                ResendAvailableAt = now.AddSeconds(_otpPolicy.ResendSeconds),
                MaxAttempts = _otpPolicy.MaxAttempts
            };

            _context.Set<OtpChallenge>().Add(challenge);

            await _context.SaveChangesAsync(ct);
            await _otpCache.StoreAsync(challenge, challenge.CodeHash, ct);
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

    private static OtpChannel ResolveRegisterOtpChannel(string? otpChannel, bool hasPhone, bool hasEmail)
    {
        if (string.IsNullOrWhiteSpace(otpChannel))
        {
            if (hasPhone && hasEmail)
            {
                throw AuthSupport.CreateValidationException(
                    nameof(RegisterRequest.OtpChannel),
                    "Vui lòng chọn kênh nhận OTP trước khi gửi.");
            }

            return hasPhone ? OtpChannel.Phone : OtpChannel.Email;
        }

        var resolvedChannel = AuthSupport.ResolveOtpChannel(otpChannel, hasPhone ? OtpChannel.Phone : OtpChannel.Email, nameof(RegisterRequest.OtpChannel));

        if (resolvedChannel == OtpChannel.Email && !hasEmail)
        {
            throw AuthSupport.CreateValidationException(nameof(RegisterRequest.OtpChannel), "Email is required when OtpChannel is 'email'.");
        }

        if (resolvedChannel == OtpChannel.Phone && !hasPhone)
        {
            throw AuthSupport.CreateValidationException(nameof(RegisterRequest.OtpChannel), "Phone is required when OtpChannel is 'phone'.");
        }

        return resolvedChannel;
    }

    private static bool RegistrationIdentityMatches(User user, string? normalizedPhone, string? normalizedEmail) =>
        AuthSupport.UserIdentityMatches(user, normalizedPhone, normalizedEmail);

    private static string GetRegisterIdentifierProperty(string? normalizedPhone) =>
        normalizedPhone is null ? nameof(RegisterRequest.Email) : nameof(RegisterRequest.Phone);
}
