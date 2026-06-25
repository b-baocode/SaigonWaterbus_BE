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

        return services
            .Select(service => new PublicWaterbusServiceCatalogDto(
                service.Id,
                service.Code,
                service.Name,
                service.Description,
                service.BookingMode,
                WaterbusServiceSupport.GetSupportedSeatSetupTypes(service.BookingMode),
                WaterbusServiceSupport.GetSeatTypesForBookingMode(service.BookingMode)
                    .Select(x => new PublicWaterbusServiceSeatTypeDto(
                        x.SeatTypeId,
                        x.Code,
                        x.Name))
                    .ToArray()))
            .ToArray();
    }
}
