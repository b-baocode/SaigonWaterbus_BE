using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Auth.Login;

public sealed record LoginCommand(
    string Phone,
    string Password) : IRequest<AuthSessionDto>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty()
            .WithMessage("Số điện thoại là bắt buộc.")
            .Must(PhoneRules.IsValid)
            .WithMessage(PhoneRules.InvalidInternationalPhoneMessage);

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("Mật khẩu là bắt buộc.");
    }
}

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, AuthSessionDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly ISecretHasher _secretHasher;
    private readonly ITokenService _tokenService;
    private readonly TimeProvider _timeProvider;

    public LoginCommandHandler(
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

    public async Task<AuthSessionDto> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await GetUserByPhoneAsync(request.Phone, cancellationToken);

        AuthSupport.EnsureUserCanLogin(user, nameof(request.Phone));

        if (string.IsNullOrWhiteSpace(user.PasswordHash)
            || !_secretHasher.Verify(request.Password, user.PasswordHash))
        {
            throw AuthSupport.CreateValidationException(nameof(request.Password), "Mật khẩu không đúng.");
        }

        var roles = await AuthSupport.GetActiveRolesAsync(_context, user.Id, cancellationToken);
        if (roles.Count == 0)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Phone), "Tài khoản chưa có vai trò hoạt động.");
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

    private async Task<User> GetUserByPhoneAsync(string phone, CancellationToken cancellationToken)
    {
        var normalizedPhone = _identityNormalizer.NormalizePhone(phone);

        return await _context.Set<User>()
            .SingleOrDefaultAsync(x => x.NormalizedPhoneNumber == normalizedPhone, cancellationToken)
            ?? throw AuthSupport.CreateValidationException(nameof(LoginCommand.Phone), "Số điện thoại chưa được đăng ký.");
    }
}
