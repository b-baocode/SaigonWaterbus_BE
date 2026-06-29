using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Auth.Login;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Auth;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Auth;

public class LoginRequestUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ExecuteLocksAccountAfterFiveFailedPasswordAttemptsWithinFifteenMinutes()
    {
        await using var context = CreateContext();
        await SeedUserAsync(context);
        var useCase = CreateUseCase(context, Now);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await Should.ThrowAsync<ValidationException>(() => useCase.ExecuteAsync(CreateLogin("WrongP@ssword"), CancellationToken.None));

            var user = await context.Users.SingleAsync();
            user.Status.ShouldBe(UserStatus.Active);
            user.FailedLoginAttemptCount.ShouldBe(attempt);
            user.FailedLoginWindowStartedAt.ShouldBe(Now);
        }

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            useCase.ExecuteAsync(CreateLogin("WrongP@ssword"), CancellationToken.None));

        var lockedUser = await context.Users.SingleAsync();
        lockedUser.Status.ShouldBe(UserStatus.Suspended);
        lockedUser.FailedLoginAttemptCount.ShouldBe(5);
        exception.Errors.ShouldContain(x => x.Key == "emailOrPhone"
            && x.Value.Contains("Tài khoản đã bị khóa do đăng nhập sai 5 lần trong 15 phút. Vui lòng liên hệ Admin để mở lại."));
    }

    [Test]
    public async Task ExecuteResetsFailedPasswordWindowAfterFifteenMinutes()
    {
        await using var context = CreateContext();
        var user = await SeedUserAsync(context);
        user.FailedLoginAttemptCount = 4;
        user.FailedLoginWindowStartedAt = Now.AddMinutes(-16);
        await context.SaveChangesAsync();

        var useCase = CreateUseCase(context, Now);

        await Should.ThrowAsync<ValidationException>(() => useCase.ExecuteAsync(CreateLogin("WrongP@ssword"), CancellationToken.None));

        var updatedUser = await context.Users.SingleAsync();
        updatedUser.Status.ShouldBe(UserStatus.Active);
        updatedUser.FailedLoginAttemptCount.ShouldBe(1);
        updatedUser.FailedLoginWindowStartedAt.ShouldBe(Now);
    }

    [Test]
    public async Task ExecuteResetsFailedPasswordTrackingOnSuccessfulLogin()
    {
        await using var context = CreateContext();
        var user = await SeedUserAsync(context);
        user.FailedLoginAttemptCount = 3;
        user.FailedLoginWindowStartedAt = Now.AddMinutes(-5);
        await context.SaveChangesAsync();

        var useCase = CreateUseCase(context, Now);

        var result = await useCase.ExecuteAsync(CreateLogin("P@ssword123"), CancellationToken.None);

        result.Tokens.AccessToken.ShouldBe("access-token");
        var updatedUser = await context.Users.SingleAsync();
        updatedUser.FailedLoginAttemptCount.ShouldBe(0);
        updatedUser.FailedLoginWindowStartedAt.ShouldBeNull();
        updatedUser.LastLoginAt.ShouldBe(Now);
        var refreshTokenCount = await context.RefreshTokens.CountAsync();
        refreshTokenCount.ShouldBe(1);
    }

    private static LoginRequest CreateLogin(string password) =>
        new("customer@gmail.com", password);

    private static LoginRequestUseCase CreateUseCase(ApplicationDbContext context, DateTimeOffset now)
    {
        var timeProvider = new FixedTimeProvider(now);
        return new LoginRequestUseCase(
            context,
            new IdentityNormalizer(),
            new Pbkdf2SecretHasher(),
            new TestTokenService(now),
            timeProvider);
    }

    private static async Task<User> SeedUserAsync(ApplicationDbContext context)
    {
        var role = new Role
        {
            Code = Roles.CustomerCode,
            DisplayName = "Customer"
        };

        var user = new User
        {
            FullName = "Nguyen Van A",
            PhoneNumber = "+84901234567",
            Email = "customer@gmail.com",
            PasswordHash = new Pbkdf2SecretHasher().Hash("P@ssword123"),
            RoleId = role.Id,
            Status = UserStatus.Active
        };

        context.Roles.Add(role);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"login-lockout-{Guid.NewGuid():N}")
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TestTokenService(DateTimeOffset now) : ITokenService
    {
        public AccessTokenResult GenerateAccessToken(
            Guid userId,
            string? phoneNumber,
            string? email,
            IReadOnlyCollection<string> roleSystemNames) =>
            new("access-token", now.AddMinutes(5));

        public string GenerateRefreshTokenSecret() => "refresh-secret";

        public DateTimeOffset GetRefreshTokenExpiry() => now.AddDays(30);
    }
}
