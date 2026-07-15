using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Fares;

/// <summary>
/// Tính giá vé theo quãng đường cho trip Regular (ghế STANDARD):
/// giá = RoundUp(BaseFare + PricePerKm × km, RoundingStep), áp giá sàn MinFare nếu có.
/// Km của chặng = tổng distance_from_previous_km của các stop nằm sau trạm lên đến trạm xuống.
/// </summary>
public static class DistanceFareSupport
{
    public const string DistanceFareSeatTypeCode = "STANDARD";

    /// <summary>Trip có dùng giá theo quãng đường không (waterbus thường, không phải sightseeing/charter).</summary>
    public static bool UsesDistanceFare(Trip trip) =>
        string.Equals(trip.TripType, TripTypes.Regular, StringComparison.OrdinalIgnoreCase)
        && trip.Route is not null
        && string.Equals(trip.Route.RouteType, RouteTypes.Regular, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Km giữa hai trạm theo lộ trình. Trả về null nếu admin chưa nhập đủ km cho mọi chặng
    /// trong khoảng (caller fallback về giá theo loại ghế).
    /// </summary>
    public static decimal? TryComputeSegmentDistanceKm(
        IEnumerable<RouteStop> routeStops,
        int fromStopOrder,
        int toStopOrder)
    {
        if (fromStopOrder >= toStopOrder)
        {
            return null;
        }

        var segments = routeStops
            .Where(rs => rs.StopOrder > fromStopOrder && rs.StopOrder <= toStopOrder)
            .ToList();

        // StopOrder liên tục 1..n trên tuyến — thiếu stop hoặc thiếu km thì không tính được.
        if (segments.Count != toStopOrder - fromStopOrder
            || segments.Any(rs => rs.DistanceFromPreviousKm is null or <= 0))
        {
            return null;
        }

        return segments.Sum(rs => rs.DistanceFromPreviousKm!.Value);
    }

    /// <summary>Giá vé nguyên giá (chưa nhân hệ số loại vé) cho quãng đường distanceKm.</summary>
    public static decimal CalculateFare(FarePolicyDto policy, decimal distanceKm)
    {
        var raw = policy.BaseFare + policy.PricePerKm * distanceKm;

        var step = policy.RoundingStep > 0 ? policy.RoundingStep : 1m;
        var rounded = Math.Ceiling(raw / step) * step;

        if (policy.MinFare is > 0 && rounded < policy.MinFare.Value)
        {
            rounded = policy.MinFare.Value;
        }

        return rounded;
    }

    /// <summary>Policy đang active, hoặc mặc định trong code nếu DB chưa có.</summary>
    public static async Task<FarePolicyDto> GetActivePolicyAsync(
        IApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        var policy = await context.Set<FarePolicy>()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.Created)
            .FirstOrDefaultAsync(cancellationToken);

        return policy is null
            ? FarePolicyDefaults.Dto
            : new FarePolicyDto(policy.Id, policy.BaseFare, policy.PricePerKm,
                policy.RoundingStep, policy.MinFare, policy.Currency);
    }
}
