using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.WaterbusServices;

public sealed record PublicWaterbusServiceSeatTypeDto(
    Guid SeatTypeId,
    string Code,
    string Name);

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
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Code)
            .ToArrayAsync(cancellationToken);

        var seatTypes = await _context.SeatTypes
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Code)
            .Select(x => new PublicWaterbusServiceSeatTypeDto(
                x.Id,
                x.Code,
                x.Name))
            .ToArrayAsync(cancellationToken);

        var seatTypeCodes = seatTypes
            .Select(x => x.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var supportedSeatSetupTypes = new List<SeatSetupType>();

        if (seatTypeCodes.Contains("STANDARD"))
        {
            supportedSeatSetupTypes.Add(SeatSetupType.FullStandard);
        }

        if (seatTypeCodes.Contains("STANDARD")
            && seatTypeCodes.Any(code => !string.Equals(code, "STANDARD", StringComparison.OrdinalIgnoreCase)))
        {
            supportedSeatSetupTypes.Add(SeatSetupType.StandardAndVip);
        }

        return services
            .Select(service => new PublicWaterbusServiceCatalogDto(
                service.Id,
                service.Code,
                service.Name,
                service.Description,
                service.BookingMode,
                supportedSeatSetupTypes,
                seatTypes))
            .ToArray();
    }
}
