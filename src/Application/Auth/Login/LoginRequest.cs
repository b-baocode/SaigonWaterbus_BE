using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using System.ComponentModel.DataAnnotations;

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

        AuthSupport.EnsureUserCanLogin(user, nameof(request.EmailOrPhone));

        if (string.IsNullOrWhiteSpace(user.PasswordHash)
            || !_secretHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException();
        }

        var roles = await AuthSupport.GetActiveRolesAsync(_context, user.Id, cancellationToken);
        if (roles.Count == 0)
        {
            throw AuthSupport.CreateValidationException(nameof(request.EmailOrPhone), "Tài khoản chưa có vai trò hoạt động.");
        }

        user.LastLoginAt = _timeProvider.GetUtcNow();

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
            return await query.SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken)
                ?? throw new UnauthorizedAccessException();
        }

        var normalizedPhone = _identityNormalizer.NormalizePhone(trimmedEmailOrPhone);
        return await query
            .SingleOrDefaultAsync(x => x.NormalizedPhoneNumber == normalizedPhone, cancellationToken)
            ?? throw new UnauthorizedAccessException();
    }

    private static bool IsEmailInput(string emailOrPhone) =>
        emailOrPhone.Contains('@', StringComparison.Ordinal);
}
