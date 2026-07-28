using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Landmarks;

/// <summary>Toàn bộ landmark (kể cả inactive), kèm mọi audio đã bake — cho màn admin.</summary>
[Authorize(Roles = "Admin")]
public sealed record GetLandmarksAdminQuery : IRequest<IReadOnlyList<LandmarkAdminDto>>;

public sealed class GetLandmarksAdminQueryHandler
    : IRequestHandler<GetLandmarksAdminQuery, IReadOnlyList<LandmarkAdminDto>>
{
    private readonly IApplicationDbContext _context;

    public GetLandmarksAdminQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<LandmarkAdminDto>> Handle(
        GetLandmarksAdminQuery request, CancellationToken cancellationToken)
    {
        var landmarks = await _context.Set<Landmark>()
            .Include(l => l.Audios).ThenInclude(a => a.Voice)
            .OrderBy(l => l.DisplayOrder)
            .ToListAsync(cancellationToken);

        return landmarks.Select(LandmarkAdminDto.From).ToList();
    }
}
