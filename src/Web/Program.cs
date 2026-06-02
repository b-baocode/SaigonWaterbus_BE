using SaigonWaterbus.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Add services to the container.
builder.AddKeyVaultIfConfigured();
builder.AddApplicationServices();
builder.AddInfrastructureServices();
builder.AddWebServices();

var app = builder.Build();

if (args.Contains("db:migrate-seed", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    await initialiser.InitialiseAsync();
    await initialiser.SeedAsync();

    var rolesCount = await dbContext.Roles.CountAsync();
    var usersCount = await dbContext.Users.CountAsync();
    var otpCount = await dbContext.OtpChallenges.CountAsync();
    var refreshTokenCount = await dbContext.RefreshTokens.CountAsync();
    var externalLoginCount = await dbContext.ExternalLogins.CountAsync();
    var auditLogCount = await dbContext.AuditLogs.CountAsync();

    Console.WriteLine(
        $"db:migrate-seed completed. roles={rolesCount}, users={usersCount}, otp_challenges={otpCount}, refresh_tokens={refreshTokenCount}, external_logins={externalLoginCount}, audit_logs={auditLogCount}");

    return;
}

if (args.Contains("db:reset-seed", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    await initialiser.InitialiseAsync();
    await initialiser.ResetAndSeedSampleDataAsync();

    var rolesCount = await dbContext.Roles.CountAsync();
    var usersCount = await dbContext.Users.CountAsync();
    var otpCount = await dbContext.OtpChallenges.CountAsync();
    var refreshTokenCount = await dbContext.RefreshTokens.CountAsync();
    var externalLoginCount = await dbContext.ExternalLogins.CountAsync();
    var auditLogCount = await dbContext.AuditLogs.CountAsync();

    Console.WriteLine(
        $"db:reset-seed completed. roles={rolesCount}, users={usersCount}, otp_challenges={otpCount}, refresh_tokens={refreshTokenCount}, external_logins={externalLoginCount}, audit_logs={auditLogCount}");

    return;
}

if (args.Contains("db:clear", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    await initialiser.InitialiseAsync();
    await initialiser.ClearDataForRetestAsync();

    var rolesCount = await dbContext.Roles.CountAsync();
    var usersCount = await dbContext.Users.CountAsync();
    var otpCount = await dbContext.OtpChallenges.CountAsync();
    var refreshTokenCount = await dbContext.RefreshTokens.CountAsync();
    var externalLoginCount = await dbContext.ExternalLogins.CountAsync();
    var auditLogCount = await dbContext.AuditLogs.CountAsync();

    Console.WriteLine(
        $"db:clear completed. roles={rolesCount}, users={usersCount}, otp_challenges={otpCount}, refresh_tokens={refreshTokenCount}, external_logins={externalLoginCount}, audit_logs={auditLogCount}");

    return;
}

if (args.Contains("db:setup-expiry-cleanup-cron", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();
    var cronExpression = app.Configuration["Database:PendingRegistrationCleanupCron"] ?? "*/1 * * * *";

    try
    {
        await initialiser.InitialiseAsync();
        await initialiser.ConfigurePendingRegistrationCleanupCronAsync(cronExpression);
        var expiredPendingCount = await initialiser.CountExpiredPendingRegistrationUsersAsync();

        Console.WriteLine(
            $"db:setup-expiry-cleanup-cron completed. job={ApplicationDbContextInitialiser.PendingRegistrationCleanupCronJobName}, cron={cronExpression}, expired_pending_users={expiredPendingCount}");
    }
    catch (PostgresException ex) when (ex.SqlState == "0A000"
                                       && string.Equals(ex.MessageText, "extension \"pg_cron\" is not available", StringComparison.Ordinal))
    {
        Console.Error.WriteLine(
            "db:setup-expiry-cleanup-cron failed: PostgreSQL server chưa cài pg_cron. " +
            "Cài extension ở DB server, bật shared_preload_libraries='pg_cron', restart PostgreSQL, rồi chạy lại lệnh này.");
        Environment.ExitCode = 1;
    }

    return;
}

if (args.Contains("db:count-expired-pending", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();

    await initialiser.InitialiseAsync();
    var expiredPendingCount = await initialiser.CountExpiredPendingRegistrationUsersAsync();

    Console.WriteLine($"db:count-expired-pending completed. expired_pending_users={expiredPendingCount}");

    return;
}

if (args.Contains("db:cleanup-expired-pending-now", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();

    await initialiser.InitialiseAsync();
    var before = await initialiser.CountExpiredPendingRegistrationUsersAsync();
    var removed = await initialiser.CleanupExpiredPendingRegistrationUsersAsync();
    var after = await initialiser.CountExpiredPendingRegistrationUsersAsync();

    Console.WriteLine(
        $"db:cleanup-expired-pending-now completed. before={before}, removed={removed}, after={after}");

    return;
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    await app.InitialiseDatabaseAsync();
}
else
{ 
    app.UseHsts();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors("FrontendClientPolicy");
app.UseAuthentication();
app.UseAuthorization();

app.UseFileServer();

app.MapOpenApi();
app.UseSwagger();
app.UseSwaggerUI();

app.Map("/", () => Results.Redirect("/swagger"));

app.MapEndpoints(typeof(Program).Assembly);


app.Run();

public partial class Program;
