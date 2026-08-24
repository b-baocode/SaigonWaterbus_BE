using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.Trips;

/// <summary>
/// Hạn đóng bán vé tính theo GIỜ TÀU RỜI BẾN KHÁCH LÊN, không phải giờ chuyến rời bến đầu tuyến.
/// Chuyến chạy 08:00 từ bến đầu, tới bến giữa lúc 08:40 thì khách lên bến giữa vẫn mua được vé
/// tới 08:20 — trước đây bị chặn từ 07:40 vì tính theo bến đầu.
///
/// Vé đi nguyên chuyến (sightseeing) có bến lên = bến đầu nên kết quả không đổi.
/// Yêu cầu caller đã load trip.TripStops (hoặc trip.Route.RouteStops cho nhánh fallback).
/// </summary>
public static class BookingCutoffSupport
{
    /// <summary>Giờ tàu rời bến khách lên; thiếu dữ liệu lịch trình thì rơi về giờ chuyến.</summary>
    public static DateTimeOffset ResolveBoardingTime(Trip trip, int? fromStopOrder, int? toStopOrder) =>
        TripStopScheduleSupport.ResolveSegmentTimes(trip, fromStopOrder, toStopOrder).Departure;

    /// <summary>Thời điểm cuối cùng booking online/giữ chỗ được phép chờ thanh toán cho chặng.</summary>
    public static DateTimeOffset ResolveBookingDeadline(Trip trip, int? fromStopOrder, int? toStopOrder) =>
        ResolveBoardingTime(trip, fromStopOrder, toStopOrder)
            .Subtract(BookingExpirationPolicy.BookingCutoffBeforeDeparture);

    public static bool IsPastCutoff(Trip trip, int? fromStopOrder, int? toStopOrder, DateTimeOffset now) =>
        ResolveBookingDeadline(trip, fromStopOrder, toStopOrder) <= now;

    /// <summary>Tàu đã rời bến khách lên hay chưa — mốc cuối cùng cho vé bán tại quầy.</summary>
    public static bool IsPastBoarding(Trip trip, int? fromStopOrder, int? toStopOrder, DateTimeOffset now) =>
        ResolveBoardingTime(trip, fromStopOrder, toStopOrder) <= now;

    public static string BoardingPassedMessage() =>
        "Tàu đã rời bến khách lên nên không bán được vé cho chặng này nữa.";

    public static string CutoffMessage() =>
        $"Đã ngừng bán vé cho chặng này (đóng bán trước "
        + $"{BookingExpirationPolicy.BookingCutoffBeforeDeparture.TotalMinutes:0} phút so với giờ tàu rời bến lên).";
}
