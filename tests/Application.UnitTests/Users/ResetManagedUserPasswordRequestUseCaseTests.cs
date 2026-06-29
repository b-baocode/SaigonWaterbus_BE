using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Users;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Auth;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Users;

public class ResetManagedUserPasswordRequestUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ExecuteReplacesPasswordResetsFailedLoginsAndRevokesActiveRefreshTokens()
    {
        await using var context = CreateContext();
        var actor = await SeedUserAsync(context, Roles.AdminCode, Roles.AdminName, "Admin");
        var target = await SeedUserAsync(context, Roles.ManagerCode, Roles.ManagerSystemName, "Manager");
        var secretHasher = new Pbkdf2SecretHasher();
        target.PasswordHash = secretHasher.Hash("OldP@ssword123");
        target.FailedLoginAttemptCount = 4;
        target.FailedLoginWindowStartedAt = Now.AddMinutes(-5);

        var activeRefreshToken = new RefreshToken
        {
            UserId = target.Id,
            TokenHash = secretHasher.Hash("refresh-secret"),
            ExpiresAt = Now.AddDays(1)
        };
        context.RefreshTokens.Add(activeRefreshToken);
        await context.SaveChangesAsync();

        var useCase = new ResetManagedUserPasswordRequestUseCase(
            context,
            secretHasher,
            new TestUserContext(actor.Id),
            new FixedTimeProvider(Now));

        var result = await useCase.ExecuteAsync(new ResetManagedUserPasswordRequest(target.Id), CancellationToken.None);

        result.User.Id.ShouldBe(target.Id);
        result.GeneratedPassword.Length.ShouldBe(12);
        PasswordRules.IsStrong(result.GeneratedPassword).ShouldBeTrue();

        var updatedUser = await context.Users.SingleAsync(x => x.Id == target.Id);
        secretHasher.Verify(result.GeneratedPassword, updatedUser.PasswordHash!).ShouldBeTrue();
        secretHasher.Verify("OldP@ssword123", updatedUser.PasswordHash!).ShouldBeFalse();
        updatedUser.FailedLoginAttemptCount.ShouldBe(0);
        updatedUser.FailedLoginWindowStartedAt.ShouldBeNull();

        var updatedToken = await context.RefreshTokens.SingleAsync(x => x.Id == activeRefreshToken.Id);
        updatedToken.RevokedAt.ShouldBe(Now);
    }

    [Test]
    public async Task ExecuteDoesNotChangeSuspendedStatus()
    {
        await using var context = CreateContext();
        var actor = await SeedUserAsync(context, Roles.AdminCode, Roles.AdminName, "Admin");
        var target = await SeedUserAsync(context, Roles.ManagerCode, Roles.ManagerSystemName, "Manager");
        target.Status = UserStatus.Suspended;
        await context.SaveChangesAsync();

        var useCase = new ResetManagedUserPasswordRequestUseCase(
            context,
            new Pbkdf2SecretHasher(),
            new TestUserContext(actor.Id),
            new FixedTimeProvider(Now));

        await useCase.ExecuteAsync(new ResetManagedUserPasswordRequest(target.Id), CancellationToken.None);

        var updatedUser = await context.Users.SingleAsync(x => x.Id == target.Id);
        updatedUser.Status.ShouldBe(UserStatus.Suspended);
    }

    private static async Task<User> SeedUserAsync(
        ApplicationDbContext context,
        string roleCode,
        string roleSystemName,
        string roleDisplayName)
    {
        var role = new Role
        {
            Code = roleCode,
            SystemName = roleSystemName,
            DisplayName = roleDisplayName
        };
        var user = new User
        {
            FullName = $"{roleDisplayName} User",
            Email = $"{roleSystemName.ToLowerInvariant()}-{Guid.NewGuid():N}@gmail.com",
            RoleId = role.Id,
            Role = role,
            Status = UserStatus.Active,
            PasswordHash = new Pbkdf2SecretHasher().Hash("P@ssword123")
        };

        context.AddRange(role, user);
        await context.SaveChangesAsync();
        return user;
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"reset-managed-password-{Guid.NewGuid():N}")
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TestUserContext(Guid userId) : IUserContext
    {
        public Guid? UserId { get; } = userId;

        public bool IsAuthenticated => true;
    }
}
