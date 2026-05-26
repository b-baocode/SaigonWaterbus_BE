using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Login;

public sealed record VerifyGooglePhoneCommand(string TempToken, string Otp) : IRequest<AuthSessionDto>;

public sealed class VerifyGooglePhoneCommandValidator : AbstractValidator<VerifyGooglePhoneCommand>
{
    public VerifyGooglePhoneCommandValidator()
    {
        RuleFor(x => x.TempToken)
            .NotEmpty()
            .WithMessage("Temp token là bắt buộc.");

        RuleFor(x => x.Otp)
            .NotEmpty()
            .WithMessage("Mã OTP là bắt buộc.")
            .Length(4, 10)
            .WithMessage("Mã OTP không hợp lệ.");
    }
}

public sealed class VerifyGooglePhoneCommandHandler : IRequestHandler<VerifyGooglePhoneCommand, AuthSessionDto>
{
    private const string GoogleProvider = "google";

    private readonly IApplicationDbContext _context;
    private readonly IGoogleLoginTempStore _tempStore;
    private readonly ISecretHasher _secretHasher;
    private readonly ITokenService _tokenService;
    private readonly IUserCodeGenerator _userCodeGenerator;
    private readonly IProfileImageStorageService _profileImageStorage;
    private readonly TimeProvider _timeProvider;

    public VerifyGooglePhoneCommandHandler(
        IApplicationDbContext context,
        IGoogleLoginTempStore tempStore,
        ISecretHasher secretHasher,
        ITokenService tokenService,
        IUserCodeGenerator userCodeGenerator,
        IProfileImageStorageService profileImageStorage,
        TimeProvider timeProvider)
    {
        _context = context;
        _tempStore = tempStore;
        _secretHasher = secretHasher;
        _tokenService = tokenService;
        _userCodeGenerator = userCodeGenerator;
        _profileImageStorage = profileImageStorage;
        _timeProvider = timeProvider;
    }

    public async Task<AuthSessionDto> Handle(VerifyGooglePhoneCommand request, CancellationToken cancellationToken)
    {
        var session = await _tempStore.GetAsync(request.TempToken, cancellationToken)
            ?? throw AuthSupport.CreateValidationException(nameof(request.TempToken), "Temp token không hợp lệ hoặc đã hết hạn.");
        if (string.IsNullOrWhiteSpace(session.PhoneNumber) || string.IsNullOrWhiteSpace(session.NormalizedPhoneNumber))
        {
            throw AuthSupport.CreateValidationException(nameof(request.TempToken), "Vui lòng gửi OTP trước khi xác minh.");
        }

        var phoneNumber = session.PhoneNumber!;
        var normalizedPhone = session.NormalizedPhoneNumber!;
        var now = _timeProvider.GetUtcNow();
        if (string.IsNullOrWhiteSpace(session.OtpCodeHash) || session.OtpExpiresAt is null)
        {
            throw AuthSupport.CreateValidationException(nameof(request.TempToken), "Vui lòng gửi OTP trước khi xác minh.");
        }

        if (session.OtpExpiresAt <= now)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Otp), "OTP đã hết hạn.");
        }

        if (!_secretHasher.Verify(request.Otp, session.OtpCodeHash))
        {
            var failedSession = session with
            {
                OtpAttemptCount = session.OtpAttemptCount + 1
            };

            if (failedSession.OtpAttemptCount >= session.OtpMaxAttempts)
            {
                await _tempStore.RemoveAsync(session.TempToken, cancellationToken);
            }
            else
            {
                await _tempStore.SaveAsync(failedSession, cancellationToken);
            }

            throw AuthSupport.CreateValidationException(nameof(request.Otp), "OTP không hợp lệ.");
        }

        var userId = await _context.ExecuteInTransactionAsync(
            async ct => await CreateOrCompleteUserAsync(session, phoneNumber, normalizedPhone, now, ct),
            cancellationToken);

        var user = await _context.Set<User>()
            .Include(x => x.Role)
            .SingleAsync(x => x.Id == userId, cancellationToken);

        AuthSupport.EnsureUserCanLogin(user);

        if (!string.IsNullOrWhiteSpace(session.AvatarUrl)
            && user.AvatarSource != AvatarSource.Upload)
        {
            var importedAvatar = await _profileImageStorage.ImportAvatarFromUrlAsync(
                new ProfileImageUrlImport(
                    user.Id,
                    session.AvatarUrl,
                    "google-avatar.jpg"),
                cancellationToken);

            user.AvatarUrl = importedAvatar.Url;
            user.AvatarPublicId = importedAvatar.PublicId;
            user.AvatarSource = AvatarSource.Google;
            user.AvatarUpdatedAt = now;
        }

        user.LastLoginAt = now;

        var roles = await AuthSupport.GetActiveRolesAsync(_context, user.Id, cancellationToken);
        if (roles.Count == 0)
        {
            throw AuthSupport.CreateValidationException(nameof(request.TempToken), "Tài khoản chưa có vai trò hoạt động.");
        }

        var accessToken = _tokenService.GenerateAccessToken(
            user.Id,
            user.PhoneNumber,
            user.Email,
            roles.Select(x => x.SystemName).ToArray());

        var refreshTokenSecret = _tokenService.GenerateRefreshTokenSecret();
        var refreshTokenEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _secretHasher.Hash(refreshTokenSecret),
            ExpiresAt = _tokenService.GetRefreshTokenExpiry()
        };

        _context.Set<RefreshToken>().Add(refreshTokenEntity);
        await _context.SaveChangesAsync(cancellationToken);
        await _tempStore.RemoveAsync(session.TempToken, cancellationToken);

        return AuthSupport.CreateSessionDto(
            user,
            roles,
            accessToken,
            AuthSupport.FormatRefreshToken(refreshTokenEntity.Id, refreshTokenSecret),
            refreshTokenEntity.ExpiresAt);
    }

    private async Task<int> CreateOrCompleteUserAsync(
        GoogleLoginTempSession session,
        string phoneNumber,
        string normalizedPhone,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await IsPhoneUsedByAnotherUserAsync(normalizedPhone, session.ExistingUserId, cancellationToken))
        {
            throw AuthSupport.CreateValidationException(nameof(VerifyGooglePhoneCommand.TempToken), "Số điện thoại đã được đăng ký.");
        }

        if (session.ExistingUserId.HasValue)
        {
            var existingUser = await _context.Set<User>()
                .SingleOrDefaultAsync(x => x.Id == session.ExistingUserId.Value, cancellationToken)
                ?? throw AuthSupport.CreateValidationException(nameof(VerifyGooglePhoneCommand.TempToken), "User liên kết với temp token không còn tồn tại.");

            if (existingUser.Status != UserStatus.Active)
            {
                AuthSupport.EnsureUserCanLogin(existingUser);
            }

            existingUser.PhoneNumber = phoneNumber;
            existingUser.NormalizedPhoneNumber = normalizedPhone;
            existingUser.PhoneVerifiedAt = now;
            existingUser.Status = UserStatus.Active;

            if (string.IsNullOrWhiteSpace(existingUser.Email))
            {
                existingUser.Email = session.Email;
                existingUser.NormalizedEmail = session.NormalizedEmail;
            }

            await EnsureExternalLoginAsync(existingUser.Id, session, now, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return existingUser.Id;
        }

        if (await _context.Set<User>().AnyAsync(x => x.NormalizedEmail == session.NormalizedEmail, cancellationToken))
        {
            throw AuthSupport.CreateValidationException(nameof(VerifyGooglePhoneCommand.TempToken), "Email Google đã được đăng ký.");
        }

        var customerRole = await AuthSupport.GetRoleByCodeAsync(
            _context,
            Roles.CustomerCode,
            cancellationToken);

        var user = new User
        {
            UserCode = await _userCodeGenerator.GenerateNextCodeAsync(customerRole.Code, cancellationToken),
            FullName = string.IsNullOrWhiteSpace(session.Name) ? "Google User" : session.Name.Trim(),
            PhoneNumber = phoneNumber,
            NormalizedPhoneNumber = normalizedPhone,
            PhoneVerifiedAt = now,
            Email = session.Email,
            NormalizedEmail = session.NormalizedEmail,
            RoleId = customerRole.Id,
            Status = UserStatus.Active
        };

        _context.Set<User>().Add(user);
        _context.Set<ExternalLogin>().Add(new ExternalLogin
        {
            User = user,
            Provider = GoogleProvider,
            ProviderUserId = session.GoogleUserId,
            Email = session.Email,
            DisplayName = session.Name,
            ProfilePictureUrl = session.AvatarUrl,
            LinkedAt = now
        });

        await _context.SaveChangesAsync(cancellationToken);
        return user.Id;
    }

    private async Task EnsureExternalLoginAsync(
        int userId,
        GoogleLoginTempSession session,
        DateTimeOffset linkedAt,
        CancellationToken cancellationToken)
    {
        var exists = await _context.Set<ExternalLogin>().AnyAsync(
            x => x.Provider == GoogleProvider && x.ProviderUserId == session.GoogleUserId,
            cancellationToken);

        if (exists)
        {
            return;
        }

        _context.Set<ExternalLogin>().Add(new ExternalLogin
        {
            UserId = userId,
            Provider = GoogleProvider,
            ProviderUserId = session.GoogleUserId,
            Email = session.Email,
            DisplayName = session.Name,
            ProfilePictureUrl = session.AvatarUrl,
            LinkedAt = linkedAt
        });
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
