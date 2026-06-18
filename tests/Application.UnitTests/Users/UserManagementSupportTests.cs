using NUnit.Framework;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Users;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Users;

public class UserManagementSupportTests
{
    [Test]
    public void ManagerCanCreateStaff()
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var targetRole = Role(Roles.StaffSystemName);

        Should.NotThrow(() => UserManagementSupport.EnsureCanCreateRole(actor, targetRole, "roleId"));
    }

    [Test]
    public void ManagerCannotCreateCustomer()
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var targetRole = Role(Roles.CustomerSystemName);

        Should.Throw<ValidationException>(() =>
            UserManagementSupport.EnsureCanCreateRole(actor, targetRole, "roleId"));
    }

    [TestCase(Roles.ManagerSystemName)]
    [TestCase(Roles.AdminName)]
    public void ManagerCannotCreateManagerOrAdmin(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var targetRole = Role(targetSystemName);

        Should.Throw<ValidationException>(() =>
            UserManagementSupport.EnsureCanCreateRole(actor, targetRole, "roleId"));
    }

    [Test]
    public void AdminCanCreateManager()
    {
        var actor = UserWithRole(1, Roles.AdminName);
        var targetRole = Role(Roles.ManagerSystemName);

        Should.NotThrow(() => UserManagementSupport.EnsureCanCreateRole(actor, targetRole, "roleId"));
    }

    [TestCase(Roles.AdminName)]
    [TestCase(Roles.CustomerSystemName)]
    [TestCase(Roles.StaffSystemName)]
    [TestCase(Roles.CustomerSystemName)]
    public void AdminCannotCreateNonManagerRoles(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.AdminName);
        var targetRole = Role(targetSystemName);

        Should.Throw<ValidationException>(() =>
            UserManagementSupport.EnsureCanCreateRole(actor, targetRole, "roleId"));
    }

    [TestCase(Roles.CustomerSystemName)]
    [TestCase(Roles.StaffSystemName)]
    public void ManagerCanViewUpdateAndDeleteCustomerAndStaff(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var target = UserWithRole(2, targetSystemName);

        Should.NotThrow(() => UserManagementSupport.EnsureCanViewUser(actor, target));
        Should.NotThrow(() => UserManagementSupport.EnsureCanUpdateUser(actor, target));
        Should.NotThrow(() => UserManagementSupport.EnsureCanDeleteUser(actor, target));
    }

    [TestCase(Roles.ManagerSystemName)]
    [TestCase(Roles.AdminName)]
    public void ManagerCannotViewUpdateOrDeleteManagerOrAdmin(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var target = UserWithRole(2, targetSystemName);

        Should.Throw<ForbiddenAccessException>(() => UserManagementSupport.EnsureCanViewUser(actor, target));
        Should.Throw<ForbiddenAccessException>(() => UserManagementSupport.EnsureCanUpdateUser(actor, target));
        Should.Throw<ForbiddenAccessException>(() => UserManagementSupport.EnsureCanDeleteUser(actor, target));
    }

    [TestCase(Roles.StaffSystemName)]
    public void ManagerCanAssignStaffRoleToCustomerOrStaff(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var target = UserWithRole(2, targetSystemName);

        Should.NotThrow(() =>
            UserManagementSupport.EnsureCanAssignRole(actor, target, Role(Roles.StaffSystemName), "roleId"));
    }

    [TestCase(Roles.CustomerSystemName)]
    [TestCase(Roles.StaffSystemName)]
    public void ManagerCannotAssignCustomerRole(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var target = UserWithRole(2, targetSystemName);

        Should.Throw<ValidationException>(() =>
            UserManagementSupport.EnsureCanAssignRole(actor, target, Role(Roles.CustomerSystemName), "roleId"));
    }

    [TestCase(Roles.ManagerSystemName)]
    [TestCase(Roles.AdminName)]
    public void ManagerCannotAssignManagerOrAdminRoles(string targetRoleSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var target = UserWithRole(2, Roles.StaffSystemName);

        Should.Throw<ValidationException>(() =>
            UserManagementSupport.EnsureCanAssignRole(actor, target, Role(targetRoleSystemName), "roleId"));
    }

    [TestCase(Roles.ManagerSystemName)]
    [TestCase(Roles.AdminName)]
    public void ManagerCannotAssignRoleToManagerOrAdminTarget(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var target = UserWithRole(2, targetSystemName);

        Should.Throw<ForbiddenAccessException>(() =>
            UserManagementSupport.EnsureCanAssignRole(actor, target, Role(Roles.CustomerSystemName), "roleId"));
    }

    [Test]
    public void AdminCanAssignManagerRole()
    {
        var actor = UserWithRole(1, Roles.AdminName);
        var target = UserWithRole(2, Roles.StaffSystemName);

        Should.NotThrow(() =>
            UserManagementSupport.EnsureCanAssignRole(actor, target, Role(Roles.ManagerSystemName), "roleId"));
    }

    [TestCase(Roles.AdminName)]
    [TestCase(Roles.StaffSystemName)]
    [TestCase(Roles.CustomerSystemName)]
    public void AdminCannotAssignNonManagerRoles(string targetRoleSystemName)
    {
        var actor = UserWithRole(1, Roles.AdminName);
        var target = UserWithRole(2, Roles.StaffSystemName);

        Should.Throw<ValidationException>(() =>
            UserManagementSupport.EnsureCanAssignRole(actor, target, Role(Roles.AdminName), "roleId"));
    }

    [Test]
    public void ActorCannotUpdateOrDeleteOwnAccountFromManagementApi()
    {
        var userId = Guid.NewGuid();
        var actor = UserWithRole(userId, Roles.ManagerSystemName);
        var target = UserWithRole(userId, Roles.StaffSystemName);

        Should.Throw<ValidationException>(() => UserManagementSupport.EnsureCanUpdateUser(actor, target));
        Should.Throw<ValidationException>(() => UserManagementSupport.EnsureCanDeleteUser(actor, target));
    }

    [Test]
    public void AdminCanAssignStationsToManager()
    {
        var actor = UserWithRole(1, Roles.AdminName);
        var target = UserWithRole(2, Roles.ManagerSystemName);

        Should.NotThrow(() => UserManagementSupport.EnsureCanAssignStationsToUser(actor, target));
    }

    [Test]
    public void AdminCanAssignStationsToStaff()
    {
        var actor = UserWithRole(1, Roles.AdminName);
        var target = UserWithRole(2, Roles.StaffSystemName);

        Should.NotThrow(() => UserManagementSupport.EnsureCanAssignStationsToUser(actor, target));
    }

    [Test]
    public void ManagerCannotAssignStations()
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var target = UserWithRole(2, Roles.ManagerSystemName);

        Should.Throw<ForbiddenAccessException>(() =>
            UserManagementSupport.EnsureCanAssignStationsToUser(actor, target));
    }

    [TestCase(Roles.CustomerSystemName)]
    [TestCase(Roles.AdminName)]
    public void AdminCannotAssignStationsToUnsupportedRoles(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.AdminName);
        var target = UserWithRole(2, targetSystemName);

        Should.Throw<ValidationException>(() =>
            UserManagementSupport.EnsureCanAssignStationsToUser(actor, target));
    }

    private static User UserWithRole(int id, string systemName) =>
        UserWithRole(Guid.Parse($"00000000-0000-0000-0000-{id:000000000000}"), systemName);

    private static User UserWithRole(Guid id, string systemName) =>
        new()
        {
            Id = id,
            FullName = $"{systemName} User",
            Role = Role(systemName)
        };

    private static Role Role(string systemName) =>
        systemName switch
        {
            Roles.AdminName => new Role
            {
                Id = Guid.NewGuid(),
                Code = Roles.AdminCode,
                SystemName = Roles.AdminName,
                DisplayName = "Admin"
            },
            Roles.ManagerSystemName => new Role
            {
                Id = Guid.NewGuid(),
                Code = Roles.ManagerCode,
                SystemName = Roles.ManagerSystemName,
                DisplayName = "Manager"
            },
            Roles.StaffSystemName => new Role
            {
                Id = Guid.NewGuid(),
                Code = Roles.StaffCode,
                SystemName = Roles.StaffSystemName,
                DisplayName = "Staff"
            },
            Roles.CustomerSystemName => new Role
            {
                Id = Guid.NewGuid(),
                Code = Roles.CustomerCode,
                SystemName = Roles.CustomerSystemName,
                DisplayName = "Customer"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(systemName), systemName, null)
        };
}
