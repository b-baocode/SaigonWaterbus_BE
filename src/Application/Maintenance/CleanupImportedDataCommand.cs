using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Maintenance;

/// <summary>
/// Don du lieu rac sinh ra khi import GeoJSON:
/// - Xoa station khong ten (StationName rong hoac placeholder "Unnamed ferry terminal")
///   va KHONG bi tham chieu boi route stop / booking / landmark / phan cong / GPS session.
///   Station khong ten nhung dang duoc dung se bi bo qua (skip) de tranh loi rang buoc.
/// - Xoa toan bo waterway segment khong co ten (WaterwayName rong).
/// </summary>
[Authorize(Roles = "Admin")]
public sealed record CleanupImportedDataCommand : IRequest<CleanupImportedDataResultDto>;

public sealed record CleanupImportedDataResultDto(
    int DeletedStations,
    int SkippedStations,
    int DeletedWaterwaySegments);

public sealed class CleanupImportedDataCommandHandler
    : IRequestHandler<CleanupImportedDataCommand, CleanupImportedDataResultDto>
{
    private readonly IApplicationDbContext _context;

    public CleanupImportedDataCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<CleanupImportedDataResultDto> Handle(
        CleanupImportedDataCommand request,
        CancellationToken cancellationToken)
    {
        // 1) Station khong ten (khop dinh nghia "unnamed" cua ImportRouteGeoJsonCommand).
        var unnamedStations = await _context.Set<Station>()
            .Where(s => s.StationName == null
                || s.StationName == string.Empty
                || s.StationName.StartsWith("Unnamed"))
            .ToListAsync(cancellationToken);

        // Station dang duoc tham chieu -> khong xoa de tranh loi FK.
        var referencedStationIds = await StationReferenceSupport.GetReferencedStationIdsAsync(
            _context, cancellationToken);

        var deletableStations = unnamedStations
            .Where(s => !referencedStationIds.Contains(s.Id))
            .ToList();
        _context.Set<Station>().RemoveRange(deletableStations);

        // 2) Waterway (song/kenh) khong co ten.
        var unnamedWaterways = await _context.Set<WaterwaySegment>()
            .Where(s => s.WaterwayName == null || s.WaterwayName.Trim() == string.Empty)
            .ToListAsync(cancellationToken);
        _context.Set<WaterwaySegment>().RemoveRange(unnamedWaterways);

        await _context.SaveChangesAsync(cancellationToken);

        return new CleanupImportedDataResultDto(
            deletableStations.Count,
            unnamedStations.Count - deletableStations.Count,
            unnamedWaterways.Count);
    }
}
