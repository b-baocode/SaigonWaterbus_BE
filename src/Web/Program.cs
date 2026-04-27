using SaigonWaterbus.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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

    Console.WriteLine(
        $"db:migrate-seed completed. roles={rolesCount}, users={usersCount}, otp_challenges={otpCount}, refresh_tokens={refreshTokenCount}, external_logins={externalLoginCount}");

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

    Console.WriteLine(
        $"db:reset-seed completed. roles={rolesCount}, users={usersCount}, otp_challenges={otpCount}, refresh_tokens={refreshTokenCount}, external_logins={externalLoginCount}");

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

    Console.WriteLine(
        $"db:clear completed. roles={rolesCount}, users={usersCount}, otp_challenges={otpCount}, refresh_tokens={refreshTokenCount}, external_logins={externalLoginCount}");

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
