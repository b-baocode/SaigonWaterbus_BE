using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Fares;

/// <summary>
/// Tính giá vé theo quãng đường cho trip Regular (ghế STANDARD):
/// giá = RoundUp(BaseFare + PricePerKm × km, RoundingStep).
/// Km của chặng = tổng distance_from_previous_km của các stop nằm sau trạm lên đến trạm xuống.
/// </summary>
public static class DistanceFareSupport
{
    public const string DistanceFareSeatTypeCode = "STANDARD";
    public const string MissingDistancePropertyName = "distanceFromPreviousKm";
    public const string MissingDistanceReason =
        "Tuyến Waterbus thường chưa nhập đủ số km cho chặng này. Vui lòng cập nhật distanceFromPreviousKm của các bến trên tuyến trước khi bán vé.";

    /// <summary>Trip có dùng giá theo quãng đường không (waterbus thường, không phải sightseeing/charter).</summary>
    public static bool UsesDistanceFare(Trip trip) =>
        trip.Route is not null && UsesDistanceFare(trip.TripType, trip.Route.RouteType);

    /// <summary>
    /// Bán vé theo chặng hay đi nguyên chuyến — dùng khi chỉ có tripType/routeType rời (chưa load
    /// navigation Route). Chỉ waterbus thường mới bán theo chặng; sightseeing/charter đi nguyên chuyến.
    /// </summary>
    public static bool UsesDistanceFare(string tripType, string routeType) =>
        string.Equals(tripType, TripTypes.Regular, StringComparison.OrdinalIgnoreCase)
        && string.Equals(routeType, RouteTypes.Regular, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Km giữa hai trạm theo lộ trình. Trả về null nếu admin chưa nhập đủ km cho mọi chặng
    /// trong khoảng (caller phải chặn bán vé Waterbus thường, không fallback giá ghế).
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
        return Math.Ceiling(raw / step) * step;
    }

    /// <summary>Policy đang active, hoặc mặc định trong code nếu DB chưa có.</summary>
    public static async Task<FarePolicyDto> GetActivePolicyAsync(
        IApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        var policy = await context.Set<FarePolicy>()
            .AsNoTracking()
            .OrderByDescending(x => x.Created)
            .FirstOrDefaultAsync(cancellationToken);

        return policy is null
            ? FarePolicyDefaults.Dto
            : new FarePolicyDto(policy.Id, policy.BaseFare, policy.PricePerKm,
                policy.RoundingStep, policy.Currency);
    }
}
