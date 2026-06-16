using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.WaterbusServices;

public sealed record PublicWaterbusServiceSeatTypeDto(
    Guid SeatTypeId,
    string Code,
    string Name,
    decimal PriceModifier);

public sealed record PublicWaterbusServiceCatalogDto(
    Guid ServiceId,
    string Code,
    string Name,
    string? Description,
    BookingMode BookingMode,
    IReadOnlyCollection<SeatSetupType> SupportedSeatSetupTypes,
    IReadOnlyCollection<PublicWaterbusServiceSeatTypeDto> SeatTypes);

public sealed class GetPublicWaterbusServiceCatalogRequestUseCase
{
    private readonly IApplicationDbContext _context;

    public GetPublicWaterbusServiceCatalogRequestUseCase(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyCollection<PublicWaterbusServiceCatalogDto>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var services = await _context.WaterbusServices
            .AsNoTracking()
            .Include(x => x.SeatTypePrices)
                .ThenInclude(x => x.SeatType)
            .Where(x =>
                x.IsActive
                && x.SeatTypePrices.Any(price => price.IsActive && price.SeatType.IsActive))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Code)
            .ToArrayAsync(cancellationToken);

        return services
            .Select(service =>
            {
                var seatTypes = service.SeatTypePrices
                    .Where(x => x.IsActive && x.SeatType.IsActive)
                    .OrderBy(x => x.SeatType.DisplayOrder)
                    .ThenBy(x => x.SeatType.Code)
                    .Select(x => new PublicWaterbusServiceSeatTypeDto(
                        x.SeatTypeId,
                        x.SeatType.Code,
                        x.SeatType.Name,
                        x.PriceModifier))
                    .ToArray();

                var seatTypeCodes = seatTypes
                    .Select(x => x.Code)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var supportedSeatSetupTypes = new List<SeatSetupType>();

                if (seatTypeCodes.Contains("STANDARD"))
                {
                    supportedSeatSetupTypes.Add(SeatSetupType.FullStandard);
                }

                if (seatTypeCodes.Contains("STANDARD") && seatTypeCodes.Contains("VIP"))
                {
                    supportedSeatSetupTypes.Add(SeatSetupType.StandardAndVip);
                }

                return new PublicWaterbusServiceCatalogDto(
                    service.Id,
                    service.Code,
                    service.Name,
                    service.Description,
                    service.BookingMode,
                    supportedSeatSetupTypes,
                    seatTypes);
            })
            .ToArray();
    }
}
