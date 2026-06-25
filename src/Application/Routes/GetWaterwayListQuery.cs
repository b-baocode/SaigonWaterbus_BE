using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Routes;

public sealed record WaterwaySegmentDto(
    Guid Id,
    string? OsmId,
    string? WaterwayName,
    string WaterwayType,
    decimal TotalLengthKm);

public sealed record GetWaterwayListQuery(
    string? Name = null,
    string? Type = null) : IRequest<IReadOnlyList<WaterwaySegmentDto>>;

public sealed class GetWaterwayListQueryHandler : IRequestHandler<GetWaterwayListQuery, IReadOnlyList<WaterwaySegmentDto>>
{
    public GetWaterwayListQueryHandler() { }

    public Task<IReadOnlyList<WaterwaySegmentDto>> Handle(GetWaterwayListQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<WaterwaySegmentDto>>(Array.Empty<WaterwaySegmentDto>());
    }
}
