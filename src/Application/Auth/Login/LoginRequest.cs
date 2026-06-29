using System.ComponentModel.DataAnnotations;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Auth.Login;

public sealed record LoginRequest(
    string EmailOrPhone,
    string Password);

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    private static readonly EmailAddressAttribute EmailAddressValidator = new();

    public LoginRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.EmailOrPhone))
            .WithMessage("Email hoặc số điện thoại là bắt buộc.")
            .OverridePropertyName(nameof(LoginRequest.EmailOrPhone));

        RuleFor(x => x)
            .Must(x => string.IsNullOrWhiteSpace(x.EmailOrPhone) || IsValidEmailOrPhone(x.EmailOrPhone))
            .WithMessage("Vui lòng nhập email đúng định dạng hoặc số điện thoại Việt Nam hợp lệ.")
            .OverridePropertyName(nameof(LoginRequest.EmailOrPhone));

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("Mật khẩu là bắt buộc.");
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

public sealed class LoginRequestUseCase
{
    private const int MaxFailedLoginAttempts = 5;
    private static readonly TimeSpan FailedLoginWindow = TimeSpan.FromMinutes(15);

    private readonly IApplicationDbContext _context;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly ISecretHasher _secretHasher;
    private readonly ITokenService _tokenService;
    private readonly TimeProvider _timeProvider;

    public LoginRequestUseCase(
        IApplicationDbContext context,
        IIdentityNormalizer identityNormalizer,
        ISecretHasher secretHasher,
        ITokenService tokenService,
        TimeProvider timeProvider)
    {
        _context = context;
        _identityNormalizer = identityNormalizer;
        _secretHasher = secretHasher;
        _tokenService = tokenService;
        _timeProvider = timeProvider;
    }

    public async Task<AuthSessionDto> ExecuteAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await GetUserByEmailOrPhoneAsync(request.EmailOrPhone, cancellationToken);
        var now = _timeProvider.GetUtcNow();

        AuthSupport.EnsureUserCanLogin(user, nameof(request.EmailOrPhone));

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            throw AuthSupport.CreateValidationException(
                nameof(request.Password),
                "Tài khoản này đăng nhập bằng Google và chưa có mật khẩu. Vui lòng đăng nhập bằng Google hoặc dùng quên mật khẩu để đặt mật khẩu.");
        }

        if (!_secretHasher.Verify(request.Password, user.PasswordHash))
        {
            await RegisterFailedLoginAttemptAsync(user, now, cancellationToken);
            if (user.Status == UserStatus.Suspended)
            {
                throw AuthSupport.CreateValidationException(
                    nameof(request.EmailOrPhone),
                    "Tài khoản đã bị khóa do đăng nhập sai 5 lần trong 15 phút. Vui lòng liên hệ Admin để mở lại.");
            }

            throw AuthSupport.CreateValidationException(nameof(request.Password), "Mật khẩu không đúng.");
        }

        var roles = await AuthSupport.GetActiveRolesAsync(_context, user.Id, cancellationToken);
        if (roles.Count == 0)
        {
            throw AuthSupport.CreateValidationException(nameof(request.EmailOrPhone), "Tài khoản chưa có vai trò hoạt động.");
        }

        ResetFailedLoginTracking(user);
        user.LastLoginAt = now;

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

        return AuthSupport.CreateSessionDto(
            user,
            roles,
            accessToken,
            AuthSupport.FormatRefreshToken(refreshTokenEntity.Id, refreshTokenSecret),
            refreshTokenEntity.ExpiresAt);
    }

    private async Task<User> GetUserByEmailOrPhoneAsync(string emailOrPhone, CancellationToken cancellationToken)
    {
        var trimmedEmailOrPhone = emailOrPhone.Trim();
        var query = _context.Set<User>();

        if (IsEmailInput(trimmedEmailOrPhone))
        {
            var normalizedEmail = _identityNormalizer.NormalizeEmail(trimmedEmailOrPhone);
            return await AuthSupport.WhereUserIdentityMatches(query, null, normalizedEmail)
                .Include(u => u.StationAssignments).ThenInclude(a => a.Station)
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy tài khoản.");
        }

        var normalizedPhone = _identityNormalizer.NormalizePhone(trimmedEmailOrPhone);
        return await AuthSupport.WhereUserIdentityMatches(query, normalizedPhone, null)
            .Include(u => u.StationAssignments).ThenInclude(a => a.Station)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy tài khoản.");
    }

    private static bool IsEmailInput(string emailOrPhone) =>
        emailOrPhone.Contains('@', StringComparison.Ordinal);

    private async Task RegisterFailedLoginAttemptAsync(User user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (user.FailedLoginWindowStartedAt is null
            || now - user.FailedLoginWindowStartedAt.Value > FailedLoginWindow)
        {
            user.FailedLoginWindowStartedAt = now;
            user.FailedLoginAttemptCount = 1;
        }
        else
        {
            user.FailedLoginAttemptCount += 1;
        }

        if (user.FailedLoginAttemptCount >= MaxFailedLoginAttempts)
        {
            user.Status = UserStatus.Suspended;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static void ResetFailedLoginTracking(User user)
    {
        user.FailedLoginAttemptCount = 0;
        user.FailedLoginWindowStartedAt = null;
    }
}
