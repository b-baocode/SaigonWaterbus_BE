using System.Text.Json.Nodes;
using SaigonWaterbus.Application.Bookings;

namespace SaigonWaterbus.Application.Assistant;

/// <summary>Thông tin đặt vé khách đã cung cấp đủ để bước tiếp tới đâu.</summary>
public enum AssistantBookingStage
{
    /// <summary>Chưa đủ chặng hoặc ngày — chưa mời khách sang trang đặt vé được.</summary>
    NotReady = 0,

    /// <summary>Đủ chặng + ngày (ngắm cảnh: chỉ ngày). Sang trang được, dừng ở bước tìm chuyến.</summary>
    Search = 1,

    /// <summary>Đã có chuyến (đủ cả hai chiều nếu khứ hồi). Sang thẳng bước chọn ghế.</summary>
    SelectSeats = 2,

    /// <summary>Đã chọn sẵn ghế. Sang bước chọn ghế để GIỮ CHỖ rồi thanh toán.</summary>
    HoldSeats = 3,
}

/// <summary>
/// Đọc thông tin đặt vé (bản draft đã merge) để biết khách đi tiếp được tới đâu, rồi sinh nút hành động
/// đúng ngữ cảnh thay vì bộ nút tĩnh theo từ khoá.
///
/// KHÔNG CÓ MỨC "SANG THẲNG THANH TOÁN": bước thanh toán đòi ghế đã GIỮ (hold có TTL), mà trợ lý
/// cố ý chỉ chọn ghế chứ không giữ chỗ. Nên đích xa nhất chat đưa khách tới là bước chọn ghế —
/// khác biệt là ghế đã điền sẵn, khách chỉ còn bấm giữ chỗ.
/// </summary>
public static class AssistantBookingReadiness
{
    /// <summary>Trang đặt vé tương ứng với loại dịch vụ; dùng làm route cho nút hành động.</summary>
    public const string WaterbusRoute = "/waterbus-booking";
    public const string SightseeingRoute = "/watersightseeing-booking";

    /// <summary>Type riêng để client phân biệt với các nút điều hướng chung chung.</summary>
    public const string OpenBookingActionType = "open-booking";

    public static AssistantBookingStage Resolve(JsonNode? draft)
    {
        if (draft is not JsonObject root)
        {
            return AssistantBookingStage.NotReady;
        }

        // Tour ngắm cảnh chạy vòng nên KHÔNG có chặng — đòi bến ở đây thì tour luôn kẹt ở NotReady.
        var isSightseeing = string.Equals(Text(root, "serviceType"), BookingServiceTypes.Sightseeing, StringComparison.OrdinalIgnoreCase);
        var hasDate = Text(root, "departureDate") is not null;
        var hasLeg = isSightseeing || (Text(root, "fromStationCode") is not null && Text(root, "toStationCode") is not null);

        if (!hasDate || !hasLeg)
        {
            return AssistantBookingStage.NotReady;
        }

        if (!HasTrip(root, "selectedDepartureTrip"))
        {
            return AssistantBookingStage.Search;
        }

        // Khứ hồi mà mới có chuyến đi thì vẫn còn việc phải chọn ở bước tìm chuyến, chưa nên mời
        // khách sang thẳng bước ghế.
        if (ReadBool(root, "isRoundTrip") == true && !HasTrip(root, "selectedReturnTrip"))
        {
            return AssistantBookingStage.Search;
        }

        var seats = (root["selectedSeatsDeparture"] as JsonArray)?.Count ?? 0;
        return seats > 0 ? AssistantBookingStage.HoldSeats : AssistantBookingStage.SelectSeats;
    }

    private static bool HasTrip(JsonObject root, string property) =>
        root[property] is JsonObject trip && Text(trip, "tripId") is not null;

    private static bool? ReadBool(JsonObject root, string property) =>
        root[property] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;

    /// <summary>
    /// Nút mời khách sang trang đặt vé, hoặc null khi thông tin chưa đủ.
    /// </summary>
    /// <param name="english">Nhãn theo ngôn ngữ khách đang dùng.</param>
    public static AssistantAction? BuildAction(JsonNode? draft, bool english)
    {
        var stage = Resolve(draft);
        if (stage == AssistantBookingStage.NotReady)
        {
            return null;
        }

        var isSightseeing = draft is JsonObject root
            && string.Equals(Text(root, "serviceType"), BookingServiceTypes.Sightseeing, StringComparison.OrdinalIgnoreCase);
        var route = isSightseeing ? SightseeingRoute : WaterbusRoute;

        var label = stage switch
        {
            AssistantBookingStage.HoldSeats => english ? "Hold seats & pay" : "Giữ ghế & thanh toán",
            AssistantBookingStage.SelectSeats => english ? "Choose seats" : "Chọn ghế",
            _ => english ? "View trips" : "Xem chuyến",
        };

        // Step: client mở thẳng bước tương ứng của wizard đặt vé (1 = tìm chuyến, 2 = chọn ghế).
        var step = stage == AssistantBookingStage.Search ? 1 : 2;
        return new AssistantAction(label, route, OpenBookingActionType, step);
    }

    private static string? Text(JsonObject root, string property) =>
        root[property] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
}
