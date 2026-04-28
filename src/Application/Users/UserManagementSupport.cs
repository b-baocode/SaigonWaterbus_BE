using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Users;

internal static class UserManagementSupport
{
    public static IQueryable<User> BuildVisibleUsersQuery(IApplicationDbContext context, User actor)
    {
        var query = context.Set<User>()
            .Include(x => x.Role)
            .AsQueryable();

        if (AuthSupport.IsAdmin(actor))
        {
            return query;
        }

        return query.Where(x =>
            x.Role.SystemName == Roles.CustomerSystemName
            || x.Role.SystemName == Roles.StaffSystemName);
    }

    public static void EnsureDepartmentMatchesRole(Role role, string? department, string propertyName)
    {
        var hasDepartment = !string.IsNullOrWhiteSpace(department);
        if (AuthSupport.RequiresDepartment(role) && !hasDepartment)
        {
            throw AuthSupport.CreateValidationException(propertyName, "Department is required for internal accounts.");
        }

        if (!AuthSupport.RequiresDepartment(role) && hasDepartment)
        {
            throw AuthSupport.CreateValidationException(propertyName, "Customer accounts cannot have a department.");
        }
    }

    public static void EnsureCanCreateRole(User actor, Role targetRole, string propertyName)
    {
        if (AuthSupport.IsAdmin(actor))
        {
            return;
        }

        if (AuthSupport.IsManager(actor) && targetRole.SystemName == Roles.StaffSystemName)
        {
            return;
        }

        throw AuthSupport.CreateValidationException(propertyName, "Manager can only create staff accounts.");
    }

    public static void EnsureCanViewUser(User actor, User target)
    {
        if (AuthSupport.IsAdmin(actor))
        {
            return;
        }

        if (AuthSupport.IsManager(actor)
            && (AuthSupport.IsCustomer(target) || AuthSupport.IsStaff(target)))
        {
            return;
        }

        throw new ForbiddenAccessException();
    }

    public static void EnsureCanUpdateUser(User actor, User target)
    {
        if (actor.Id == target.Id)
        {
            throw AuthSupport.CreateValidationException(nameof(target.Id), "Current account cannot be updated from the management API.");
        }

        if (AuthSupport.IsAdmin(actor))
        {
            return;
        }

        if (AuthSupport.IsManager(actor) && AuthSupport.IsCustomer(target))
        {
            return;
        }

        throw new ForbiddenAccessException();
    }

    public static void EnsureCanDeleteUser(User actor, User target)
    {
        if (actor.Id == target.Id)
        {
            throw AuthSupport.CreateValidationException(nameof(target.Id), "Current account must be deleted from the profile API.");
        }

        if (AuthSupport.IsAdmin(actor)
            && (AuthSupport.IsManager(target) || AuthSupport.IsStaff(target)))
        {
            return;
        }

        if (AuthSupport.IsManager(actor) && AuthSupport.IsStaff(target))
        {
            return;
        }

        throw new ForbiddenAccessException();
    }

    public static void EnsureCanAssignRole(User actor, User target, Role targetRole, string propertyName)
    {
        if (AuthSupport.IsAdmin(actor))
        {
            return;
        }

        if (AuthSupport.IsManager(actor))
        {
            if (!AuthSupport.IsCustomer(target))
            {
                throw new ForbiddenAccessException();
            }

            if (targetRole.SystemName != Roles.CustomerSystemName)
            {
                throw AuthSupport.CreateValidationException(propertyName, "Manager can only keep customer role when updating a customer account.");
            }

            return;
        }

        throw new ForbiddenAccessException();
    }

    public static async Task<User> GetVisibleUserByIdAsync(
        IApplicationDbContext context,
        User actor,
        int userId,
        CancellationToken cancellationToken)
    {
        var user = await BuildVisibleUsersQuery(context, actor)
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new global::SaigonWaterbus.Application.Common.Exceptions.NotFoundException("User was not found.");

        EnsureCanViewUser(actor, user);
        return user;
    }
}
