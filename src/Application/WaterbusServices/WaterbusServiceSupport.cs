using SaigonWaterbus.Application.Auth.Common;
using SaigonWaterbus.Application.Common.Exceptions;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.WaterbusServices;

internal static class WaterbusServiceSupport
{
    private static readonly WaterbusServiceSeatTypeDto[] StandardSeatTypes =
    [
        new(Guid.Empty, "STANDARD", "Standard", 1, true)
    ];

    private static readonly WaterbusServiceSeatTypeDto[] SightseeingSeatTypes =
    [
        new(Guid.Empty, "CABIN", "Cabin", 1, true),
        new(Guid.Empty, "RIVER", "River", 2, true),
        new(Guid.Empty, "SKY", "Sky", 3, true)
    ];

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

    public static WaterbusServiceSeatTypesDto CreateSeatTypesDto(
        WaterbusService service,
        bool includeInactive) =>
        new(
            service.Id,
            service.Code,
            service.BookingMode,
            GetSeatTypesForBookingMode(service.BookingMode));

    public static IReadOnlyCollection<WaterbusServiceSeatTypeDto> GetSeatTypesForBookingMode(BookingMode bookingMode) =>
        bookingMode switch
        {
            BookingMode.SeatBased => StandardSeatTypes,
            BookingMode.SeatTypeBased => SightseeingSeatTypes,
            _ => []
        };

    public static IReadOnlyCollection<SeatSetupType> GetSupportedSeatSetupTypes(BookingMode bookingMode) =>
        bookingMode switch
        {
            BookingMode.SeatBased => [SeatSetupType.FullStandard],
            BookingMode.SeatTypeBased => [SeatSetupType.FullStandard, SeatSetupType.StandardAndVip],
            _ => []
        };

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
