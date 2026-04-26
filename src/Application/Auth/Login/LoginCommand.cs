using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Auth.Login;

public sealed record LoginCommand(string Email, string Password) : IRequest<AuthSessionDto>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(255)
            .EmailAddress();

        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, AuthSessionDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly ISecretHasher _secretHasher;
    private readonly ITokenService _tokenService;
    private readonly IUserContext _userContext;
    private readonly TimeProvider _timeProvider;

    public LoginCommandHandler(
        IApplicationDbContext context,
        IIdentityNormalizer identityNormalizer,
        ISecretHasher secretHasher,
        ITokenService tokenService,
        IUserContext userContext,
        TimeProvider timeProvider)
    {
        _context = context;
        _identityNormalizer = identityNormalizer;
        _secretHasher = secretHasher;
        _tokenService = tokenService;
        _userContext = userContext;
        _timeProvider = timeProvider;
    }

    public async Task<AuthSessionDto> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = _identityNormalizer.NormalizeEmail(request.Email);
        var user = await _context.Set<User>()
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken)
            ?? throw new UnauthorizedAccessException();

        AuthSupport.EnsureUserCanLogin(user, nameof(request.Email));

        if (string.IsNullOrWhiteSpace(user.PasswordHash)
            || !_secretHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException();
        }

        var roles = await AuthSupport.GetActiveRolesAsync(_context, user.Id, cancellationToken);
        if (roles.Count == 0)
        {
            throw AuthSupport.CreateValidationException(nameof(request.Email), "Account does not have any active roles.");
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
            ExpiresAt = _tokenService.GetRefreshTokenExpiry(),
            DeviceName = "password_login",
            IpAddress = _userContext.IpAddress,
            UserAgent = _userContext.UserAgent
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
}
