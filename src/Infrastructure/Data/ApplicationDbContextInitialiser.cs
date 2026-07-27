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
    private const string LegacyAdminFullName = "System Administrator";

    private static readonly (string OldMigrationId, string CurrentMigrationId)[] RenamedMigrationIds =
    [
        ("20260624113047_AddCustomBookings", "20260624113047_AddCharterBookings"),
        ("20260624233018_AddCustomBookingCustomerRequirements", "20260624233018_AddCharterBookingCustomerRequirements"),
        ("20260625023824_AddCustomBookingHold", "20260625023824_AddCharterBookingHold"),
        ("20260626175903_CollapseCustomBookingIntoBooking", "20260626175903_CollapseCharterBookingIntoBooking"),
        ("20260703134524_AddCustomBookingQrToken", "20260703134524_AddCharterBookingQrToken")
    ];

    private static readonly SeedUser[] InternalUsers =
    [
        new(
            Roles.AdminCode,
            "AD0000001",
            "Admin",
            "admin@saigonwaterbus.local",
            "0900000001",
            "Admin@123")
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

            if (!_databaseStartupSettings.ApplyMigrationsOnStartup)
            {
                await _context.Database.CanConnectAsync();
                return;
            }

            if (_context.Database.GetMigrations().Any())
            {
                await MarkRenamedMigrationsAppliedAsync();
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

        foreach (var definition in Roles.BuiltIn)
        {
            if (!roleByCode.TryGetValue(definition.Code, out var existingRole))
            {
                existingRole = new Role
                {
                    Code = definition.Code,
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

                if (!string.Equals(existingRole.DisplayName, definition.DisplayName, StringComparison.Ordinal))
                {
                    existingRole.DisplayName = definition.DisplayName;
                }
            }

            roleByCode[definition.Code] = existingRole;
        }

        await _context.SaveChangesAsync();

        await SeedSeatTypesAsync();
        await SeedInsurancePackagesAsync();

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
    }

    private static readonly (string Code, string Name, decimal BasePrice, int DisplayOrder)[] SeatTypeDefinitions =
    [
        ("STANDARD", "Standard", 7_000m, 1),
        ("CABIN", "Cabin", 10_000m, 2),
        ("RIVER", "River", 12_000m, 3),
        ("SKY", "Sky", 15_000m, 4)
    ];

    private static readonly InsurancePackageSeed[] InsurancePackageDefinitions =
    [
        new(
            "CHARTER_PASSENGER_BASIC",
            "Bảo hiểm hành khách thuê tàu",
            Booking.CharterBookingType,
            false,
            "Bảo hiểm mặc định",
            null,
            10_000m,
            50_000_000m,
            "VND",
            [
                "Chỉ áp dụng cho hành khách có tên trong danh sách chuyến đi.",
                "Chỉ có hiệu lực trong thời gian diễn ra chuyến thuê tàu.",
                "Không áp dụng nếu thông tin hành khách sai hoặc không đầy đủ."
            ],
            null,
            1)
    ];

    /// <summary>
    /// Seed danh mục loại ghế vào bảng seat_types (nếu thiếu) và gắn lại seat_type_id
    /// cho các ghế được tạo trước khi bảng này có dữ liệu. Không ghi đè giá admin đã chỉnh.
    /// </summary>
    private async Task SeedSeatTypesAsync()
    {
        var existingByCode = await _context.Set<SeatType>()
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);

        var added = false;
        foreach (var definition in SeatTypeDefinitions)
        {
            if (existingByCode.ContainsKey(definition.Code))
            {
                continue;
            }

            _context.Set<SeatType>().Add(new SeatType
            {
                Code = definition.Code,
                Name = definition.Name,
                BasePrice = definition.BasePrice,
                Currency = "VND",
                DisplayOrder = definition.DisplayOrder
            });
            added = true;
        }

        if (added)
        {
            await _context.SaveChangesAsync();
        }

        var seatTypeIdByCode = await _context.Set<SeatType>()
            .ToDictionaryAsync(x => x.Code, x => x.Id, StringComparer.OrdinalIgnoreCase);
        var unlinkedSeats = await _context.Set<Seat>()
            .Where(x => x.SeatTypeId == null)
            .ToListAsync();
        var linked = 0;
        foreach (var seat in unlinkedSeats)
        {
            if (seatTypeIdByCode.TryGetValue(seat.SeatTypeCode, out var seatTypeId))
            {
                seat.SeatTypeId = seatTypeId;
                linked++;
            }
        }

        if (linked > 0)
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Linked {LinkedSeatCount} seats to seeded seat types.", linked);
        }
    }

    private async Task SeedInsurancePackagesAsync()
    {
        var existingKeys = await _context.Set<InsurancePackage>()
            .Select(x => new { x.BookingType, x.Code })
            .ToListAsync();
        var existingKeySet = existingKeys
            .Select(x => $"{x.BookingType}:{x.Code}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = false;
        foreach (var definition in InsurancePackageDefinitions)
        {
            var key = $"{definition.BookingType}:{definition.Code}";
            if (existingKeySet.Contains(key))
            {
                continue;
            }

            _context.Set<InsurancePackage>().Add(new InsurancePackage
            {
                Code = definition.Code,
                Name = definition.Name,
                BookingType = definition.BookingType,
                IsRequired = definition.IsRequired,
                ProviderName = definition.ProviderName,
                ProviderLogoUrl = definition.ProviderLogoUrl,
                UnitPremiumAmount = definition.UnitPremiumAmount,
                CoverageAmount = definition.CoverageAmount,
                Currency = definition.Currency,
                Conditions = definition.Conditions,
                TermsUrl = definition.TermsUrl,
                IsActive = true,
                DisplayOrder = definition.DisplayOrder
            });
            added = true;
        }

        if (added)
        {
            await _context.SaveChangesAsync();
        }
    }

    public async Task ResetAndSeedSampleDataAsync()
    {
        await ClearDataForRetestAsync();
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

        if (await HasTableAsync("charter_booking_requests", cancellationToken)
            && await HasTableAsync("charter_booking_quotes", cancellationToken))
        {
            await MarkMigrationAppliedAsync("20260610163151_AddCharterBookingRequests", cancellationToken);
        }
    }

    public async Task PrintSchemaDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var columnChecks = new (string Table, string Column)[]
        {
            ("roles", "role_id"),
            ("roles", "role_code"),
            ("users", "user_id"),
            ("users", "role_id"),
            ("stations", "station_id"),
            ("bookings", "booking_id")
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
        await _context.Users.ExecuteDeleteAsync();

        await SeedAsync();
    }

    public Task<int> CountExpiredPendingRegistrationUsersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Task<int> CleanupExpiredPendingRegistrationUsersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Task ConfigurePendingRegistrationCleanupCronAsync(
        string cronExpression,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(cronExpression);

        return Task.CompletedTask;
    }

    private async Task SeedInternalUserAsync(SeedUser definition, Role role)
    {
        var normalizedEmail = _identityNormalizer.NormalizeEmail(definition.Email);
        var normalizedPhone = _identityNormalizer.NormalizePhone(definition.PhoneNumber);

        var user = await _context.Users
            .SingleOrDefaultAsync(x => (x.Email != null && x.Email.ToUpper() == normalizedEmail)
                                    || x.UserCode == definition.UserCode);

        if (user is null)
        {
            user = new User
            {
                UserCode = definition.UserCode,
                FullName = definition.FullName,
                Email = definition.Email,
                NormalizedEmail = normalizedEmail,
                PhoneNumber = normalizedPhone,
                NormalizedPhoneNumber = normalizedPhone,
                PasswordHash = _secretHasher.Hash(definition.Password),
                RoleId = role.Id,
                Role = role,
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

        if (string.IsNullOrWhiteSpace(user.FullName)
            || (string.Equals(user.UserCode, definition.UserCode, StringComparison.Ordinal)
                && string.Equals(user.FullName, LegacyAdminFullName, StringComparison.Ordinal)))
        {
            user.FullName = definition.FullName;
        }

        if (string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            user.PhoneNumber = normalizedPhone;
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
        await Task.CompletedTask;
    }

    private async Task MarkRenamedMigrationsAppliedAsync(CancellationToken cancellationToken = default)
    {
        if (!await _context.Database.CanConnectAsync(cancellationToken)
            || !await HasTableAsync("__EFMigrationsHistory", cancellationToken))
        {
            return;
        }

        var appliedMigrations = (await GetAppliedMigrationsAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (oldMigrationId, currentMigrationId) in RenamedMigrationIds)
        {
            if (!appliedMigrations.Contains(oldMigrationId)
                || appliedMigrations.Contains(currentMigrationId))
            {
                continue;
            }

            await MarkMigrationAppliedAsync(currentMigrationId, cancellationToken);
            appliedMigrations.Add(currentMigrationId);

            _logger.LogInformation(
                "Marked renamed EF migration {CurrentMigrationId} as applied because {OldMigrationId} is already applied.",
                currentMigrationId,
                oldMigrationId);
        }
    }

    private async Task<bool> HasInitialSchemaAsync(CancellationToken cancellationToken) =>
        await HasTableAsync("roles", cancellationToken)
        && await HasTableAsync("users", cancellationToken)
        && await HasSequenceAsync("user_code_ad_seq", cancellationToken)
        && await HasSequenceAsync("user_code_mg_seq", cancellationToken)
        && await HasSequenceAsync("user_code_cu_seq", cancellationToken)
        && await HasSequenceAsync("user_code_st_seq", cancellationToken);

    private async Task<bool> HasTransitOperationsSchemaAsync(CancellationToken cancellationToken) =>
        await HasTableAsync("stations", cancellationToken)
        && await HasTableAsync("routes", cancellationToken)
        && await HasTableAsync("trips", cancellationToken)
        && await HasTableAsync("bookings", cancellationToken);

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
        return _context.Users.Where(x => false);
    }

    private sealed record SeedUser(
        string RoleCode,
        string UserCode,
        string FullName,
        string Email,
        string PhoneNumber,
        string Password);

    private sealed record InsurancePackageSeed(
        string Code,
        string Name,
        string BookingType,
        bool IsRequired,
        string? ProviderName,
        string? ProviderLogoUrl,
        decimal UnitPremiumAmount,
        decimal CoverageAmount,
        string Currency,
        string[] Conditions,
        string? TermsUrl,
        int DisplayOrder);
}
