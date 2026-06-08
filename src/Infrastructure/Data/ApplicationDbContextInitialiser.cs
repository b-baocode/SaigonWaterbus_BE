using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Options;

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
    public const string PendingRegistrationCleanupCronJobName = "cleanup-expired-pending-users";

    private static readonly SeedUser[] InternalUsers =
    [
        new(
            Roles.AdminCode,
            "AD0000001",
            "Admin",
            "admin@saigonwaterbus.local",
            "0900000001",
            "Admin@123"),
        new(
            Roles.ManagerCode,
            "MG0000001",
            "Operations Manager",
            "manager@saigonwaterbus.local",
            "0900000002",
            "Manager@123")
    ];

    private readonly ILogger<ApplicationDbContextInitialiser> _logger;
    private readonly ApplicationDbContext _context;
    private readonly DatabaseStartupSettings _databaseStartupSettings;
    private readonly IIdentityNormalizer _identityNormalizer;
    private readonly ISecretHasher _secretHasher;

    public ApplicationDbContextInitialiser(
        ILogger<ApplicationDbContextInitialiser> logger,
        ApplicationDbContext context,
        IOptions<DatabaseStartupSettings> databaseStartupSettings,
        IIdentityNormalizer identityNormalizer,
        ISecretHasher secretHasher)
    {
        _logger = logger;
        _context = context;
        _databaseStartupSettings = databaseStartupSettings.Value;
        _identityNormalizer = identityNormalizer;
        _secretHasher = secretHasher;
    }

    public async Task InitialiseAsync()
    {
        try
        {
            if (_databaseStartupSettings.ResetOnStartup)
            {
                if (await _context.Database.CanConnectAsync())
                {
                    _logger.LogWarning("Database reset is enabled. Deleting database before applying migrations.");
                    await _context.Database.EnsureDeletedAsync();
                }
                else
                {
                    _logger.LogInformation("Database reset is enabled, but the database does not exist yet.");
                }
            }

            // Apply migrations when available. For initial setup without migrations,
            // create schema without destroying existing data.
            if (_context.Database.GetMigrations().Any())
            {
                await _context.Database.MigrateAsync();
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
            _logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }

    public async Task TrySeedAsync()
    {
        var roleByCode = await _context.Roles
            .ToDictionaryAsync(x => x.Code);

        foreach (var definition in Roles.BuiltIn)
        {
            if (!roleByCode.TryGetValue(definition.Code, out var existingRole))
            {
                existingRole = new Role
                {
                    Code = definition.Code,
                    SystemName = definition.SystemName,
                    DisplayName = definition.DisplayName
                };

                _context.Roles.Add(existingRole);
                roleByCode[definition.Code] = existingRole;
            }
            else
            {
                if (!string.Equals(existingRole.SystemName, definition.SystemName, StringComparison.Ordinal))
                {
                    existingRole.SystemName = definition.SystemName;
                }

                if (!string.Equals(existingRole.DisplayName, definition.DisplayName, StringComparison.Ordinal))
                {
                    existingRole.DisplayName = definition.DisplayName;
                }
            }
        }

        await _context.SaveChangesAsync();

        if (_databaseStartupSettings.SeedInternalUsers)
        {
            foreach (var definition in InternalUsers)
            {
                var role = roleByCode[definition.RoleCode];
                await SeedInternalUserAsync(definition, role);
            }

            await _context.SaveChangesAsync();
        }
        else
        {
            _logger.LogInformation("Skipping internal user seeding because Database:SeedInternalUsers is disabled.");
        }

        await SyncUserCodeSequencesAsync();
    }

    public async Task ResetAndSeedSampleDataAsync()
    {
        if (await _context.Database.CanConnectAsync())
        {
            await _context.Database.EnsureDeletedAsync();
        }

        if (_context.Database.GetMigrations().Any())
        {
            await _context.Database.MigrateAsync();
        }
        else
        {
            await _context.Database.EnsureCreatedAsync();
        }

        await SeedAsync();
    }

    public async Task ClearDataForRetestAsync()
    {
        await _context.ExternalLogins.ExecuteDeleteAsync();
        await _context.OtpChallenges.ExecuteDeleteAsync();
        await _context.RefreshTokens.ExecuteDeleteAsync();
        await _context.AuditLogs.ExecuteDeleteAsync();
        await _context.Users.ExecuteDeleteAsync();

        await SeedAsync();
    }

    public Task<int> CountExpiredPendingRegistrationUsersAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return GetExpiredPendingRegistrationUsersQuery(now).CountAsync(cancellationToken);
    }

    public async Task<int> CleanupExpiredPendingRegistrationUsersAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await GetExpiredPendingRegistrationUsersQuery(now).ExecuteDeleteAsync(cancellationToken);
    }

    public Task ConfigurePendingRegistrationCleanupCronAsync(
        string cronExpression,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(cronExpression);

        const string cleanupCommand =
            """
            DELETE FROM users u
            WHERE u."Status" = 'PendingVerification'
              AND EXISTS (
                    SELECT 1
                    FROM otp_challenges oc
                    WHERE oc."UserId" = u."Id"
                      AND oc."Purpose" = 'Register')
              AND NOT EXISTS (
                    SELECT 1
                    FROM otp_challenges oc
                    WHERE oc."UserId" = u."Id"
                      AND oc."Purpose" = 'Register'
                      AND oc."ExpiresAt" > now());
            """;

        return _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             CREATE EXTENSION IF NOT EXISTS pg_cron;

             SELECT cron.unschedule(jobid)
             FROM cron.job
             WHERE jobname = {PendingRegistrationCleanupCronJobName};

             SELECT cron.schedule(
                 {PendingRegistrationCleanupCronJobName},
                 {cronExpression},
                 {cleanupCommand});
             """,
            cancellationToken);
    }

    private async Task SeedInternalUserAsync(SeedUser definition, Role role)
    {
        var normalizedEmail = _identityNormalizer.NormalizeEmail(definition.Email);
        var normalizedPhone = _identityNormalizer.NormalizePhone(definition.PhoneNumber);

        var user = await _context.Users
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail || x.Code == definition.Code);

        if (user is null)
        {
            user = new User
            {
                Code = definition.Code,
                FullName = definition.FullName,
                Email = definition.Email,
                NormalizedEmail = normalizedEmail,
                PhoneNumber = definition.PhoneNumber,
                NormalizedPhoneNumber = normalizedPhone,
                PasswordHash = _secretHasher.Hash(definition.Password),
                RoleId = role.Id,
                Status = UserStatus.Active,
                PhoneVerifiedAt = DateTimeOffset.UtcNow
            };
            _context.Users.Add(user);
            return;
        }

        if (string.IsNullOrWhiteSpace(user.Code))
        {
            user.Code = definition.Code;
        }

        if (string.IsNullOrWhiteSpace(user.FullName)
            || string.Equals(user.FullName, "System Administrator", StringComparison.Ordinal))
        {
            user.FullName = definition.FullName;
        }

        if (string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            user.PhoneNumber = definition.PhoneNumber;
        }

        if (string.IsNullOrWhiteSpace(user.NormalizedPhoneNumber))
        {
            user.NormalizedPhoneNumber = normalizedPhone;
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            user.Email = definition.Email;
        }

        if (string.IsNullOrWhiteSpace(user.NormalizedEmail))
        {
            user.NormalizedEmail = normalizedEmail;
        }

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            user.PasswordHash = _secretHasher.Hash(definition.Password);
        }

        user.RoleId = role.Id;

        user.Status = UserStatus.Active;
        user.PhoneVerifiedAt ??= DateTimeOffset.UtcNow;
    }

    private async Task SyncUserCodeSequencesAsync()
    {
        await _context.Database.ExecuteSqlRawAsync(
            """
            CREATE SEQUENCE IF NOT EXISTS user_code_cu_seq AS integer START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 9999999 NO CYCLE;
            CREATE SEQUENCE IF NOT EXISTS user_code_mg_seq AS integer START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 9999999 NO CYCLE;
            CREATE SEQUENCE IF NOT EXISTS user_code_ad_seq AS integer START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 9999999 NO CYCLE;
            CREATE SEQUENCE IF NOT EXISTS user_code_st_seq AS integer START WITH 1 INCREMENT BY 1 MINVALUE 1 MAXVALUE 9999999 NO CYCLE;

            SELECT setval('user_code_ad_seq',
                GREATEST(COALESCE((SELECT MAX(SUBSTRING("Code" FROM 3)::integer) FROM users WHERE "Code" LIKE 'AD%'), 0), 1),
                COALESCE((SELECT MAX(SUBSTRING("Code" FROM 3)::integer) FROM users WHERE "Code" LIKE 'AD%'), 0) > 0);

            SELECT setval('user_code_mg_seq',
                GREATEST(COALESCE((SELECT MAX(SUBSTRING("Code" FROM 3)::integer) FROM users WHERE "Code" LIKE 'MG%'), 0), 1),
                COALESCE((SELECT MAX(SUBSTRING("Code" FROM 3)::integer) FROM users WHERE "Code" LIKE 'MG%'), 0) > 0);

            SELECT setval('user_code_cu_seq',
                GREATEST(COALESCE((SELECT MAX(SUBSTRING("Code" FROM 3)::integer) FROM users WHERE "Code" LIKE 'CU%'), 0), 1),
                COALESCE((SELECT MAX(SUBSTRING("Code" FROM 3)::integer) FROM users WHERE "Code" LIKE 'CU%'), 0) > 0);

            SELECT setval('user_code_st_seq',
                GREATEST(COALESCE((SELECT MAX(SUBSTRING("Code" FROM 3)::integer) FROM users WHERE "Code" LIKE 'ST%'), 0), 1),
                COALESCE((SELECT MAX(SUBSTRING("Code" FROM 3)::integer) FROM users WHERE "Code" LIKE 'ST%'), 0) > 0);
            """);
    }

    private IQueryable<User> GetExpiredPendingRegistrationUsersQuery(DateTimeOffset now)
    {
        return _context.Users
            .Where(x => x.Status == UserStatus.PendingVerification
                     && x.OtpChallenges.Any(otp => otp.Purpose == OtpPurpose.Register)
                     && !x.OtpChallenges.Any(otp => otp.Purpose == OtpPurpose.Register
                                                 && otp.ExpiresAt > now));
    }

    private sealed record SeedUser(
        string RoleCode,
        string Code,
        string FullName,
        string Email,
        string PhoneNumber,
        string Password);
}
