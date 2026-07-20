using SaigonWaterbus.Domain.Constants;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Trips;

/// <summary>
/// Tau va tuyen phai cung hang dich vu: tau ghe thuong (FullStandard) chay tuyen Regular,
/// tau co ghe VIP (StandardAndVip) chay tuyen SightseeingLoop. Khong cho dung lan nhau.
/// Cac loai tuyen khac (Charter, CharterReference) khong bi rang buoc.
/// </summary>
internal static class BoatRouteCompatibilitySupport
{
    public static SeatSetupType? RequiredSeatSetupType(string routeType) =>
        string.Equals(routeType, RouteTypes.Regular, StringComparison.OrdinalIgnoreCase) ? SeatSetupType.FullStandard
        : string.Equals(routeType, RouteTypes.SightseeingLoop, StringComparison.OrdinalIgnoreCase) ? SeatSetupType.StandardAndVip
        : null;

    public static bool IsCompatible(string routeType, SeatSetupType boatSeatSetupType) =>
        RequiredSeatSetupType(routeType) is not { } required || required == boatSeatSetupType;

    public static string BuildIncompatibleMessage(string routeType, SeatSetupType boatSeatSetupType) =>
        string.Equals(routeType, RouteTypes.SightseeingLoop, StringComparison.OrdinalIgnoreCase)
            ? $"Tuyến ngắm cảnh chỉ chạy được bằng tàu có ghế VIP (SeatSetupType = {SeatSetupType.StandardAndVip}), "
                + $"tàu này là {boatSeatSetupType}."
            : $"Tuyến thường chỉ chạy được bằng tàu ghế thường (SeatSetupType = {SeatSetupType.FullStandard}), "
                + $"tàu này là {boatSeatSetupType}.";
}
