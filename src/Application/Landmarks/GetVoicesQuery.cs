using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Landmarks;

/// <summary>Danh sách giọng active cho màn hình khách chọn giọng nghe thuyết minh.</summary>
public sealed record GetVoicesQuery : IRequest<IReadOnlyList<VoiceDto>>;

public sealed class GetVoicesQueryHandler : IRequestHandler<GetVoicesQuery, IReadOnlyList<VoiceDto>>
{
    private readonly IApplicationDbContext _context;

    public GetVoicesQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<VoiceDto>> Handle(GetVoicesQuery request, CancellationToken cancellationToken)
    {
        var voices = await _context.Set<Voice>()
            .Where(v => v.IsActive)
            .OrderBy(v => v.DisplayOrder).ThenBy(v => v.Name)
            .ToListAsync(cancellationToken);

        return voices.Select(VoiceDto.From).ToList();
    }
}
