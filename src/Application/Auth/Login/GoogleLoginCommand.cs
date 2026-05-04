using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Login;

public sealed record GoogleLoginCommand(string IdToken) : IRequest<AuthSessionDto>;

public sealed class GoogleLoginCommandValidator : AbstractValidator<GoogleLoginCommand>
{
    public GoogleLoginCommandValidator()
    {
        RuleFor(x => x.IdToken).NotEmpty();
    }
}

public sealed class GoogleLoginCommandHandler : IRequestHandler<GoogleLoginCommand, AuthSessionDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IProfileImageStorageService _profileImageStorage;
    private readonly ISecretHasher _secretHasher;
    private readonly IUserCodeGenerator _userCodeGenerator;
    private readonly TimeProvider _timeProvider;
    private readonly string _googleClientId;

    public GoogleLoginCommandHandler(
        IApplicationDbContext context,
        ITokenService tokenService,
        IProfileImageStorageService profileImageStorage,
        ISecretHasher secretHasher,
        IUserCodeGenerator userCodeGenerator,
        TimeProvider timeProvider,
        IConfiguration configuration)
    {
        _context = context;
        _tokenService = tokenService;
        _profileImageStorage = profileImageStorage;
        _secretHasher = secretHasher;
        _userCodeGenerator = userCodeGenerator;
        _timeProvider = timeProvider;
        _googleClientId = configuration["OAuth:Google:ClientId"] ?? throw new InvalidOperationException("Google ClientId not configured");
    }

    public async Task<AuthSessionDto> Handle(GoogleLoginCommand request, CancellationToken cancellationToken)
    {
        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(
                request.IdToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = [_googleClientId]
                });
        }
        catch (InvalidJwtException ex)
        {
            throw new UnauthorizedAccessException($"Google token is invalid: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(payload.Email))
        {
            throw new UnauthorizedAccessException("Google token does not contain an email address.");
        }

        var normalizedEmail = payload.Email.Trim().ToUpperInvariant();
        Role? customerRole = null;

        async Task<Role> GetCustomerRoleAsync()
        {
            if (customerRole is null)
            {
                customerRole = await AuthSupport.GetRoleByCodeAsync(
                    _context,
                    Roles.CustomerCode,
                    cancellationToken);
            }

            return customerRole;
        }

        var now = _timeProvider.GetUtcNow();

        var externalLogin = await _context.Set<ExternalLogin>()
            .Include(x => x.User)
            .FirstOrDefaultAsync(
                x => x.Provider == "google" && x.ProviderUserId == payload.Subject,
                cancellationToken);

        User user;

        if (externalLogin != null)
        {
            user = externalLogin.User ?? throw new InvalidOperationException("User not found");
        }
        else
        {
            customerRole = await GetCustomerRoleAsync();

            var existingUser = await _context.Set<User>()
                .FirstOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

            if (existingUser != null)
            {
                user = existingUser;
            }
            else
            {
                user = new User
                {
                    UserCode = await _userCodeGenerator.GenerateNextCodeAsync(customerRole.Code, cancellationToken),
                    FullName = payload.Name ?? "Google User",
                    Email = payload.Email,
                    NormalizedEmail = normalizedEmail,
                    RoleId = customerRole.Id,
                    Status = UserStatus.Active
                };

                _context.Set<User>().Add(user);
                await _context.SaveChangesAsync(cancellationToken);
            }

            var newExternalLogin = new ExternalLogin
            {
                UserId = user.Id,
                Provider = "google",
                ProviderUserId = payload.Subject,
                Email = payload.Email,
                DisplayName = payload.Name,
                ProfilePictureUrl = payload.Picture,
                LinkedAt = now
            };

            _context.Set<ExternalLogin>().Add(newExternalLogin);
        }

        AuthSupport.EnsureUserCanLogin(user, nameof(request.IdToken));

        var roles = await AuthSupport.GetActiveRolesAsync(_context, user.Id, cancellationToken);
        
        if (roles.Count == 0)
        {
            customerRole = await GetCustomerRoleAsync();

            user.RoleId = customerRole.Id;
            roles = new[] { customerRole };
        }

        if (string.IsNullOrWhiteSpace(user.UserCode))
        {
            user.UserCode = await _userCodeGenerator.GenerateNextCodeAsync(roles.First().Code, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(payload.Picture)
            && user.AvatarSource != AvatarSource.Upload)
        {
            var importedAvatar = await _profileImageStorage.ImportAvatarFromUrlAsync(
                new ProfileImageUrlImport(
                    user.Id,
                    payload.Picture,
                    "google-avatar.jpg"),
                cancellationToken);

            user.AvatarUrl = importedAvatar.Url;
            user.AvatarPublicId = importedAvatar.PublicId;
            user.AvatarSource = AvatarSource.Google;
            user.AvatarUpdatedAt = now;
        }

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
}
