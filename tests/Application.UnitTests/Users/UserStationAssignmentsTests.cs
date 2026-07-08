using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Users;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;
using SaigonWaterbus.Infrastructure.Data;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Users;

public class UserStationAssignmentsTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ManagerCanAssignStaffToOwnedStation()
    {
        await using var context = CreateContext();
        var manager = await SeedUserAsync(context, Roles.ManagerCode, Roles.ManagerSystemName, "Manager");
        var staff = await SeedUserAsync(context, Roles.StaffCode, Roles.StaffSystemName, "Staff");
        var station = await SeedStationAsync(context, "BD", "Bach Dang");
        await SeedAssignmentAsync(context, manager, station);

        var useCase = CreateUseCase(context, manager.Id);

        var result = await useCase.ExecuteAsync(
            new AssignUserStationsRequest(staff.Id, [station.Id], station.Id),
            CancellationToken.None);

        result.Count.ShouldBe(1);
        var assignment = await context.UserStationAssignments.SingleAsync(x => x.UserId == staff.Id);
        assignment.StationId.ShouldBe(station.Id);
        assignment.IsPrimary.ShouldBeTrue();
        assignment.IsActive.ShouldBeTrue();
        assignment.AssignedByUserId.ShouldBe(manager.Id);
        assignment.AssignedAt.ShouldBe(Now);
    }

    [Test]
    public async Task ManagerCannotAssignStaffToUnownedStation()
    {
        await using var context = CreateContext();
        var manager = await SeedUserAsync(context, Roles.ManagerCode, Roles.ManagerSystemName, "Manager");
        var staff = await SeedUserAsync(context, Roles.StaffCode, Roles.StaffSystemName, "Staff");
        var ownedStation = await SeedStationAsync(context, "BD", "Bach Dang");
        var otherStation = await SeedStationAsync(context, "TT", "Thanh Da");
        await SeedAssignmentAsync(context, manager, ownedStation);

        var useCase = CreateUseCase(context, manager.Id);

        await Should.ThrowAsync<ValidationException>(() =>
            useCase.ExecuteAsync(
                new AssignUserStationsRequest(staff.Id, [otherStation.Id], otherStation.Id),
                CancellationToken.None));
    }

    [Test]
    public async Task ManagerWithoutStationsCannotAssignStaffStations()
    {
        await using var context = CreateContext();
        var manager = await SeedUserAsync(context, Roles.ManagerCode, Roles.ManagerSystemName, "Manager");
        var staff = await SeedUserAsync(context, Roles.StaffCode, Roles.StaffSystemName, "Staff");
        var station = await SeedStationAsync(context, "BD", "Bach Dang");

        var useCase = CreateUseCase(context, manager.Id);

        await Should.ThrowAsync<ValidationException>(() =>
            useCase.ExecuteAsync(
                new AssignUserStationsRequest(staff.Id, [station.Id], station.Id),
                CancellationToken.None));
    }

    [Test]
    public async Task ManagerCannotOverwriteStaffAssignmentOutsideScope()
    {
        await using var context = CreateContext();
        var manager = await SeedUserAsync(context, Roles.ManagerCode, Roles.ManagerSystemName, "Manager");
        var staff = await SeedUserAsync(context, Roles.StaffCode, Roles.StaffSystemName, "Staff");
        var ownedStation = await SeedStationAsync(context, "BD", "Bach Dang");
        var otherStation = await SeedStationAsync(context, "TT", "Thanh Da");
        await SeedAssignmentAsync(context, manager, ownedStation);
        await SeedAssignmentAsync(context, staff, otherStation);

        var useCase = CreateUseCase(context, manager.Id);

        await Should.ThrowAsync<ValidationException>(() =>
            useCase.ExecuteAsync(
                new AssignUserStationsRequest(staff.Id, [ownedStation.Id], ownedStation.Id),
                CancellationToken.None));

        (await context.UserStationAssignments.AnyAsync(x =>
            x.UserId == staff.Id && x.StationId == otherStation.Id)).ShouldBeTrue();
    }

    private static AssignUserStationsRequestUseCase CreateUseCase(ApplicationDbContext context, Guid userId) =>
        new(context, new TestUserContext(userId), new FixedTimeProvider(Now));

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"user-station-assignments-{Guid.NewGuid():N}")
            .Options;

        return new ApplicationDbContext(options);
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
            Status = UserStatus.Active
        };

        context.AddRange(role, user);
        await context.SaveChangesAsync();
        return user;
    }

    private static async Task<Station> SeedStationAsync(
        ApplicationDbContext context,
        string code,
        string name)
    {
        var station = new Station
        {
            StationCode = code,
            StationName = name,
            Status = StationStatus.Active
        };

        context.Stations.Add(station);
        await context.SaveChangesAsync();
        return station;
    }

    private static async Task SeedAssignmentAsync(
        ApplicationDbContext context,
        User user,
        Station station)
    {
        context.UserStationAssignments.Add(new UserStationAssignment
        {
            UserId = user.Id,
            User = user,
            StationId = station.Id,
            Station = station,
            IsPrimary = true,
            IsActive = true,
            AssignedAt = Now,
            AssignedByUserId = user.Id,
            AssignedByUser = user
        });
        await context.SaveChangesAsync();
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
