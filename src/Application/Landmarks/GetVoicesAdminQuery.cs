using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Landmarks;

/// <summary>Danh sách giọng đầy đủ (kể cả inactive) kèm vieneuVoiceId — cho màn admin.</summary>
[Authorize(Roles = "Admin")]
public sealed record GetVoicesAdminQuery : IRequest<IReadOnlyList<VoiceAdminDto>>;

public sealed class GetVoicesAdminQueryHandler : IRequestHandler<GetVoicesAdminQuery, IReadOnlyList<VoiceAdminDto>>
{
    private readonly IApplicationDbContext _context;

    public GetVoicesAdminQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<VoiceAdminDto>> Handle(GetVoicesAdminQuery request, CancellationToken cancellationToken)
    {
        var voices = await _context.Set<Voice>()
            .OrderBy(v => v.DisplayOrder).ThenBy(v => v.Name)
            .ToListAsync(cancellationToken);

        return voices.Select(VoiceAdminDto.From).ToList();
    }
}
