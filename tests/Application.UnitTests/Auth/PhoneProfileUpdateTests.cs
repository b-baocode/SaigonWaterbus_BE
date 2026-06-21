using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NUnit.Framework;
using SaigonWaterbus.Application.Auth.Profile;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.UnitTests.TestInfrastructure;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Auth;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

namespace SaigonWaterbus.Application.UnitTests.Auth;

public class PhoneProfileUpdateTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 8, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task EmailRegisteredUserCanAddPhoneNumberOnceWithOtp()
    {
        await using var context = CreateContext();
        var user = await SeedCustomerAsync(context, phoneNumber: null);
        var userContext = new TestUserContext(user.Id);
        var smsSender = new TestSmsOtpSender();

        var result = await CreateUpdateUseCase(context, userContext, smsSender)
            .ExecuteAsync(new UpdateCurrentUserProfileRequest(PhoneNumber: "0901234567"), CancellationToken.None);

        result.User.PhoneNumber.ShouldBeNull();
        result.PhoneVerification.ShouldNotBeNull();
        result.PhoneVerification.Channel.ShouldBe(OtpChannel.Phone);
        smsSender.SentPhoneNumber.ShouldBe("+84901234567");

        var challenge = await context.Set<OtpChallenge>()
            .SingleAsync(x => x.UserId == user.Id && x.Purpose == OtpPurpose.PhoneChange);
        challenge.Email.ShouldBe("+84901234567");
        challenge.PendingPhoneNumber.ShouldBe("+84901234567");

        var updatedUser = await context.Set<User>().SingleAsync(x => x.Id == user.Id);
        updatedUser.PhoneNumber.ShouldBeNull();
        updatedUser.NormalizedPhoneNumber.ShouldBeNull();
    }

    [Test]
    public async Task UserWithExistingPhoneNumberCannotSelfChangePhoneNumber()
    {
        await using var context = CreateContext();
        var user = await SeedCustomerAsync(context, phoneNumber: "+84901234567");
        var userContext = new TestUserContext(user.Id);

        var exception = await Should.ThrowAsync<ValidationException>(() =>
            CreateUpdateUseCase(context, userContext, new TestSmsOtpSender())
                .ExecuteAsync(new UpdateCurrentUserProfileRequest(PhoneNumber: "0902345678"), CancellationToken.None));

        exception.Errors["phoneNumber"].ShouldContain(
            "Số điện thoại chỉ được cập nhật một lần. Vui lòng liên hệ Admin hoặc Manager nếu cần thay đổi.");
    }

    [Test]
    public async Task VerifyPhoneOtpAllowsEmailRegisteredUserToPersistFirstPhoneNumber()
    {
        await using var context = CreateContext();
        var user = await SeedCustomerAsync(context, phoneNumber: null);
        var userContext = new TestUserContext(user.Id);
        const string otpCode = "123456";
        var hasher = new TestSecretHasher();
        var challenge = new OtpChallenge
        {
            UserId = user.Id,
            Purpose = OtpPurpose.PhoneChange,
            Email = "+84901234567",
            PendingPhoneNumber = "+84901234567",
            CodeHash = hasher.Hash(otpCode),
            ExpiresAt = Now.AddMinutes(5),
            ResendAvailableAt = Now.AddMinutes(1),
            MaxAttempts = 5
        };
        context.Set<OtpChallenge>().Add(challenge);
        await context.SaveChangesAsync();

        var result = await new VerifyPhoneChangeOtpRequestUseCase(
                context,
                hasher,
                userContext,
                new FixedTimeProvider(Now))
            .ExecuteAsync(new VerifyPhoneChangeOtpRequest(challenge.Id, otpCode), CancellationToken.None);

        result.PhoneNumber.ShouldBe("+84901234567");
        result.PhoneVerifiedAt.ShouldBe(Now);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"auth-phone-profile-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<User> SeedCustomerAsync(ApplicationDbContext context, string? phoneNumber)
    {
        var role = new Role
        {
            Code = Roles.CustomerCode,
            SystemName = Roles.CustomerSystemName,
            DisplayName = "Customer"
        };
        var user = new User
        {
            FullName = "Email registered customer",
            DateOfBirth = new DateOnly(2000, 1, 1),
            Email = "customer@gmail.com",
            NormalizedEmail = "CUSTOMER@GMAIL.COM",
            PhoneNumber = phoneNumber,
            NormalizedPhoneNumber = phoneNumber,
            PhoneVerifiedAt = phoneNumber is null ? null : Now,
            RoleId = role.Id,
            Role = role,
            Status = UserStatus.Active
        };

        context.AddRange(role, user);
        await context.SaveChangesAsync();
        return user;
    }

    private static UpdateCurrentUserProfileRequestUseCase CreateUpdateUseCase(
        ApplicationDbContext context,
        TestUserContext userContext,
        TestSmsOtpSender smsSender) =>
        new(
            context,
            new IdentityNormalizer(),
            new TestProfileImageStorageService(),
            new TestSecretHasher(),
            new TestOtpCodeService(),
            new TestOtpSender(),
            smsSender,
            new TestOtpPolicy(),
            userContext,
            new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TestSecretHasher : ISecretHasher
    {
        public string Hash(string secret) => $"hash:{secret}";

        public bool Verify(string secret, string hash) => hash == Hash(secret);
    }

    private sealed class TestOtpCodeService : IOtpCodeService
    {
        public string GenerateCode() => "123456";

        public string MaskEmail(string email) => email;

        public string MaskPhone(string phoneNumber) => phoneNumber;
    }

    private sealed class TestOtpSender : IOtpSender
    {
        public Task SendAsync(
            string email,
            string code,
            OtpPurpose purpose,
            string? recipientName,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class TestSmsOtpSender : ISmsOtpSender
    {
        public string? SentPhoneNumber { get; private set; }

        public Task SendAsync(
            string phoneNumber,
            string code,
            OtpPurpose purpose,
            string? recipientName,
            CancellationToken cancellationToken)
        {
            SentPhoneNumber = phoneNumber;
            return Task.CompletedTask;
        }
    }

    private sealed class TestOtpPolicy : IOtpPolicy
    {
        public int ExpirationMinutes => 5;

        public int ResendSeconds => 60;

        public int MaxAttempts => 5;
    }

    private sealed class TestProfileImageStorageService : IProfileImageStorageService
    {
        public long MaxAvatarBytes => 5 * 1024 * 1024;

        public IReadOnlyCollection<string> AllowedAvatarContentTypes { get; } =
            ["image/jpeg", "image/png", "image/webp"];

        public Task<StoredProfileImage> UploadAvatarAsync(
            ProfileImageUpload upload,
            CancellationToken cancellationToken) =>
            Task.FromResult(new StoredProfileImage("https://example.test/avatar.jpg", "avatar"));

        public Task<StoredProfileImage> ImportAvatarFromUrlAsync(
            ProfileImageUrlImport import,
            CancellationToken cancellationToken) =>
            Task.FromResult(new StoredProfileImage(import.ImageUrl, "avatar"));
    }
}
