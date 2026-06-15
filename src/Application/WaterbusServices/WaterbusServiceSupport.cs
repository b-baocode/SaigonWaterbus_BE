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

    public static SeatType CreateSeatType(
        string code,
        string name,
        int displayOrder) =>
        new()
        {
            Code = code,
            Name = name,
            DisplayOrder = displayOrder,
            IsActive = true
        };

    public static ServiceSeatTypePrice CreateServiceSeatTypePrice(
        WaterbusService service,
        SeatType seatType,
        decimal priceModifier = 1m) =>
        new()
        {
            WaterbusServiceId = service.Id,
            WaterbusService = service,
            SeatTypeId = seatType.Id,
            SeatType = seatType,
            PriceModifier = priceModifier,
            IsActive = true
        };

    public static WaterbusServiceSeatTypesDto CreateSeatTypesDto(
        WaterbusService service,
        bool includeInactive,
        IReadOnlyCollection<SeatType>? availableSeatTypes = null)
    {
        WaterbusServiceSeatTypeDto[] prices;
        if (includeInactive && availableSeatTypes is { Count: > 0 })
        {
            var priceBySeatTypeId = service.SeatTypePrices
                .ToDictionary(x => x.SeatTypeId);
            prices = availableSeatTypes
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.Code)
                .Select(seatType =>
                {
                    priceBySeatTypeId.TryGetValue(seatType.Id, out var price);
                    return new WaterbusServiceSeatTypeDto(
                        seatType.Id,
                        seatType.Code,
                        seatType.Name,
                        seatType.DisplayOrder,
                        price?.PriceModifier,
                        seatType.IsActive && price?.IsActive == true);
                })
                .ToArray();
        }
        else
        {
            prices = service.SeatTypePrices
                .Where(x => includeInactive || (x.IsActive && x.SeatType.IsActive))
                .OrderBy(x => x.SeatType.DisplayOrder)
                .ThenBy(x => x.SeatType.Code)
                .Select(x => new WaterbusServiceSeatTypeDto(
                    x.SeatType.Id,
                    x.SeatType.Code,
                    x.SeatType.Name,
                    x.SeatType.DisplayOrder,
                    x.PriceModifier,
                    x.IsActive && x.SeatType.IsActive))
                .ToArray();
        }

        return new WaterbusServiceSeatTypesDto(
            service.Id,
            service.Code,
            service.BookingMode,
            prices);
    }

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
