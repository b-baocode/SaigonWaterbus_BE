using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Landmarks;

/// <summary>
/// Toàn bộ landmark active kèm audio của giọng khách chọn. App tải một lần rồi tự phát
/// theo vị trí thực (proximity), không phụ thuộc tuyến.
/// Nếu không truyền <paramref name="VoiceId"/> thì dùng giọng mặc định (display_order nhỏ nhất).
/// </summary>
public sealed record GetLandmarksQuery(Guid? VoiceId) : IRequest<IReadOnlyList<LandmarkDto>>;

public sealed class GetLandmarksQueryHandler
    : IRequestHandler<GetLandmarksQuery, IReadOnlyList<LandmarkDto>>
{
    private readonly IApplicationDbContext _context;

    public GetLandmarksQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<LandmarkDto>> Handle(
        GetLandmarksQuery request, CancellationToken cancellationToken)
    {
        var voiceId = request.VoiceId ?? await _context.Set<Voice>()
            .Where(v => v.IsActive)
            .OrderBy(v => v.DisplayOrder).ThenBy(v => v.Name)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var landmarks = await _context.Set<Landmark>()
            .Where(l => l.IsActive)
            .OrderBy(l => l.DisplayOrder)
            .Select(l => new
            {
                Landmark = l,
                Audio = l.Audios.FirstOrDefault(a => a.VoiceId == voiceId),
            })
            .ToListAsync(cancellationToken);

        return landmarks.Select(x => LandmarkDto.From(x.Landmark, x.Audio)).ToList();
    }
}
