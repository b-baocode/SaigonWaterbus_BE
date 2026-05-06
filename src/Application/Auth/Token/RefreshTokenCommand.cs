using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Auth.Token;

public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<AuthSessionDto>;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

public sealed class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthSessionDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ISecretHasher _secretHasher;
    private readonly ITokenService _tokenService;
    private readonly TimeProvider _timeProvider;

    public RefreshTokenCommandHandler(
        IApplicationDbContext context,
        ISecretHasher secretHasher,
        ITokenService tokenService,
        TimeProvider timeProvider)
    {
        _context = context;
        _secretHasher = secretHasher;
        _tokenService = tokenService;
        _timeProvider = timeProvider;
    }

    public async Task<AuthSessionDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        if (!AuthSupport.TryParseRefreshToken(request.RefreshToken, out var refreshTokenId, out var refreshSecret))
        {
            throw new UnauthorizedAccessException();
        }

        var refreshToken = await _context.Set<RefreshToken>()
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.Id == refreshTokenId, cancellationToken)
            ?? throw new UnauthorizedAccessException();

        if (!_secretHasher.Verify(refreshSecret, refreshToken.TokenHash)
            || refreshToken.RevokedAt.HasValue
            || refreshToken.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            throw new UnauthorizedAccessException();
        }

        var user = refreshToken.User;
        AuthSupport.EnsureUserCanLogin(user, requireVerifiedPhone: false);

        refreshToken.RevokedAt = _timeProvider.GetUtcNow();
        user.LastLoginAt = _timeProvider.GetUtcNow();

        var roles = await AuthSupport.GetActiveRolesAsync(_context, user.Id, cancellationToken);
        if (roles.Count == 0)
        {
            throw AuthSupport.CreateValidationException(nameof(request.RefreshToken), "Account does not have any active roles.");
        }

        var accessToken = _tokenService.GenerateAccessToken(
            user.Id,
            user.PhoneNumber,
            user.Email,
            roles.Select(x => x.SystemName).ToArray());

        var newRefreshSecret = _tokenService.GenerateRefreshTokenSecret();
        var newRefreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _secretHasher.Hash(newRefreshSecret),
            ExpiresAt = _tokenService.GetRefreshTokenExpiry()
        };

        _context.Set<RefreshToken>().Add(newRefreshToken);
        await _context.SaveChangesAsync(cancellationToken);

        return AuthSupport.CreateSessionDto(
            user,
            roles,
            accessToken,
            AuthSupport.FormatRefreshToken(newRefreshToken.Id, newRefreshSecret),
            newRefreshToken.ExpiresAt);
    }
}
