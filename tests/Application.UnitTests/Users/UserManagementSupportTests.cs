using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Users;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;
using NUnit.Framework;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Users;

public class UserManagementSupportTests
{
    [TestCase(Roles.CustomerSystemName)]
    [TestCase(Roles.StaffSystemName)]
    public void ManagerCanCreateCustomerAndStaff(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var targetRole = Role(targetSystemName);

        Should.NotThrow(() => UserManagementSupport.EnsureCanCreateRole(actor, targetRole, "roleId"));
    }

    [TestCase(Roles.ManagerSystemName)]
    [TestCase(Roles.AdminSystemName)]
    public void ManagerCannotCreateManagerOrAdminSystem(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var targetRole = Role(targetSystemName);

        Should.Throw<ValidationException>(() =>
            UserManagementSupport.EnsureCanCreateRole(actor, targetRole, "roleId"));
    }

    [TestCase(Roles.ManagerSystemName)]
    [TestCase(Roles.StaffSystemName)]
    [TestCase(Roles.CustomerSystemName)]
    public void AdminCanCreateManagedRoles(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.AdminSystemName);
        var targetRole = Role(targetSystemName);

        Should.NotThrow(() => UserManagementSupport.EnsureCanCreateRole(actor, targetRole, "roleId"));
    }

    [Test]
    public void AdminCannotCreateAdminSystem()
    {
        var actor = UserWithRole(1, Roles.AdminSystemName);
        var targetRole = Role(Roles.AdminSystemName);

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
    [TestCase(Roles.AdminSystemName)]
    public void ManagerCannotViewUpdateOrDeleteManagerOrAdminSystem(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var target = UserWithRole(2, targetSystemName);

        Should.Throw<ForbiddenAccessException>(() => UserManagementSupport.EnsureCanViewUser(actor, target));
        Should.Throw<ForbiddenAccessException>(() => UserManagementSupport.EnsureCanUpdateUser(actor, target));
        Should.Throw<ForbiddenAccessException>(() => UserManagementSupport.EnsureCanDeleteUser(actor, target));
    }

    [TestCase(Roles.CustomerSystemName)]
    [TestCase(Roles.StaffSystemName)]
    public void ManagerCanAssignCustomerAndStaffRolesToCustomerOrStaff(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var target = UserWithRole(2, targetSystemName);

        Should.NotThrow(() =>
            UserManagementSupport.EnsureCanAssignRole(actor, target, Role(Roles.CustomerSystemName), "roleId"));
        Should.NotThrow(() =>
            UserManagementSupport.EnsureCanAssignRole(actor, target, Role(Roles.StaffSystemName), "roleId"));
    }

    [TestCase(Roles.ManagerSystemName)]
    [TestCase(Roles.AdminSystemName)]
    public void ManagerCannotAssignManagerOrAdminSystemRoles(string targetRoleSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var target = UserWithRole(2, Roles.StaffSystemName);

        Should.Throw<ValidationException>(() =>
            UserManagementSupport.EnsureCanAssignRole(actor, target, Role(targetRoleSystemName), "roleId"));
    }

    [TestCase(Roles.ManagerSystemName)]
    [TestCase(Roles.AdminSystemName)]
    public void ManagerCannotAssignRoleToManagerOrAdminSystemTarget(string targetSystemName)
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var target = UserWithRole(2, targetSystemName);

        Should.Throw<ForbiddenAccessException>(() =>
            UserManagementSupport.EnsureCanAssignRole(actor, target, Role(Roles.CustomerSystemName), "roleId"));
    }

    [TestCase(Roles.ManagerSystemName)]
    [TestCase(Roles.StaffSystemName)]
    [TestCase(Roles.CustomerSystemName)]
    public void AdminCanAssignManagedRoles(string targetRoleSystemName)
    {
        var actor = UserWithRole(1, Roles.AdminSystemName);
        var target = UserWithRole(2, Roles.StaffSystemName);

        Should.NotThrow(() =>
            UserManagementSupport.EnsureCanAssignRole(actor, target, Role(targetRoleSystemName), "roleId"));
    }

    [Test]
    public void AdminCannotAssignAdminSystemRole()
    {
        var actor = UserWithRole(1, Roles.AdminSystemName);
        var target = UserWithRole(2, Roles.StaffSystemName);

        Should.Throw<ValidationException>(() =>
            UserManagementSupport.EnsureCanAssignRole(actor, target, Role(Roles.AdminSystemName), "roleId"));
    }

    [Test]
    public void ActorCannotUpdateOrDeleteOwnAccountFromManagementApi()
    {
        var actor = UserWithRole(1, Roles.ManagerSystemName);
        var target = UserWithRole(1, Roles.StaffSystemName);

        Should.Throw<ValidationException>(() => UserManagementSupport.EnsureCanUpdateUser(actor, target));
        Should.Throw<ValidationException>(() => UserManagementSupport.EnsureCanDeleteUser(actor, target));
    }

    private static User UserWithRole(int id, string systemName) =>
        new()
        {
            Id = id,
            FullName = $"{systemName} User",
            Role = Role(systemName)
        };

    private static Role Role(string systemName) =>
        systemName switch
        {
            Roles.AdminSystemName => new Role
            {
                Id = 1,
                Code = Roles.AdminSystemCode,
                SystemName = Roles.AdminSystemName,
                DisplayName = "Admin System"
            },
            Roles.ManagerSystemName => new Role
            {
                Id = 2,
                Code = Roles.ManagerCode,
                SystemName = Roles.ManagerSystemName,
                DisplayName = "Manager"
            },
            Roles.StaffSystemName => new Role
            {
                Id = 3,
                Code = Roles.StaffCode,
                SystemName = Roles.StaffSystemName,
                DisplayName = "Staff"
            },
            Roles.CustomerSystemName => new Role
            {
                Id = 4,
                Code = Roles.CustomerCode,
                SystemName = Roles.CustomerSystemName,
                DisplayName = "Customer"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(systemName), systemName, null)
        };
}
