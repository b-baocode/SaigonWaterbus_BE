using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.WaterbusServices;

internal static class WaterbusServiceSupport
{
    public static async Task<User> EnsureCurrentUserCanViewWaterbusServicesAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var actor = await AuthSupport.GetCurrentUserWithRoleAsync(
            context,
            userContext,
            cancellationToken);

        if (AuthSupport.IsAdmin(actor) || AuthSupport.IsManager(actor) || AuthSupport.IsStaff(actor))
        {
            return actor;
        }

        throw new ForbiddenAccessException();
    }

    public static async Task EnsureCurrentUserCanManageWaterbusServicesAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        CancellationToken cancellationToken)
    {
        await AuthSupport.EnsureCurrentUserIsAdminAsync(
            context,
            userContext,
            cancellationToken);
    }

    public static bool CanManageWaterbusServices(User actor) =>
        AuthSupport.IsAdmin(actor);

    public static IQueryable<WaterbusService> ApplyVisibilityFilter(
        IQueryable<WaterbusService> query,
        User actor,
        bool includeInactive)
    {
        if (includeInactive && CanManageWaterbusServices(actor))
        {
            return query;
        }

        return query.Where(x => x.IsActive);
    }

    public static string NormalizeCode(string code) =>
        code.Trim().ToUpperInvariant();

    public static WaterbusServiceDto CreateDto(WaterbusService service) =>
        new(
            service.Id,
            service.Code,
            service.Name,
            service.Description,
            service.IsActive,
            service.DisplayOrder,
            service.BookingMode);

}
