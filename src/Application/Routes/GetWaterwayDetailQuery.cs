using SaigonWaterbus.Application.Common.Interfaces;

namespace SaigonWaterbus.Application.Routes;

public sealed record WaterwayCoordinateDto(double Longitude, double Latitude);

public sealed record WaterwaySegmentDetailDto(int SegmentOrder, decimal LengthKm, IReadOnlyList<WaterwayCoordinateDto> Coordinates);

public sealed record WaterwayDetailDto(
    string? OsmId,
    string? WaterwayName,
    string WaterwayType,
    decimal TotalLengthKm,
    int SegmentCount,
    IReadOnlyList<WaterwaySegmentDetailDto> Segments);

public sealed record GetWaterwayDetailQuery(Guid Id) : IRequest<WaterwayDetailDto?>;

public sealed class GetWaterwayDetailQueryHandler : IRequestHandler<GetWaterwayDetailQuery, WaterwayDetailDto?>
{
    public GetWaterwayDetailQueryHandler() { }

    public Task<WaterwayDetailDto?> Handle(GetWaterwayDetailQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult<WaterwayDetailDto?>(null);
    }
}
