using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.WaterbusServices;

public sealed record GetWaterbusServicesRequest(bool IncludeInactive = false);

public sealed record WaterbusServiceDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    int DisplayOrder,
    BookingMode BookingMode);

public sealed class GetWaterbusServicesRequestUseCase
{
    private readonly IApplicationDbContext _context;
    private readonly IUserContext _userContext;

    public GetWaterbusServicesRequestUseCase(
        IApplicationDbContext context,
        IUserContext userContext)
    {
        _context = context;
        _userContext = userContext;
    }

    public async Task<IReadOnlyCollection<WaterbusServiceDto>> ExecuteAsync(
        GetWaterbusServicesRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await WaterbusServiceSupport.EnsureCurrentUserCanViewWaterbusServicesAsync(
            _context,
            _userContext,
            cancellationToken);

        var query = _context.WaterbusServices
            .AsNoTracking()
            .AsQueryable();

        return await WaterbusServiceSupport.ApplyVisibilityFilter(query, actor, request.IncludeInactive)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Id)
            .Select(x => new WaterbusServiceDto(
                x.Id,
                x.Code,
                x.Name,
                x.Description,
                x.IsActive,
                x.DisplayOrder,
                x.BookingMode))
            .ToArrayAsync(cancellationToken);
    }
}
