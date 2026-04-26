using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Infrastructure.Data;

public static class InitialiserExtensions
{
    public static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();

        await initialiser.InitialiseAsync();
        await initialiser.SeedAsync();
    }
}

public class ApplicationDbContextInitialiser
{
    private readonly ILogger<ApplicationDbContextInitialiser> _logger;
    private readonly ApplicationDbContext _context;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly ISecretHasher _secretHasher;
    private readonly TimeProvider _timeProvider;

    public ApplicationDbContextInitialiser(
        ILogger<ApplicationDbContextInitialiser> logger,
        ApplicationDbContext context,
        IIdentityNormalizer identityNormalizer,
        ISecretHasher secretHasher,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _context = context;
        _identityNormalizer = identityNormalizer;
        _secretHasher = secretHasher;
        _timeProvider = timeProvider;
    }

    public async Task InitialiseAsync()
    {
        try
        {
            if (_context.Database.GetMigrations().Any())
            {
                try
                {
                    await _context.Database.MigrateAsync();
                }
                catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P07")
                {
                    _logger.LogWarning("Database tables already exist. Skipping migration.");
                }
            }
            else
            {
                await _context.Database.EnsureCreatedAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while initialising the database.");
            throw;
        }
    }

    public async Task SeedAsync()
    {
        try
        {
            await TrySeedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "An error occurred while seeding the database. This may indicate a schema mismatch. Consider resetting the database.");
        }
    }

    public async Task TrySeedAsync()
    {
        var existingRoleCodes = await _context.Roles
            .Select(x => x.Code)
            .ToListAsync();

        var missingRoles = Roles.BuiltIn
            .Where(x => !existingRoleCodes.Contains(x.Code))
            .Select(x => new Role
            {
                Code = x.Code,
                SystemName = x.SystemName,
                DisplayName = x.DisplayName
            })
            .ToList();

        if (missingRoles.Count == 0)
        {
            return;
        }

        _context.Roles.AddRange(missingRoles);
        await _context.SaveChangesAsync();
    }

    public async Task ResetAndSeedSampleDataAsync()
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();

        await _context.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE external_logins, otp_challenges, refresh_tokens, users, roles RESTART IDENTITY CASCADE;
            """);

        await TrySeedAsync();

        var adminRole = await _context.Roles
            .SingleAsync(x => x.Code == Roles.AdminSystemCode);

        var now = _timeProvider.GetUtcNow();
        const string seedEmail = "admin.seed@saigonwaterbus.local";
        const string seedPhone = "0900000001";

        var user = new User
        {
            UserCode = "AD0000001",
            FullName = "Seed Admin",
            DateOfBirth = new DateOnly(1995, 1, 1),
            PhoneNumber = seedPhone,
            NormalizedPhoneNumber = _identityNormalizer.NormalizePhone(seedPhone),
            Email = seedEmail,
            NormalizedEmail = _identityNormalizer.NormalizeEmail(seedEmail),
            PasswordHash = _secretHasher.Hash("P@ssw0rd!"),
            RoleId = adminRole.Id,
            Department = "Operations",
            Status = UserStatus.Active,
            EmailVerifiedAt = now,
            LastLoginAt = now
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var otpChallenge = new OtpChallenge
        {
            UserId = user.Id,
            Purpose = OtpPurpose.ForgotPassword,
            Email = seedEmail,
            CodeHash = _secretHasher.Hash("123456"),
            ExpiresAt = now.AddMinutes(5),
            ResendAvailableAt = now.AddSeconds(60),
            AttemptCount = 0,
            MaxAttempts = 5
        };

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _secretHasher.Hash("seed-refresh-token"),
            ExpiresAt = now.AddDays(30)
        };

        var externalLogin = new ExternalLogin
        {
            UserId = user.Id,
            Provider = "google",
            ProviderUserId = "google-seed-admin-1001",
            Email = seedEmail,
            DisplayName = user.FullName,
            ProfilePictureUrl = "https://example.com/avatar/seed-admin.png",
            LinkedAt = now
        };

        _context.OtpChallenges.Add(otpChallenge);
        _context.RefreshTokens.Add(refreshToken);
        _context.ExternalLogins.Add(externalLogin);

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task ClearDataForRetestAsync()
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();

        await _context.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE external_logins, otp_challenges, refresh_tokens, users, roles RESTART IDENTITY CASCADE;
            """);

        await TrySeedAsync();
        await transaction.CommitAsync();
    }
}
