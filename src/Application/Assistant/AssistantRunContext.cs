using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaigonWaterbus.Application.Assistant;

/// <summary>Một ghế trợ lý đã chọn giúp khách. Chỉ mang những gì FE cần để vẽ lại và giữ ghế.</summary>
public sealed record AssistantPickedSeat(
    string SeatNumber,
    int Deck,
    decimal Price,
    string? SeatTypeName);

/// <summary>
/// Chuyến trợ lý đã chọn giúp khách. Tên field CỐ Ý trùng với TripSummaryDto mà FE nhận từ
/// <c>/api/trips/search</c>, để FE gán thẳng vào <c>selectedDepartureTrip</c> mà không phải ánh xạ
/// lại — form đang đọc đúng những tên này để hiện giờ chạy và mã chuyến.
/// </summary>
public sealed record AssistantPickedTrip(
    Guid TripId,
    string TripCode,
    DateTimeOffset? FromStopScheduledDeparture,
    DateTimeOffset? ToStopScheduledArrival,
    decimal? MinPrice,
    int AvailableSeats);

/// <summary>
/// Thay đổi mà trợ lý muốn ghi vào form đặt vé đang mở bên FE. Trả kèm câu trả lời chat,
/// FE tự merge vào draft của nó.
///
/// HỢP ĐỒNG: field nào khác null thì FE GHI ĐÈ, null nghĩa là "lượt này không đụng tới".
/// Không có trường phân loại — thêm field mới ở đây thì FE không phải sửa gì, và một lượt có thể
/// vừa điền thông tin vừa chọn ghế.
///
/// VÌ SAO KHÔNG ĐỂ FE TỰ ĐỌC TEXT: nếu để model viết "A12, A14" trong câu trả lời rồi FE parse
/// chuỗi thì sai một dấu phẩy là ghi nhầm ghế. Ở đây mọi giá trị đều do SERVER kiểm rồi mới phát:
/// tên ga phải khớp bến thật, ngày phải hợp lệ, ghế phải còn trống trên sơ đồ ghế.
/// </summary>
public sealed record AssistantDraftPatch(
    string? ServiceType = null,
    string? DepartureDate = null,
    string? ReturnDate = null,
    bool? IsRoundTrip = null,
    string? FromStationCode = null,
    string? FromStationName = null,
    string? ToStationCode = null,
    string? ToStationName = null,
    int? AdultCount = null,
    int? ChildCount = null,
    int? InfantCount = null,
    AssistantPickedTrip? Trip = null,
    string? TripCode = null,
    IReadOnlyList<AssistantPickedSeat>? Seats = null)
{
    /// <summary>
    /// Mọi field đều null = không có gì để gửi cho FE. [JsonIgnore] vì record này serialize thẳng
    /// ra response — thiếu nó thì client nhận thêm một trường "isEmpty" vô nghĩa.
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty =>
        ServiceType is null && DepartureDate is null && ReturnDate is null && IsRoundTrip is null
        && FromStationCode is null && FromStationName is null
        && ToStationCode is null && ToStationName is null
        && AdultCount is null && ChildCount is null && InfantCount is null
        && Trip is null && TripCode is null && Seats is null;

    /// <summary>Đè <paramref name="next"/> lên bản này; field null của next thì giữ giá trị cũ.</summary>
    public AssistantDraftPatch MergeWith(AssistantDraftPatch next) => new(
        next.ServiceType ?? ServiceType,
        next.DepartureDate ?? DepartureDate,
        next.ReturnDate ?? ReturnDate,
        next.IsRoundTrip ?? IsRoundTrip,
        next.FromStationCode ?? FromStationCode,
        next.FromStationName ?? FromStationName,
        next.ToStationCode ?? ToStationCode,
        next.ToStationName ?? ToStationName,
        next.AdultCount ?? AdultCount,
        next.ChildCount ?? ChildCount,
        next.InfantCount ?? InfantCount,
        next.Trip ?? Trip,
        next.TripCode ?? TripCode,
        next.Seats ?? Seats);
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
    string? DepartureDate,
    int AdultCount,
    int ChildCount,
    int InfantCount,
    bool IsSightseeing)
{
    /// <summary>Số ghế cần chọn = người lớn + trẻ em (em bé ngồi cùng người lớn, không tính ghế).</summary>
    public int SeatCount => AdultCount + ChildCount;

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

    /// <summary>Thay đổi tool đã ghi ra cho form, null nếu lượt này trợ lý không đụng vào form.</summary>
    public AssistantDraftPatch? DraftPatch { get; private set; }

    /// <summary>
    /// Gộp thêm thay đổi. Dùng gộp thay vì ghi đè vì một lượt model có thể gọi tool nhiều lần
    /// (điền thông tin trước, chọn ghế sau) — ghi đè sẽ làm mất kết quả lần gọi trước.
    /// </summary>
    public void ApplyPatch(AssistantDraftPatch patch) =>
        DraftPatch = DraftPatch is null ? patch : DraftPatch.MergeWith(patch);
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

        return new AssistantBookingDraft(
            ReadGuid(trip, "tripId") ?? ReadGuid(trip, "id"),
            ReadText(trip, "tripCode") ?? ReadText(trip, "code"),
            ReadText(root, "fromStationCode"),
            ReadText(root, "toStationCode"),
            ReadText(root, "fromStationName"),
            ReadText(root, "toStationName"),
            ReadDate(root, "departureDate"),
            ReadCount(root, "adultCount") ?? 0,
            ReadCount(root, "childCount") ?? 0,
            ReadCount(root, "infantCount") ?? 0,
            string.Equals(ReadText(root, "serviceType"), "Sightseeing", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Ngày trong draft: sai định dạng thì bỏ, coi như khách chưa chọn.</summary>
    private static string? ReadDate(JsonElement root, string property) =>
        ReadText(root, property) is { } text
        && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? text
            : null;

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
