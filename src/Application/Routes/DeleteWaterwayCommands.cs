using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;

namespace SaigonWaterbus.Application.Routes;

/// <summary>
/// Xoa mot duong song/kenh khoi mang waterway: xoa TAT CA segment cung nhom
/// (cung OsmId + ten + loai) voi segment co Id truyen vao (id lay tu GET /api/waterways).
/// </summary>
[Authorize(Roles = "Admin")]
public sealed record DeleteWaterwayCommand(Guid Id) : IRequest<DeleteWaterwayResultDto>;

public sealed record DeleteWaterwayResultDto(
    string? OsmId,
    string? WaterwayName,
    string WaterwayType,
    int DeletedSegments);

public sealed class DeleteWaterwayCommandHandler
    : IRequestHandler<DeleteWaterwayCommand, DeleteWaterwayResultDto>
{
    private readonly IApplicationDbContext _context;

    public DeleteWaterwayCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<DeleteWaterwayResultDto> Handle(
        DeleteWaterwayCommand request,
        CancellationToken cancellationToken)
    {
        var anchor = await _context.Set<WaterwaySegment>()
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException($"Waterway segment '{request.Id}' not found.");

        var siblings = await _context.Set<WaterwaySegment>()
            .Where(s => s.OsmId == anchor.OsmId
                && s.WaterwayName == anchor.WaterwayName
                && s.WaterwayType == anchor.WaterwayType)
            .ToListAsync(cancellationToken);

        _context.Set<WaterwaySegment>().RemoveRange(siblings);
        await _context.SaveChangesAsync(cancellationToken);

        return new DeleteWaterwayResultDto(
            anchor.OsmId,
            anchor.WaterwayName,
            anchor.WaterwayType,
            siblings.Count);
    }
}

/// <summary>Xoa TOAN BO mang waterway (dung truoc khi re-import GeoJSON de tranh du lieu cu tron lan).</summary>
[Authorize(Roles = "Admin")]
public sealed record DeleteAllWaterwaysCommand : IRequest<DeleteAllWaterwaysResultDto>;

public sealed record DeleteAllWaterwaysResultDto(int DeletedSegments);

public sealed class DeleteAllWaterwaysCommandHandler
    : IRequestHandler<DeleteAllWaterwaysCommand, DeleteAllWaterwaysResultDto>
{
    private readonly IApplicationDbContext _context;

    public DeleteAllWaterwaysCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<DeleteAllWaterwaysResultDto> Handle(
        DeleteAllWaterwaysCommand request,
        CancellationToken cancellationToken)
    {
        var segments = await _context.Set<WaterwaySegment>().ToListAsync(cancellationToken);
        _context.Set<WaterwaySegment>().RemoveRange(segments);
        await _context.SaveChangesAsync(cancellationToken);

        return new DeleteAllWaterwaysResultDto(segments.Count);
    }
}
