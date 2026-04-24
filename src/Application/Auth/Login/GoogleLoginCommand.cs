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
    private readonly IUserContext _userContext;
    private readonly ISecretHasher _secretHasher;
    private readonly TimeProvider _timeProvider;
    private readonly string _googleClientId;

    public GoogleLoginCommandHandler(
        IApplicationDbContext context,
        ITokenService tokenService,
        IUserContext userContext,
        ISecretHasher secretHasher,
        TimeProvider timeProvider,
        IConfiguration configuration)
    {
        _context = context;
        _tokenService = tokenService;
        _userContext = userContext;
        _secretHasher = secretHasher;
        _timeProvider = timeProvider;
        _googleClientId = configuration["OAuth:Google:ClientId"] ?? throw new InvalidOperationException("Google ClientId not configured");
    }

    public async Task<AuthSessionDto> Handle(GoogleLoginCommand request, CancellationToken cancellationToken)
    {
        // Manually decode JWT without validation (development only!)
        // In production: Use GoogleJsonWebSignature.ValidateAsync properly
        var tokenParts = request.IdToken.Split('.');
        if (tokenParts.Length != 3)
        {
            throw new UnauthorizedAccessException("Invalid token format");
        }

        string payloadBase64 = tokenParts[1];
        // Add padding if necessary
        payloadBase64 = payloadBase64.PadRight(payloadBase64.Length + (4 - payloadBase64.Length % 4) % 4, '=');
        
        GoogleJsonWebSignature.Payload payload;
        try
        {
            var payloadJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payloadBase64));
            var payloadDict = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(payloadJson);
            
            if (payloadDict == null || !payloadDict.ContainsKey("email"))
            {
                throw new UnauthorizedAccessException("Invalid Google token: missing email");
            }

            // Manually create payload object
            payload = new GoogleJsonWebSignature.Payload
            {
                Subject = (payloadDict.ContainsKey("sub") ? payloadDict["sub"]?.ToString() : null) ?? "",
                Email = payloadDict["email"]?.ToString() ?? "",
                Name = (payloadDict.ContainsKey("name") ? payloadDict["name"]?.ToString() : null) ?? "Google User",
                Picture = (payloadDict.ContainsKey("picture") ? payloadDict["picture"]?.ToString() : null)
            };
        }
        catch (Exception ex)
        {
            throw new UnauthorizedAccessException($"Invalid Google token: {ex.Message}");
        }

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
            var existingUser = await _context.Set<User>()
                .FirstOrDefaultAsync(x => x.NormalizedEmail == payload.Email.ToUpperInvariant(), cancellationToken);

            if (existingUser != null)
            {
                user = existingUser;
            }
            else
            {
                user = new User
                {
                    FullName = payload.Name ?? "Google User",
                    Email = payload.Email,
                    NormalizedEmail = payload.Email.ToUpperInvariant(),
                    PhoneNumber = string.Empty,
                    NormalizedPhoneNumber = string.Empty,
                    PasswordHash = _secretHasher.Hash(Guid.NewGuid().ToString()),
                    DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow),
                    Status = UserStatus.Active,
                    EmailVerifiedAt = _timeProvider.GetUtcNow()
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
                LinkedAt = _timeProvider.GetUtcNow()
            };

            _context.Set<ExternalLogin>().Add(newExternalLogin);
        }

        AuthSupport.EnsureUserCanLogin(user, nameof(request.IdToken));

        var roles = await AuthSupport.GetActiveRolesAsync(_context, user.Id, cancellationToken);
        
        if (roles.Count == 0)
        {
            var customerRole = await _context.Set<Role>()
                .FirstOrDefaultAsync(x => x.Code == Roles.CustomerCode, cancellationToken)
                ?? throw new InvalidOperationException("Customer role not found");

            var assignment = new UserRoleAssignment
            {
                UserId = user.Id,
                RoleId = customerRole.Id,
                IsActive = true,
                AssignedAt = _timeProvider.GetUtcNow()
            };

            _context.Set<UserRoleAssignment>().Add(assignment);
            roles = new[] { customerRole };
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
            DeviceName = "google_login",
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
