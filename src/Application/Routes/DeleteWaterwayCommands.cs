using FluentValidation.Results;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;
using NotFoundException = SaigonWaterbus.Application.Common.Exceptions.NotFoundException;
using ValidationException = SaigonWaterbus.Application.Common.Exceptions.ValidationException;

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

/// <summary>
/// Xoa TAT CA waterway segment co WaterwayType chi dinh (river | canal | custom).
/// Vd xoa het kenh: WaterwayType = "canal".
/// </summary>
[Authorize(Roles = "Admin")]
public sealed record DeleteWaterwaysByTypeCommand(string WaterwayType)
    : IRequest<DeleteWaterwaysByTypeResultDto>;

public sealed record DeleteWaterwaysByTypeResultDto(string WaterwayType, int DeletedSegments);

public sealed class DeleteWaterwaysByTypeCommandHandler
    : IRequestHandler<DeleteWaterwaysByTypeCommand, DeleteWaterwaysByTypeResultDto>
{
    // Cac loai hop le, khop voi NormalizeWaterwayType khi import GeoJSON.
    private static readonly string[] AllowedTypes = ["river", "canal", "custom"];

    private readonly IApplicationDbContext _context;

    public DeleteWaterwaysByTypeCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<DeleteWaterwaysByTypeResultDto> Handle(
        DeleteWaterwaysByTypeCommand request,
        CancellationToken cancellationToken)
    {
        var type = request.WaterwayType?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!AllowedTypes.Contains(type))
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.WaterwayType),
                "WaterwayType phai la 'river', 'canal' hoac 'custom'.")]);
        }

        var segments = await _context.Set<WaterwaySegment>()
            .Where(s => s.WaterwayType == type)
            .ToListAsync(cancellationToken);

        _context.Set<WaterwaySegment>().RemoveRange(segments);
        await _context.SaveChangesAsync(cancellationToken);

        return new DeleteWaterwaysByTypeResultDto(type, segments.Count);
    }
}
