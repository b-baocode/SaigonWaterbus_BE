using Microsoft.EntityFrameworkCore;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;

namespace SaigonWaterbus.Application.UnitTests.TestInfrastructure;

internal static class SeatFlowTestData
{
    public static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"seat-flow-{Guid.NewGuid():N}")
            // Handler dùng ExecuteInTransactionAsync — provider in-memory không hỗ trợ transaction thật.
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    public static async Task<TestUserContext> SeedAdminAsync(ApplicationDbContext context)
    {
        return await SeedUserAsync(
            context,
            Roles.AdminCode,
            Roles.AdminName,
            "Admin");
    }

    public static async Task<TestUserContext> SeedManagerAsync(ApplicationDbContext context)
    {
        return await SeedUserAsync(
            context,
            Roles.ManagerCode,
            Roles.ManagerSystemName,
            "Manager");
    }

    public static async Task<TestUserContext> SeedCustomerAsync(ApplicationDbContext context)
    {
        return await SeedUserAsync(
            context,
            Roles.CustomerCode,
            Roles.CustomerSystemName,
            "Customer");
    }

    public static async Task<TestUserContext> SeedStaffAsync(
        ApplicationDbContext context,
        StaffType staffType = StaffType.OnBoard)
    {
        return await SeedUserAsync(
            context,
            Roles.StaffCode,
            Roles.StaffSystemName,
            "Staff",
            staffType);
    }

    private static async Task<TestUserContext> SeedUserAsync(
        ApplicationDbContext context,
        string roleCode,
        string roleSystemName,
        string roleDisplayName,
        StaffType? staffType = null)
    {
        var role = new Role
        {
            Code = roleCode,
            SystemName = roleSystemName,
            DisplayName = roleDisplayName
        };
        var user = new User
        {
            FullName = $"Seat flow {roleDisplayName}",
            RoleId = role.Id,
            Role = role,
            StaffType = staffType,
            Status = UserStatus.Active
        };

        context.AddRange(role, user);
        await context.SaveChangesAsync();
        return new TestUserContext(user.Id);
    }

    public static Boat Boat(
        SeatSetupType setupType,
        bool seatsConfigured = false,
        BoatStatus status = BoatStatus.Inactive) =>
        new()
        {
            Code = $"VESSEL_{Guid.NewGuid():N}"[..20],
            Name = "Validation boat",
            Status = status,
            SeatCount = 4,
            NumberOfDecks = 1,
            SeatSetupType = setupType,
            SeatsConfigured = seatsConfigured
        };

    public static void AddRequiredDocuments(
        Boat boat,
        DateTimeOffset? uploadedAt = null)
    {
        var timestamp = uploadedAt ?? new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        boat.Documents =
        [
            Document(BoatDocumentType.Inspection, timestamp),
            Document(BoatDocumentType.Registration, timestamp),
            Document(BoatDocumentType.Insurance, timestamp),
            Document(BoatDocumentType.OperationLicense, timestamp)
        ];
    }

    private static BoatDocument Document(
        BoatDocumentType type,
        DateTimeOffset uploadedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            FileName = $"{type}.pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            FileUrl = $"https://example.test/documents/{type}.pdf",
            StorageKey = $"documents/{type}.pdf",
            UploadedAt = uploadedAt
        };
}

internal sealed class TestUserContext(Guid userId) : IUserContext
{
    public Guid? UserId { get; } = userId;

    public bool IsAuthenticated => true;
}

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

internal sealed class TestDatabaseExceptionClassifier : IDatabaseExceptionClassifier
{
    public bool IsUniqueConstraintViolation(Exception exception) => false;

    public bool IsExclusionConstraintViolation(Exception exception) => false;
}

internal sealed class TestBoatImageStorageService : IBoatImageStorageService
{
    public long MaxImageBytes => 5 * 1024 * 1024;

    public IReadOnlyCollection<string> AllowedImageContentTypes { get; } =
        ["image/jpeg", "image/png", "image/webp"];

    public Task<StoredBoatImage> UploadImageAsync(
        BoatImageUpload upload,
        CancellationToken cancellationToken)
    {
        var imageId = upload.ImageId ?? upload.BoatId;
        return Task.FromResult(new StoredBoatImage(
            $"https://example.test/boats/{upload.BoatId}/{imageId:N}",
            imageId.ToString("N")));
    }
}

internal sealed class TestBoatDocumentStorageService : IBoatDocumentStorageService
{
    public long MaxDocumentBytes => 10 * 1024 * 1024;

    public IReadOnlyCollection<string> AllowedDocumentContentTypes { get; } =
        ["application/pdf", "image/jpeg", "image/png", "image/webp"];

    public Task<StoredBoatDocument> UploadDocumentAsync(
        BoatDocumentUpload upload,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new StoredBoatDocument(
            $"https://example.test/boat-documents/{upload.BoatId}/{upload.Type}/{upload.DocumentId:N}",
            $"{upload.BoatId}/{upload.Type}/{upload.DocumentId:N}"));
    }

    public string CreateDocumentUrl(string storageKey) =>
        $"https://example.test/signed-boat-documents/{storageKey}";
}

internal sealed class TestStationImageStorageService : IStationImageStorageService
{
    public long MaxImageBytes => 5 * 1024 * 1024;

    public IReadOnlyCollection<string> AllowedImageContentTypes { get; } =
        ["image/jpeg", "image/png", "image/webp"];

    public Task<StoredStationImage> UploadImageAsync(
        StationImageUpload upload,
        CancellationToken cancellationToken)
    {
        var imageId = upload.ImageId ?? upload.StationId;
        return Task.FromResult(new StoredStationImage(
            $"https://example.test/stations/{upload.StationId}/{imageId:N}",
            imageId.ToString("N")));
    }
}
