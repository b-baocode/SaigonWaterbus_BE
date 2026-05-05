using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Profile;

public sealed record UpdateCurrentUserProfileCommand(
    string? FullName = null,
    DateOnly? DateOfBirth = null,
    string? PhoneNumber = null,
    string? Email = null,
    string? AvatarFileName = null,
    string? AvatarContentType = null,
    long? AvatarLength = null,
    Stream? AvatarContent = null) : IRequest<UpdateProfileResultDto>;

public sealed class UpdateCurrentUserProfileCommandValidator : AbstractValidator<UpdateCurrentUserProfileCommand>
{
    public UpdateCurrentUserProfileCommandValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty()
            .WithMessage("Họ và tên không được để trống.")
            .MaximumLength(150)
            .WithMessage("Họ và tên không được vượt quá 150 ký tự.")
            .When(x => x.FullName is not null);

        RuleFor(x => x.DateOfBirth)
            .Must(x => !x.HasValue || x.Value <= DateOnly.FromDateTime(DateTime.UtcNow.Date))
            .WithMessage("Ngày sinh không được lớn hơn ngày hiện tại.");

        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .WithMessage("Số điện thoại không được để trống.")
            .Must(PhoneRules.IsValid)
            .WithMessage(PhoneRules.InvalidInternationalPhoneMessage)
            .When(x => x.PhoneNumber is not null);

        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("Email không được để trống.")
            .MaximumLength(255)
            .WithMessage("Email không được vượt quá 255 ký tự.")
            .EmailAddress()
            .WithMessage("Email không đúng định dạng.")
            .When(x => x.Email is not null);
    }
}

public sealed class UpdateCurrentUserProfileCommandHandler : IRequestHandler<UpdateCurrentUserProfileCommand, UpdateProfileResultDto>
{
    private sealed record PendingEmailVerification(
        int ChallengeId,
        string Email,
        string Code,
        DateTimeOffset ExpiresAt,
        DateTimeOffset ResendAvailableAt);

    private readonly IApplicationDbContext _context;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly IProfileImageStorageService _profileImageStorage;
    private readonly ISecretHasher _secretHasher;
    private readonly IOtpCodeService _otpCodeService;
    private readonly IOtpSender _otpSender;
    private readonly IOtpPolicy _otpPolicy;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public UpdateCurrentUserProfileCommandHandler(
        IApplicationDbContext context,
        IIdentityNormalizer identityNormalizer,
        IProfileImageStorageService profileImageStorage,
        ISecretHasher secretHasher,
        IOtpCodeService otpCodeService,
        IOtpSender otpSender,
        IOtpPolicy otpPolicy,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _identityNormalizer = identityNormalizer;
        _profileImageStorage = profileImageStorage;
        _secretHasher = secretHasher;
        _otpCodeService = otpCodeService;
        _otpSender = otpSender;
        _otpPolicy = otpPolicy;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<UpdateProfileResultDto> Handle(UpdateCurrentUserProfileCommand request, CancellationToken cancellationToken)
    {
        var user = await AuthSupport.GetCurrentUserWithRoleAsync(_context, _userContext, cancellationToken);
        AuthSupport.EnsureUserCanLogin(user);

        var hasEmailUpdate = request.Email is not null;
        var email = hasEmailUpdate ? request.Email!.Trim() : user.Email;
        var normalizedEmail = hasEmailUpdate ? _identityNormalizer.NormalizeEmail(email!) : user.NormalizedEmail;
        var emailChanged = hasEmailUpdate && normalizedEmail != user.NormalizedEmail;
        var hasPhoneUpdate = request.PhoneNumber is not null;
        var normalizedPhone = hasPhoneUpdate ? _identityNormalizer.NormalizePhone(request.PhoneNumber!) : user.NormalizedPhoneNumber;
        var phoneChanged = hasPhoneUpdate && normalizedPhone != user.NormalizedPhoneNumber;
        var now = _timeProvider.GetUtcNow();
        var hasAvatarUpdate = request.AvatarContent is not null;

        PendingEmailVerification? pendingEmailVerification = null;
        StoredProfileImage? uploadedAvatar = null;

        if (emailChanged)
        {
            if (await _context.Set<User>().AnyAsync(x => x.NormalizedEmail == normalizedEmail && x.Id != user.Id, cancellationToken))
            {
                throw AuthSupport.CreateValidationException(nameof(request.Email), "Email đã được đăng ký.");
            }

            var latestPendingChallenge = await _context.Set<OtpChallenge>()
                .Where(x => x.UserId == user.Id
                         && x.Purpose == OtpPurpose.EmailChange
                         && x.ConsumedAt == null)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (latestPendingChallenge is not null
                && _identityNormalizer.NormalizeEmail(latestPendingChallenge.Email) == normalizedEmail
                && latestPendingChallenge.ResendAvailableAt > now)
            {
                throw AuthSupport.CreateValidationException(nameof(request.Email), "OTP vừa được gửi, vui lòng chờ trước khi gửi lại.");
            }
        }

        if (phoneChanged)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.PhoneNumber),
                "Khách hàng không được tự thay đổi số điện thoại. Vui lòng liên hệ Admin hoặc Manager.");
        }

        if (hasAvatarUpdate)
        {
            EnsureValidAvatar(request);
            uploadedAvatar = await _profileImageStorage.UploadAvatarAsync(
                new ProfileImageUpload(
                    user.Id,
                    request.AvatarContent!,
                    request.AvatarFileName!,
                    request.AvatarContentType),
                cancellationToken);
        }

        await _context.ExecuteInTransactionAsync(async ct =>
        {
            OtpChallenge? challenge = null;
            string? otpCode = null;

            if (emailChanged)
            {
                await AuthSupport.RetirePendingOtpChallengesAsync(_context, user.Id, OtpPurpose.EmailChange, now, ct);

                otpCode = _otpCodeService.GenerateCode();
                challenge = new OtpChallenge
                {
                    UserId = user.Id,
                    Purpose = OtpPurpose.EmailChange,
                    Email = email!,
                    CodeHash = _secretHasher.Hash(otpCode),
                    ExpiresAt = now.AddMinutes(_otpPolicy.ExpirationMinutes),
                    ResendAvailableAt = now.AddSeconds(_otpPolicy.ResendSeconds),
                    MaxAttempts = _otpPolicy.MaxAttempts
                };

                _context.Set<OtpChallenge>().Add(challenge);
            }
            else if (hasEmailUpdate)
            {
                user.Email = email;
                user.NormalizedEmail = normalizedEmail;
            }

            if (request.FullName is not null)
            {
                user.FullName = request.FullName.Trim();
            }

            if (request.DateOfBirth.HasValue)
            {
                user.DateOfBirth = request.DateOfBirth;
            }

            if (hasPhoneUpdate)
            {
                user.PhoneNumber = PhoneRules.ToInternationalFormat(request.PhoneNumber!);
                user.NormalizedPhoneNumber = normalizedPhone;
            }

            if (uploadedAvatar is not null)
            {
                user.AvatarUrl = uploadedAvatar.Url;
                user.AvatarPublicId = uploadedAvatar.PublicId;
                user.AvatarSource = AvatarSource.Upload;
                user.AvatarUpdatedAt = now;
            }

            await _context.SaveChangesAsync(ct);

            if (challenge is not null && otpCode is not null)
            {
                pendingEmailVerification = new PendingEmailVerification(
                    challenge.Id,
                    email!,
                    otpCode,
                    challenge.ExpiresAt,
                    challenge.ResendAvailableAt);
            }
        }, cancellationToken);

        if (pendingEmailVerification is not null)
        {
            await _otpSender.SendAsync(
                pendingEmailVerification.Email,
                pendingEmailVerification.Code,
                OtpPurpose.EmailChange,
                user.FullName,
                cancellationToken);
        }

        var updatedUser = await _context.Set<User>()
            .Include(x => x.Role)
            .SingleAsync(x => x.Id == user.Id, cancellationToken);

        return new UpdateProfileResultDto(
            AuthSupport.CreateUserDto(updatedUser),
            pendingEmailVerification is null
                ? null
                : new OtpChallengeDto(
                    pendingEmailVerification.ChallengeId,
                    _otpCodeService.MaskEmail(pendingEmailVerification.Email),
                    pendingEmailVerification.ExpiresAt,
                    pendingEmailVerification.ResendAvailableAt)
                {
                    Channel = OtpChannel.Email
                });
    }

    private void EnsureValidAvatar(UpdateCurrentUserProfileCommand request)
    {
        if (string.IsNullOrWhiteSpace(request.AvatarFileName))
        {
            throw AuthSupport.CreateValidationException(nameof(request.AvatarFileName), "Tên file ảnh đại diện là bắt buộc.");
        }

        if (!request.AvatarLength.HasValue || request.AvatarLength <= 0)
        {
            throw AuthSupport.CreateValidationException(nameof(request.AvatarLength), "Ảnh đại diện là bắt buộc.");
        }

        if (request.AvatarLength > _profileImageStorage.MaxAvatarBytes)
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.AvatarLength),
                $"Ảnh đại diện không được vượt quá {_profileImageStorage.MaxAvatarBytes / 1024 / 1024} MB.");
        }

        if (string.IsNullOrWhiteSpace(request.AvatarContentType)
            || !_profileImageStorage.AllowedAvatarContentTypes.Contains(
                request.AvatarContentType,
                StringComparer.OrdinalIgnoreCase))
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.AvatarContentType),
                "Ảnh đại diện chỉ hỗ trợ JPEG, PNG hoặc WebP.");
        }
    }
}
