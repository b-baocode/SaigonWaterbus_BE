using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Maintenance;

internal static class StationReferenceSupport
{
    /// <summary>
    /// Id cua cac station dang duoc tham chieu boi bang khac. Cac tac vu clean-data phai giu lai
    /// nhung station nay de khong vi pham rang buoc khoa ngoai.
    /// </summary>
    public static async Task<HashSet<Guid>> GetReferencedStationIdsAsync(
        IApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        var referenced = new HashSet<Guid>();

        referenced.UnionWith(await context.Set<RouteStop>()
            .Select(x => x.StationId).Distinct().ToListAsync(cancellationToken));
        referenced.UnionWith(await context.Set<Landmark>()
            .Select(x => x.StationId).Distinct().ToListAsync(cancellationToken));
        referenced.UnionWith(await context.Set<UserStationAssignment>()
            .Select(x => x.StationId).Distinct().ToListAsync(cancellationToken));
        referenced.UnionWith(await context.Set<BookingItineraryStop>()
            .Select(x => x.StationId).Distinct().ToListAsync(cancellationToken));
        referenced.UnionWith(await context.Set<Booking>()
            .Where(x => x.FromStationId != null)
            .Select(x => x.FromStationId!.Value).Distinct().ToListAsync(cancellationToken));
        referenced.UnionWith(await context.Set<Booking>()
            .Where(x => x.ToStationId != null)
            .Select(x => x.ToStationId!.Value).Distinct().ToListAsync(cancellationToken));
        referenced.UnionWith(await context.Set<GpsTrackingSession>()
            .Where(x => x.StartStationId != null)
            .Select(x => x.StartStationId!.Value).Distinct().ToListAsync(cancellationToken));
        referenced.UnionWith(await context.Set<GpsTrackingSession>()
            .Where(x => x.EndStationId != null)
            .Select(x => x.EndStationId!.Value).Distinct().ToListAsync(cancellationToken));

        return referenced;
    }
}
