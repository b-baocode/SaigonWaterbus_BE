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
    private const string EfMigrationsProductVersion = "9.0.14";

    private static readonly SeedUser[] InternalUsers =
    [
        new(
            Roles.AdminCode,
            "AD0000001",
            "System Administrator",
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

    private static readonly ServiceSeed[] ServiceSeeds =
    [
        new(
            "WB",
            "Waterbus",
            "Dịch vụ tuyến cố định trên sông, phù hợp di chuyển hằng ngày giữa các bến.",
            BookingMode.SeatBased,
            1),
        new(
            "WS",
            "WaterSightseeing",
            "Dịch vụ tham quan, ngắm cảnh bằng tàu theo tour hoặc khung giờ cố định.",
            BookingMode.SeatTypeBased,
            2),
        new(
            "WT",
            "WaterTaxi",
            "Dịch vụ taxi đường thủy theo nhu cầu, linh hoạt điểm đón và điểm trả.",
            BookingMode.VesselRental,
            3)
    ];

    private static readonly SeatTypeSeed[] SeatTypeSeeds =
    [
        new("WB", "STANDARD", "Standard", 1),
        new("WS", "STANDARD", "Standard", 1),
        new("WS", "VIP", "VIP", 2),
        new("WS", "WINDOW", "Window", 3),
        new("WS", "UPPER_DECK", "Upper Deck", 4),
        new("WT", "STANDARD", "Standard", 1)
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
        var roles = await _context.Roles.ToListAsync();
        var roleByCode = roles.ToDictionary(x => x.Code);
        var roleBySystemName = roles.ToDictionary(x => x.SystemName);

        foreach (var definition in Roles.BuiltIn)
        {
            if (!roleBySystemName.TryGetValue(definition.SystemName, out var existingRole)
                && !roleByCode.TryGetValue(definition.Code, out existingRole))
            {
                existingRole = new Role
                {
                    Code = definition.Code,
                    SystemName = definition.SystemName,
                    DisplayName = definition.DisplayName
                };

                _context.Roles.Add(existingRole);
            }
            else
            {
                if (!string.Equals(existingRole.Code, definition.Code, StringComparison.Ordinal))
                {
                    existingRole.Code = definition.Code;
                }

                if (!string.Equals(existingRole.SystemName, definition.SystemName, StringComparison.Ordinal))
                {
                    existingRole.SystemName = definition.SystemName;
                }

                if (!string.Equals(existingRole.DisplayName, definition.DisplayName, StringComparison.Ordinal))
                {
                    existingRole.DisplayName = definition.DisplayName;
                }
            }

            roleByCode[definition.Code] = existingRole;
            roleBySystemName[definition.SystemName] = existingRole;
        }

        await _context.SaveChangesAsync();
        if (_databaseStartupSettings.SeedSampleData)
        {
            await WaterbusSeedData.SeedAsync(_context);
        }

        await SeedServicesAndSeatTypesAsync();
        await _context.SaveChangesAsync();

        if (!_databaseStartupSettings.SeedInternalUsers)
        {
            _logger.LogInformation("Skipping internal user seeding because Database:SeedInternalUsers is disabled.");
            return;
        }

        foreach (var definition in InternalUsers)
        {
            var role = roleByCode[definition.RoleCode];
            await SeedInternalUserAsync(definition, role);
        }

        await _context.SaveChangesAsync();
        await SyncUserCodeSequencesAsync();
    }

    private async Task SeedServicesAndSeatTypesAsync()
    {
        var serviceByCode = await _context.WaterbusServices
            .Include(x => x.SeatTypes)
            .ToDictionaryAsync(x => x.Code);

        foreach (var definition in ServiceSeeds)
        {
            if (!serviceByCode.TryGetValue(definition.Code, out var service))
            {
                service = new WaterbusService
                {
                    Code = definition.Code,
                    Name = definition.Name,
                    Description = definition.Description,
                    BookingMode = definition.BookingMode,
                    DisplayOrder = definition.DisplayOrder,
                    IsActive = true
                };

                _context.WaterbusServices.Add(service);
                serviceByCode[definition.Code] = service;
            }
            else
            {
                service.Name = definition.Name;
                service.Description = definition.Description;
                service.BookingMode = definition.BookingMode;
                service.DisplayOrder = definition.DisplayOrder;
                service.IsActive = true;
            }
        }

        foreach (var definition in SeatTypeSeeds)
        {
            var service = serviceByCode[definition.ServiceCode];
            var seatType = service.SeatTypes
                .SingleOrDefault(x => string.Equals(x.Code, definition.Code, StringComparison.OrdinalIgnoreCase));

            if (seatType is null)
            {
                service.SeatTypes.Add(new SeatType
                {
                    Code = definition.Code,
                    Name = definition.Name,
                    DisplayOrder = definition.DisplayOrder,
                    IsActive = true
                });
            }
            else
            {
                seatType.Name = definition.Name;
                seatType.DisplayOrder = definition.DisplayOrder;
                seatType.IsActive = true;
            }
        }
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

    public async Task BaselineExistingSchemaMigrationsAsync(CancellationToken cancellationToken = default)
    {
        await _context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" character varying(150) NOT NULL,
                "ProductVersion" character varying(32) NOT NULL,
                CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
            );
            """,
            cancellationToken);

        if (await HasInitialSchemaAsync(cancellationToken))
        {
            await MarkMigrationAppliedAsync("20260608100138_InitialCreate", cancellationToken);
        }

        if (await HasTransitOperationsSchemaAsync(cancellationToken))
        {
            await MarkMigrationAppliedAsync("20260610131015_AddTransitOperationsSchema", cancellationToken);
        }

        if (await HasTableAsync("stations", cancellationToken)
            && !await HasColumnAsync("stations", "phone_number", cancellationToken))
        {
            await MarkMigrationAppliedAsync("20260610153831_RemoveStationPhoneNumber", cancellationToken);
        }

        if (await HasTableAsync("custom_booking_requests", cancellationToken)
            && await HasTableAsync("custom_booking_quotes", cancellationToken))
        {
            await MarkMigrationAppliedAsync("20260610163151_AddCustomBookingRequests", cancellationToken);
        }
    }

    public async Task PrintSchemaDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var columnChecks = new (string Table, string Column)[]
        {
            ("roles", "Id"),
            ("roles", "Code"),
            ("users", "Id"),
            ("users", "RoleId"),
            ("stations", "station_id"),
            ("bookings", "booking_id"),
            ("custom_booking_requests", "custom_booking_request_id")
        };

        foreach (var (table, column) in columnChecks)
        {
            Console.WriteLine($"{table}.{column}={await GetColumnDataTypeAsync(table, column, cancellationToken)}");
        }

        var migrations = await GetAppliedMigrationsAsync(cancellationToken);
        Console.WriteLine("migrations=" + (migrations.Count == 0 ? "(none)" : string.Join(",", migrations)));
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
            WHERE u."Status" = 0
              AND EXISTS (
                    SELECT 1
                    FROM otp_challenges oc
                    WHERE oc."UserId" = u."Id"
                      AND oc."Purpose" = 1)
              AND NOT EXISTS (
                    SELECT 1
                    FROM otp_challenges oc
                    WHERE oc."UserId" = u."Id"
                      AND oc."Purpose" = 1
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
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail || x.UserCode == definition.UserCode);

        if (user is null)
        {
            user = new User
            {
                UserCode = definition.UserCode,
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

        if (string.IsNullOrWhiteSpace(user.UserCode))
        {
            user.UserCode = definition.UserCode;
        }

        if (string.IsNullOrWhiteSpace(user.FullName))
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

    private async Task<bool> HasInitialSchemaAsync(CancellationToken cancellationToken) =>
        await HasTableAsync("roles", cancellationToken)
        && await HasTableAsync("users", cancellationToken)
        && await HasTableAsync("waterbus_services", cancellationToken)
        && await HasSequenceAsync("user_code_ad_seq", cancellationToken)
        && await HasSequenceAsync("user_code_mg_seq", cancellationToken)
        && await HasSequenceAsync("user_code_cu_seq", cancellationToken)
        && await HasSequenceAsync("user_code_st_seq", cancellationToken);

    private async Task<bool> HasTransitOperationsSchemaAsync(CancellationToken cancellationToken) =>
        await HasTableAsync("stations", cancellationToken)
        && await HasTableAsync("routes", cancellationToken)
        && await HasTableAsync("trips", cancellationToken)
        && await HasTableAsync("bookings", cancellationToken)
        && await HasTableAsync("ticket_types", cancellationToken);

    private Task<int> MarkMigrationAppliedAsync(string migrationId, CancellationToken cancellationToken) =>
        _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
             VALUES ({migrationId}, {EfMigrationsProductVersion})
             ON CONFLICT ("MigrationId") DO NOTHING;
             """,
            cancellationToken);

    private async Task<bool> HasTableAsync(string tableName, CancellationToken cancellationToken)
    {
        await using var command = _context.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_name = @tableName
            );
            """;
        AddParameter(command, "tableName", tableName);
        return await ExecuteScalarBoolAsync(command, cancellationToken);
    }

    private async Task<bool> HasColumnAsync(string tableName, string columnName, CancellationToken cancellationToken)
    {
        await using var command = _context.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = @tableName
                  AND column_name = @columnName
            );
            """;
        AddParameter(command, "tableName", tableName);
        AddParameter(command, "columnName", columnName);
        return await ExecuteScalarBoolAsync(command, cancellationToken);
    }

    private async Task<bool> HasSequenceAsync(string sequenceName, CancellationToken cancellationToken)
    {
        await using var command = _context.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.sequences
                WHERE sequence_schema = 'public'
                  AND sequence_name = @sequenceName
            );
            """;
        AddParameter(command, "sequenceName", sequenceName);
        return await ExecuteScalarBoolAsync(command, cancellationToken);
    }

    private async Task<string> GetColumnDataTypeAsync(
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = _context.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            SELECT data_type
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = @tableName
              AND column_name = @columnName;
            """;
        AddParameter(command, "tableName", tableName);
        AddParameter(command, "columnName", columnName);
        return Convert.ToString(await ExecuteScalarAsync(command, cancellationToken)) ?? "(missing)";
    }

    private async Task<IReadOnlyList<string>> GetAppliedMigrationsAsync(CancellationToken cancellationToken)
    {
        if (!await HasTableAsync("__EFMigrationsHistory", cancellationToken))
        {
            return [];
        }

        await using var command = _context.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            SELECT "MigrationId"
            FROM "__EFMigrationsHistory"
            ORDER BY "MigrationId";
            """;

        var connection = command.Connection
            ?? throw new InvalidOperationException("Database command has no connection.");
        var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var migrations = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                migrations.Add(reader.GetString(0));
            }

            return migrations;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<bool> ExecuteScalarBoolAsync(
        System.Data.Common.DbCommand command,
        CancellationToken cancellationToken)
    {
        return Convert.ToBoolean(await ExecuteScalarAsync(command, cancellationToken));
    }

    private async Task<object?> ExecuteScalarAsync(
        System.Data.Common.DbCommand command,
        CancellationToken cancellationToken)
    {
        var connection = command.Connection
            ?? throw new InvalidOperationException("Database command has no connection.");
        var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            return await command.ExecuteScalarAsync(cancellationToken);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
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
        string UserCode,
        string FullName,
        string Email,
        string PhoneNumber,
        string Password);

    private sealed record ServiceSeed(
        string Code,
        string Name,
        string Description,
        BookingMode BookingMode,
        int DisplayOrder);

    private sealed record SeatTypeSeed(
        string ServiceCode,
        string Code,
        string Name,
        int DisplayOrder);
}
