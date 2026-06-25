using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Auth.Token;

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty()
            .WithMessage("Refresh token là bắt buộc.");
    }
}

public sealed class RefreshTokenRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly ISecretHasher _secretHasher;
    private readonly ITokenService _tokenService;
    private readonly TimeProvider _timeProvider;

    public RefreshTokenRequestUseCase(
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

    public async Task<AuthSessionDto> ExecuteAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        if (!AuthSupport.TryParseRefreshToken(request.RefreshToken, out var refreshTokenId, out var refreshSecret))
        {
            throw new UnauthorizedAccessException();
        }

        var refreshToken = await _context.Set<RefreshToken>()
            .Include(x => x.User).ThenInclude(u => u.StationAssignments).ThenInclude(a => a.Station)
            .SingleOrDefaultAsync(x => x.Id == refreshTokenId, cancellationToken)
            ?? throw new UnauthorizedAccessException();

        var now = _timeProvider.GetUtcNow();
        if (!_secretHasher.Verify(refreshSecret, refreshToken.TokenHash)
            || refreshToken.RevokedAt.HasValue
            || refreshToken.ExpiresAt <= now)
        {
            throw new UnauthorizedAccessException();
        }

        var user = refreshToken.User;
        AuthSupport.EnsureUserCanLogin(user, requireVerifiedPhone: false);

        refreshToken.RevokedAt = now;
        user.LastLoginAt = now;

        var roles = await AuthSupport.GetActiveRolesAsync(_context, user.Id, cancellationToken);
        if (roles.Count == 0)
        {
            throw AuthSupport.CreateValidationException(nameof(request.RefreshToken), "Tài khoản chưa có vai trò hoạt động.");
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
