using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Auth.Login;

public sealed record GoogleLoginRequest(string IdToken);

public sealed class GoogleLoginRequestValidator : AbstractValidator<GoogleLoginRequest>
{
    public GoogleLoginRequestValidator()
    {
        RuleFor(x => x).Custom((request, context) =>
        {
            if (string.IsNullOrWhiteSpace(request.IdToken))
            {
                context.AddFailure(nameof(GoogleLoginRequest.IdToken), "IdToken là bắt buộc.");
            }
        });
    }
}

public sealed class GoogleLoginRequestUseCase
{
    private const string GoogleProvider = "google";
    private const string LoggedInStatus = "LOGGED_IN";

    private readonly IApplicationDbContext _context;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly ITokenService _tokenService;
    private readonly IProfileImageStorageService _profileImageStorage;
    private readonly ISecretHasher _secretHasher;
    private readonly IUserCodeGenerator _userCodeGenerator;
    private readonly ILoginNotificationSender _loginNotificationSender;
    private readonly ILogger<GoogleLoginRequestUseCase> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlyCollection<string> _googleClientIds;

    public GoogleLoginRequestUseCase(
        IApplicationDbContext context,
        IIdentityNormalizer identityNormalizer,
        ITokenService tokenService,
        IProfileImageStorageService profileImageStorage,
        ISecretHasher secretHasher,
        IUserCodeGenerator userCodeGenerator,
        ILoginNotificationSender loginNotificationSender,
        ILogger<GoogleLoginRequestUseCase> logger,
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
        _logger = logger;
        _timeProvider = timeProvider;
        _googleClientIds = LoadGoogleClientIds(configuration);
    }

    public async Task<GoogleLoginResultDto> ExecuteAsync(GoogleLoginRequest request, CancellationToken cancellationToken)
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

        var existingUser = await AuthSupport.WhereUserIdentityMatches(
                _context.Set<User>(),
                null,
                normalizedEmail)
            .Include(u => u.StationAssignments).ThenInclude(a => a.Station)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingUser is not null)
        {
            var shouldSendFirstGoogleLoginEmail = existingUser.LastLoginAt is null;

            if (existingUser.Status == UserStatus.PendingVerification)
            {
                existingUser.Status = UserStatus.Active;
                await AuthSupport.RetirePendingOtpChallengesAsync(
                    _context,
                    existingUser.Id,
                    OtpPurpose.Register,
                    now,
                    cancellationToken);
            }
            else
            {
                AuthSupport.EnsureUserCanLogin(existingUser, nameof(request.IdToken), requireVerifiedPhone: false);
            }

            var result = await CreateLoggedInResultAsync(existingUser, payload, now, cancellationToken);
            if (shouldSendFirstGoogleLoginEmail)
            {
                await SendFirstGoogleLoginEmailAsync(existingUser, now, cancellationToken);
            }

            return result;
        }

        return await CreateNewGoogleUserLoggedInResultAsync(payload, normalizedEmail, now, cancellationToken);
    }

    private async Task<GoogleJsonWebSignature.Payload> ValidateGoogleTokenAsync(string idToken)
    {
        try
        {
            return await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = _googleClientIds.ToList()
                });
        }
        catch (InvalidJwtException ex)
        {
            throw new UnauthorizedAccessException($"Google token is invalid: {ex.Message}");
        }
    }

    private static IReadOnlyCollection<string> LoadGoogleClientIds(IConfiguration configuration)
    {
        var configuredClientIds = configuration
            .GetSection("OAuth:Google:ClientIds")
            .Get<string[]>()
            ?? [];

        var legacyClientId = configuration["OAuth:Google:ClientId"];
        var clientIds = configuredClientIds
            .Append(legacyClientId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return clientIds.Count > 0
            ? clientIds
            : throw new InvalidOperationException("Google ClientId not configured");
    }

    private async Task<GoogleLoginResultDto> CreateNewGoogleUserLoggedInResultAsync(
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
            FullName = string.IsNullOrWhiteSpace(payload.Name) ? "Google User" : payload.Name.Trim(),
            Email = payload.Email,
            NormalizedEmail = normalizedEmail,
            RoleId = customerRole.Id,
            Status = UserStatus.Active
        };

        _context.Set<User>().Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        var result = await CreateLoggedInResultAsync(user, payload, now, cancellationToken);
        await SendFirstGoogleLoginEmailAsync(user, now, cancellationToken);
        return result;
    }

    private async Task SendFirstGoogleLoginEmailAsync(
        User user,
        DateTimeOffset loggedInAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        try
        {
            await _loginNotificationSender.SendLoginSucceededAsync(
                new LoginNotification(
                    user.Email,
                    user.FullName,
                    GoogleProvider,
                    loggedInAt,
                    DeviceInfo: "Google Sign-In"),
                cancellationToken);
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Google first-login email failed. UserId: {UserId}, Email: {Email}",
                user.Id,
                user.Email);
        }
    }

    private async Task<GoogleLoginResultDto> CreateLoggedInResultAsync(
        User user,
        GoogleJsonWebSignature.Payload payload,
        DateTimeOffset now,
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
            try
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
            catch (ProfileImageStorageException)
            {
            }
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

        return new GoogleLoginResultDto(
            LoggedInStatus,
            session.User,
            session.Tokens);
    }

}
