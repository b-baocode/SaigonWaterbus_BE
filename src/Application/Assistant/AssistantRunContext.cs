using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SaigonWaterbus.Application.Bookings;

namespace SaigonWaterbus.Application.Assistant;

/// <summary>
/// Một ghế trợ lý đã chọn giúp khách. Tên field CỐ Ý trùng với TripSeatMapSeatDto:
/// <c>SeatNumber</c> để giữ ghế, <c>Row</c>/<c>Column</c>/<c>Deck</c> để trang đặt vé vẽ đúng ô
/// trên lưới sơ đồ ghế. Không mang <c>Status</c> — client tự gán khi tải lại sơ đồ ghế.
/// </summary>
public sealed record AssistantPickedSeat(
    string SeatNumber,
    int Deck,
    string Row,
    int Column,
    decimal Price,
    string? SeatTypeName);

/// <summary>
/// Chuyến trợ lý đã chọn giúp khách. Tên field CỐ Ý trùng với TripSummaryDto mà FE nhận từ
/// <c>/api/trips/search</c>, để FE gán thẳng vào <c>selectedDepartureTrip</c> mà không phải ánh xạ
/// lại — trang đặt vé đang đọc đúng những tên này.
///
/// Đây là TẬP CON của TripSummaryDto, chọn đúng những field trang đặt vé đụng tới ở bước chọn ghế:
/// ba field <c>AvailableSeats</c>/<c>MinPrice</c>/<c>FromStopScheduledDeparture</c> quyết định nút
/// "Tiếp tục" có bật không, <c>TotalSeats</c> để hiện "còn trống x/y", <c>BoatId</c> để tải ảnh tàu,
/// <c>RouteName</c> để tuyến ngắm cảnh tách ra tên bến (tuyến vòng không có chặng).
/// </summary>
public sealed record AssistantPickedTrip(
    Guid TripId,
    string TripCode,
    string RouteName,
    DateTimeOffset? FromStopScheduledDeparture,
    DateTimeOffset? ToStopScheduledArrival,
    decimal? MinPrice,
    int AvailableSeats,
    int TotalSeats,
    Guid? BoatId);

/// <summary>
/// Thay đổi mà trợ lý vừa ghi nhận cho khách. Trả kèm câu trả lời chat; server đã merge sẵn
/// vào bookingDraft nên client thường chỉ cần dùng bản merge đó.
///
/// HỢP ĐỒNG: field nào khác null thì FE GHI ĐÈ, null nghĩa là "lượt này không đụng tới".
/// Không có trường phân loại — thêm field mới ở đây thì FE không phải sửa gì, và một lượt có thể
/// vừa ghi thông tin chặng vừa chọn chuyến và ghế.
///
/// VÌ SAO KHÔNG ĐỂ FE TỰ ĐỌC TEXT: nếu để model viết "A12, A14" trong câu trả lời rồi FE parse
/// chuỗi thì sai một dấu phẩy là ghi nhầm ghế. Ở đây mọi giá trị đều do SERVER kiểm rồi mới phát:
/// tên ga phải khớp bến thật, ngày phải hợp lệ, chuyến phải nằm trong kết quả tìm chuyến, ghế
/// phải còn trống trên sơ đồ ghế.
/// </summary>
public sealed record AssistantDraftPatch(
    string? ServiceType = null,
    string? DepartureDate = null,
    string? ReturnDate = null,
    bool? IsRoundTrip = null,
    Guid? FromStationId = null,
    string? FromStationCode = null,
    string? FromStationName = null,
    Guid? ToStationId = null,
    string? ToStationCode = null,
    string? ToStationName = null,
    AssistantPickedTrip? Trip = null,
    AssistantPickedTrip? ReturnTrip = null,
    IReadOnlyList<AssistantPickedSeat>? Seats = null,
    IReadOnlyList<AssistantPickedSeat>? ReturnSeats = null)
{
    /// <summary>
    /// Mọi field đều null = không có gì để gửi cho FE. [JsonIgnore] vì record này serialize thẳng
    /// ra response — thiếu nó thì client nhận thêm một trường "isEmpty" vô nghĩa.
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty =>
        ServiceType is null && DepartureDate is null && ReturnDate is null && IsRoundTrip is null
        && FromStationId is null && FromStationCode is null && FromStationName is null
        && ToStationId is null && ToStationCode is null && ToStationName is null
        && Trip is null && ReturnTrip is null && Seats is null && ReturnSeats is null;

    /// <summary>Đè <paramref name="next"/> lên bản này; field null của next thì giữ giá trị cũ.</summary>
    public AssistantDraftPatch MergeWith(AssistantDraftPatch next) => new(
        next.ServiceType ?? ServiceType,
        next.DepartureDate ?? DepartureDate,
        next.ReturnDate ?? ReturnDate,
        next.IsRoundTrip ?? IsRoundTrip,
        next.FromStationId ?? FromStationId,
        next.FromStationCode ?? FromStationCode,
        next.FromStationName ?? FromStationName,
        next.ToStationId ?? ToStationId,
        next.ToStationCode ?? ToStationCode,
        next.ToStationName ?? ToStationName,
        next.Trip ?? Trip,
        next.ReturnTrip ?? ReturnTrip,
        next.Seats ?? Seats,
        next.ReturnSeats ?? ReturnSeats);
}

/// <summary>
/// Phần thông tin đặt vé mà TOOL cần dùng (khác <see cref="AssistantBookingDraftSummary"/> —
/// cái đó chỉ sinh text nhét vào prompt cho model đọc).
///
/// Cố ý tách: dữ liệu ở đây KHÔNG đi qua prompt nên model không đọc và không bịa được, tool lấy
/// thẳng từ draft. Nhờ vậy trợ lý không thể tự nghĩ ra một trip_code lạ để chọn ghế hộ.
/// </summary>
public sealed record AssistantBookingDraft(
    Guid? TripId,
    Guid? ReturnTripId,
    string? FromStationCode,
    string? ToStationCode,
    string? FromStationName,
    string? ToStationName,
    string? DepartureDate,
    string? ReturnDate,
    bool IsSightseeing);

/// <summary>
/// Ngữ cảnh sống trong đúng MỘT lượt chat: thông tin đặt vé khách đang có và chỗ để tool ghi kết quả
/// cần trả ngược về FE. Truyền xuống <see cref="AssistantToolset"/> qua tham số, không đưa vào DI,
/// vì mỗi lượt một ngữ cảnh khác nhau.
/// </summary>
public sealed class AssistantRunContext
{
    public AssistantRunContext(AssistantBookingDraft? bookingDraft) => BookingDraft = bookingDraft;

    public AssistantBookingDraft? BookingDraft { get; }

    /// <summary>Thay đổi tool đã ghi ra, null nếu lượt này trợ lý không đụng tới thông tin đặt vé.</summary>
    public AssistantDraftPatch? DraftPatch { get; private set; }

    /// <summary>
    /// Gộp thêm thay đổi. Dùng gộp thay vì ghi đè vì một lượt model có thể gọi tool nhiều lần
    /// (ghi thông tin trước, chọn chuyến/ghế sau) — ghi đè sẽ làm mất kết quả lần gọi trước.
    /// </summary>
    public void ApplyPatch(AssistantDraftPatch patch) =>
        DraftPatch = DraftPatch is null ? patch : DraftPatch.MergeWith(patch);
}

/// <summary>
/// Đọc draft JSON do client gửi thành <see cref="AssistantBookingDraft"/>.
///
/// Dữ liệu này do CLIENT kiểm soát nên chỉ lấy đúng vài trường và ép kiểu chặt: ngày phải đúng
/// định dạng, mã ga cắt ngắn và bỏ ký tự lạ. Giá trị sai thì bỏ — coi như khách chưa có, trợ lý
/// sẽ hỏi lại.
/// </summary>
public static class AssistantBookingDraftReader
{
    private const int MaxTextLength = 60;

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

        return new AssistantBookingDraft(
            ReadTripId(root, "selectedDepartureTrip"),
            ReadTripId(root, "selectedReturnTrip"),
            ReadText(root, "fromStationCode"),
            ReadText(root, "toStationCode"),
            ReadText(root, "fromStationName"),
            ReadText(root, "toStationName"),
            ReadDate(root, "departureDate"),
            ReadDate(root, "returnDate"),
            string.Equals(ReadText(root, "serviceType"), BookingServiceTypes.Sightseeing, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Id chuyến của một chiều. Chỉ lấy đúng <c>tripId</c> trong object chuyến (nhận cả <c>id</c>
    /// cho client cũ); giá trị không parse được thành Guid thì coi như khách chưa chọn chuyến.
    /// </summary>
    private static Guid? ReadTripId(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var trip) || trip.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var raw = ReadText(trip, "tripId") ?? ReadText(trip, "id");
        return raw is not null && Guid.TryParse(raw, out var value) ? value : null;
    }

    /// <summary>Ngày trong draft: sai định dạng thì bỏ, coi như khách chưa chọn.</summary>
    private static string? ReadDate(JsonElement root, string property) =>
        ReadText(root, property) is { } text
        && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? text
            : null;

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
}
