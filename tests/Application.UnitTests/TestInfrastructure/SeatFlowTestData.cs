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

    public static async Task<TestUserContext> SeedStaffAsync(ApplicationDbContext context)
    {
        return await SeedUserAsync(
            context,
            Roles.StaffCode,
            Roles.StaffSystemName,
            "Staff");
    }

    private static async Task<TestUserContext> SeedUserAsync(
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
            FullName = $"Seat flow {roleDisplayName}",
            RoleId = role.Id,
            Role = role,
            Status = UserStatus.Active
        };

        context.AddRange(role, user);
        await context.SaveChangesAsync();
        return new TestUserContext(user.Id);
    }

    public static SeatType SeatType(string code, bool isActive = true) =>
        new()
        {
            Code = code,
            Name = code,
            DisplayOrder = code == "STANDARD" ? 1 : 2,
            IsActive = isActive
        };

    public static WaterbusService Service(
        string code,
        params (SeatType SeatType, decimal Modifier, bool IsActive)[] prices)
    {
        var service = new WaterbusService
        {
            Code = code,
            Name = code,
            IsActive = true
        };

        foreach (var (seatType, modifier, isActive) in prices)
        {
            service.SeatTypePrices.Add(new ServiceSeatTypePrice
            {
                WaterbusServiceId = service.Id,
                WaterbusService = service,
                SeatTypeId = seatType.Id,
                SeatType = seatType,
                PriceModifier = modifier,
                IsActive = isActive
            });
        }

        return service;
    }

    public static Vessel Vessel(
        SeatSetupType setupType,
        bool seatsConfigured = false,
        VesselStatus status = VesselStatus.Inactive) =>
        new()
        {
            Code = $"VESSEL_{Guid.NewGuid():N}"[..20],
            Name = "Validation vessel",
            Status = status,
            SeatCount = 4,
            NumberOfDecks = 1,
            SeatSetupType = setupType,
            SeatsConfigured = seatsConfigured
        };
}

internal sealed class TestUserContext(Guid userId) : IUserContext
{
    public Guid? UserId { get; } = userId;

    public bool IsAuthenticated => true;
}

internal sealed class TestDatabaseExceptionClassifier : IDatabaseExceptionClassifier
{
    public bool IsUniqueConstraintViolation(Exception exception) => false;
}

internal sealed class TestVesselImageStorageService : IVesselImageStorageService
{
    public long MaxImageBytes => 5 * 1024 * 1024;

    public IReadOnlyCollection<string> AllowedImageContentTypes { get; } =
        ["image/jpeg", "image/png", "image/webp"];

    public Task<StoredVesselImage> UploadImageAsync(
        VesselImageUpload upload,
        CancellationToken cancellationToken) =>
        Task.FromResult(new StoredVesselImage(
            $"https://example.test/vessels/{upload.VesselId}",
            upload.VesselId.ToString("N")));
}
