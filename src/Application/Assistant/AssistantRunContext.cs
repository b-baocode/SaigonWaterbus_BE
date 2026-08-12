using System.Globalization;
using System.Text.Json;

namespace SaigonWaterbus.Application.Assistant;

/// <summary>Một ghế trợ lý đã chọn giúp khách. Chỉ mang những gì FE cần để vẽ lại và giữ ghế.</summary>
public sealed record AssistantPickedSeat(
    string SeatNumber,
    int Deck,
    decimal Price,
    string? SeatTypeName);

/// <summary>
/// Thay đổi mà trợ lý muốn ghi vào form đặt vé đang mở bên FE. Trả kèm câu trả lời chat,
/// FE tự merge vào draft của nó.
///
/// VÌ SAO KHÔNG PHẢI TEXT: nếu để model tự đọc "A12, A14" trong câu trả lời rồi FE parse chuỗi
/// thì sai một dấu phẩy là ghi nhầm ghế. Ở đây ghế do SERVER chọn từ sơ đồ ghế thật, model chỉ
/// ra lệnh chọn.
/// </summary>
/// <param name="Kind">Hiện chỉ có "seats-departure" (ghế chiều đi). Thêm loại mới thì đặt tên khác.</param>
public sealed record AssistantDraftPatch(
    string Kind,
    string TripCode,
    IReadOnlyList<AssistantPickedSeat> Seats)
{
    public const string SeatsDeparture = "seats-departure";
}

/// <summary>
/// Phần draft form đặt vé mà TOOL cần dùng (khác <see cref="AssistantBookingDraftSummary"/> —
/// cái đó chỉ sinh text nhét vào prompt cho model đọc).
///
/// Cố ý tách: dữ liệu ở đây KHÔNG đi qua prompt nên model không đọc và không bịa được, tool lấy
/// thẳng từ draft. Nhờ vậy trợ lý không thể tự nghĩ ra một trip_code lạ để chọn ghế hộ.
/// </summary>
public sealed record AssistantBookingDraft(
    Guid? TripId,
    string? TripCode,
    string? FromStationCode,
    string? ToStationCode,
    string? FromStationName,
    string? ToStationName,
    int SeatCount)
{
    /// <summary>Số ghế cần chọn = người lớn + trẻ em (em bé ngồi cùng người lớn, không tính ghế).</summary>
    public bool CanPickSeats => TripId is not null && SeatCount > 0;
}

/// <summary>
/// Ngữ cảnh sống trong đúng MỘT lượt chat: dữ liệu form khách đang mở và chỗ để tool ghi kết quả
/// cần trả ngược về FE. Truyền xuống <see cref="AssistantToolset"/> qua tham số, không đưa vào DI,
/// vì mỗi lượt một ngữ cảnh khác nhau.
/// </summary>
public sealed class AssistantRunContext
{
    public AssistantRunContext(AssistantBookingDraft? bookingDraft) => BookingDraft = bookingDraft;

    public AssistantBookingDraft? BookingDraft { get; }

    /// <summary>Kết quả tool chọn ghế đã ghi ra, null nếu lượt này trợ lý không chọn ghế.</summary>
    public AssistantDraftPatch? DraftPatch { get; private set; }

    public void SetDraftPatch(AssistantDraftPatch patch) => DraftPatch = patch;
}

/// <summary>
/// Đọc draft JSON do client gửi thành <see cref="AssistantBookingDraft"/>.
///
/// Dữ liệu này do CLIENT kiểm soát nên chỉ lấy đúng vài trường và ép kiểu chặt: Guid phải parse
/// được, mã ga cắt ngắn và bỏ ký tự lạ, số khách phải nằm trong khoảng hợp lý. Giá trị sai thì
/// bỏ — coi như khách chưa có, tool sẽ nhắc khách thao tác trên form.
/// </summary>
public static class AssistantBookingDraftReader
{
    private const int MaxTextLength = 60;
    private const int MaxPassengerCount = 100;

    public static AssistantBookingDraft? Read(string? draftJson)
    {
        if (string.IsNullOrWhiteSpace(draftJson))
        {
            return null;
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(draftJson);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var trip = root.TryGetProperty("selectedDepartureTrip", out var tripElement)
                   && tripElement.ValueKind == JsonValueKind.Object
            ? tripElement
            : default;

        var seatCount = (ReadCount(root, "adultCount") ?? 0) + (ReadCount(root, "childCount") ?? 0);

        return new AssistantBookingDraft(
            ReadGuid(trip, "tripId") ?? ReadGuid(trip, "id"),
            ReadText(trip, "tripCode") ?? ReadText(trip, "code"),
            ReadText(root, "fromStationCode"),
            ReadText(root, "toStationCode"),
            ReadText(root, "fromStationName"),
            ReadText(root, "toStationName"),
            seatCount);
    }

    private static Guid? ReadGuid(JsonElement element, string property) =>
        ReadText(element, property) is { } text && Guid.TryParse(text, out var value) ? value : null;

    private static string? ReadText(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var raw = value.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = new string(raw.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length == 0)
        {
            return null;
        }

        return cleaned.Length <= MaxTextLength ? cleaned : cleaned[..MaxTextLength];
    }

    /// <summary>Nhận cả số lẫn chuỗi vì FE giữ vài ô đếm dưới dạng giá trị input.</summary>
    private static int? ReadCount(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element))
        {
            return null;
        }

        var parsed = element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(
                element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => (int?)null,
        };

        return parsed is >= 0 and <= MaxPassengerCount ? parsed : null;
    }
}
