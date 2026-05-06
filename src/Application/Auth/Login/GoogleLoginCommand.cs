using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Login;

public sealed record GoogleLoginCommand(string IdToken) : IRequest<GoogleLoginResultDto>;

public sealed class GoogleLoginCommandValidator : AbstractValidator<GoogleLoginCommand>
{
    public GoogleLoginCommandValidator()
    {
        RuleFor(x => x.IdToken).NotEmpty();
    }
}

public sealed class GoogleLoginCommandHandler : IRequestHandler<GoogleLoginCommand, GoogleLoginResultDto>
{
    private const string GoogleProvider = "google";
    private const string GoogleDisplayProvider = "Google";
    private const string LoggedInStatus = "LOGGED_IN";

    private readonly IApplicationDbContext _context;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly ITokenService _tokenService;
    private readonly IProfileImageStorageService _profileImageStorage;
    private readonly ISecretHasher _secretHasher;
    private readonly IUserCodeGenerator _userCodeGenerator;
    private readonly ILoginNotificationSender _loginNotificationSender;
    private readonly IClientInfoProvider _clientInfoProvider;
    private readonly TimeProvider _timeProvider;
    private readonly string _googleClientId;

    public GoogleLoginCommandHandler(
        IApplicationDbContext context,
        IIdentityNormalizer identityNormalizer,
        ITokenService tokenService,
        IProfileImageStorageService profileImageStorage,
        ISecretHasher secretHasher,
        IUserCodeGenerator userCodeGenerator,
        ILoginNotificationSender loginNotificationSender,
        IClientInfoProvider clientInfoProvider,
        TimeProvider timeProvider,
        IConfiguration configuration)
    {
        _context = context;
        _identityNormalizer = identityNormalizer;
        _tokenService = tokenService;
        _profileImageStorage = profileImageStorage;
        _secretHasher = secretHasher;
        _userCodeGenerator = userCodeGenerator;
        _loginNotificationSender = loginNotificationSender;
        _clientInfoProvider = clientInfoProvider;
        _timeProvider = timeProvider;
        _googleClientId = configuration["OAuth:Google:ClientId"] ?? throw new InvalidOperationException("Google ClientId not configured");
    }

    public async Task<GoogleLoginResultDto> Handle(GoogleLoginCommand request, CancellationToken cancellationToken)
    {
        var payload = await ValidateGoogleTokenAsync(request.IdToken);

        if (string.IsNullOrWhiteSpace(payload.Subject))
        {
            throw new UnauthorizedAccessException("Google token does not contain a subject.");
        }

        if (string.IsNullOrWhiteSpace(payload.Email))
        {
            throw new UnauthorizedAccessException("Google token does not contain an email address.");
        }

        if (!payload.EmailVerified)
        {
            throw new UnauthorizedAccessException("Google email is not verified.");
        }

        if (!EmailRules.HasAllowedRegistrationDomain(payload.Email))
        {
            throw AuthSupport.CreateValidationException(nameof(request.IdToken), EmailRules.AllowedEmailDomainMessage);
        }

        var normalizedEmail = _identityNormalizer.NormalizeEmail(payload.Email);
        var now = _timeProvider.GetUtcNow();

        var externalLogin = await _context.Set<ExternalLogin>()
            .Include(x => x.User)
            .FirstOrDefaultAsync(
                x => x.Provider == GoogleProvider && x.ProviderUserId == payload.Subject,
                cancellationToken);

        if (externalLogin is not null)
        {
            var linkedUser = externalLogin.User ?? throw new InvalidOperationException("User not found");
            EnsureGoogleUserCanLogin(linkedUser, nameof(request.IdToken));
            return await CreateLoggedInResultAsync(linkedUser, payload, now, sendLoginNotification: false, cancellationToken);
        }

        var existingUser = await _context.Set<User>()
            .FirstOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

        if (existingUser is not null)
        {
            EnsureGoogleUserCanLogin(existingUser, nameof(request.IdToken));
            AddExternalLogin(existingUser, payload, now);
            return await CreateLoggedInResultAsync(existingUser, payload, now, sendLoginNotification: true, cancellationToken);
        }

        var user = await CreateGoogleUserAsync(payload, normalizedEmail, now, cancellationToken);
        return await CreateLoggedInResultAsync(user, payload, now, sendLoginNotification: true, cancellationToken);
    }

    private async Task<GoogleJsonWebSignature.Payload> ValidateGoogleTokenAsync(string idToken)
    {
        try
        {
            return await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = [_googleClientId]
                });
        }
        catch (InvalidJwtException ex)
        {
            throw new UnauthorizedAccessException($"Google token is invalid: {ex.Message}");
        }
    }

    private async Task<User> CreateGoogleUserAsync(
        GoogleJsonWebSignature.Payload payload,
        string normalizedEmail,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var customerRole = await AuthSupport.GetRoleByCodeAsync(
            _context,
            Roles.CustomerCode,
            cancellationToken);

        var user = new User
        {
            UserCode = await _userCodeGenerator.GenerateNextCodeAsync(customerRole.Code, cancellationToken),
            FullName = ResolveGoogleDisplayName(payload.Name),
            Email = payload.Email,
            NormalizedEmail = normalizedEmail,
            RoleId = customerRole.Id,
            Status = UserStatus.Active
        };

        _context.Set<User>().Add(user);
        AddExternalLogin(user, payload, now);
        await _context.SaveChangesAsync(cancellationToken);

        return user;
    }

    private async Task<GoogleLoginResultDto> CreateLoggedInResultAsync(
        User user,
        GoogleJsonWebSignature.Payload payload,
        DateTimeOffset now,
        bool sendLoginNotification,
        CancellationToken cancellationToken)
    {
        var roles = await AuthSupport.GetActiveRolesAsync(_context, user.Id, cancellationToken);

        if (roles.Count == 0)
        {
            var customerRole = await AuthSupport.GetRoleByCodeAsync(
                _context,
                Roles.CustomerCode,
                cancellationToken);

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

        var session = AuthSupport.CreateSessionDto(
            user,
            roles,
            accessToken,
            AuthSupport.FormatRefreshToken(refreshTokenEntity.Id, refreshTokenSecret),
            refreshTokenEntity.ExpiresAt);

        if (sendLoginNotification)
        {
            await _loginNotificationSender.SendLoginSucceededAsync(
                new LoginNotification(
                    payload.Email,
                    user.FullName,
                    GoogleDisplayProvider,
                    now,
                    _clientInfoProvider.GetDeviceInfo()),
                cancellationToken);
        }

        return new GoogleLoginResultDto(
            LoggedInStatus,
            session.User,
            session.Tokens);
    }

    private void AddExternalLogin(
        User user,
        GoogleJsonWebSignature.Payload payload,
        DateTimeOffset linkedAt)
    {
        var externalLogin = new ExternalLogin
        {
            Provider = GoogleProvider,
            ProviderUserId = payload.Subject,
            Email = payload.Email,
            DisplayName = payload.Name,
            ProfilePictureUrl = payload.Picture,
            LinkedAt = linkedAt
        };

        if (user.Id > 0)
        {
            externalLogin.UserId = user.Id;
        }
        else
        {
            externalLogin.User = user;
        }

        _context.Set<ExternalLogin>().Add(externalLogin);
    }

    private static void EnsureGoogleUserCanLogin(User user, string propertyName)
    {
        if (user.Status == UserStatus.Suspended)
        {
            throw AuthSupport.CreateValidationException(propertyName, "Tài khoản đã bị tạm khóa.");
        }

        if (user.Status == UserStatus.PendingVerification)
        {
            user.Status = UserStatus.Active;
        }
    }

    private static string ResolveGoogleDisplayName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Google User" : name.Trim();
}
