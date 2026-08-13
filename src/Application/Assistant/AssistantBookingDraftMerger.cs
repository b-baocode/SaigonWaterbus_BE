using System.Text.Json;
using System.Text.Json.Nodes;

namespace SaigonWaterbus.Application.Assistant;

/// <summary>
/// Ghép <see cref="AssistantDraftPatch"/> vào draft client gửi lên rồi trả về DRAFT ĐẦY ĐỦ, để
/// client chỉ việc copy nguyên khối `bookingDraft` của response trước vào request sau — không phải
/// tự viết luật merge.
///
/// VÌ SAO CHUYỂN VIỆC MERGE VỀ SERVER: luật "field khác null thì ghi đè, null thì giữ" nghe đơn
/// giản nhưng client dễ làm sai theo đúng hai cách đắt giá: (a) `Object.assign(draft, patch)` —
/// patch luôn có đủ field nên các null sẽ XOÁ SẠCH thứ khách đã điền (mất `serviceType` là tour
/// ngắm cảnh biến thành waterbus); (b) quên đổi tên `trip`→`selectedDepartureTrip`,
/// `seats`→`selectedSeatsDeparture` nên chuyến/ghế trợ lý chọn rơi mất. Cả hai lỗi đều im lặng.
///
/// VẪN KHÔNG SỞ HỮU CẤU TRÚC FORM: chỉ chạm đúng những khoá nằm trong patch; mọi field lạ của
/// client (stage, passengers, contact, preview...) được GIỮ NGUYÊN vị trí và giá trị. Nhờ vậy FE
/// đổi bước hay thêm trường vẫn không cần BE sửa gì.
/// </summary>
public static class AssistantBookingDraftMerger
{
    /// <summary>Patch dùng tên ngắn, form dùng tên dài — chỉ hai khoá này lệch nhau.</summary>
    private const string TripDraftProperty = "selectedDepartureTrip";
    private const string SeatsDraftProperty = "selectedSeatsDeparture";

    /// <summary>JsonSerializerDefaults.Web = camelCase, khớp với JSON mà endpoint đang trả.</summary>
    private static readonly JsonSerializerOptions NodeOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Trả về draft sau khi áp patch. Null khi client không gửi draft và lượt này cũng không có
    /// thay đổi nào — lúc đó không có gì để client copy.
    /// </summary>
    /// <param name="draftJson">Draft thô client gửi kèm request. Không đọc được thì coi như chưa có.</param>
    public static JsonNode? Merge(string? draftJson, AssistantDraftPatch? patch)
    {
        var root = Parse(draftJson);

        if (patch is null || patch.IsEmpty)
        {
            // Không có thay đổi thì vẫn trả lại nguyên draft cũ, để client LUÔN copy được một chỗ
            // duy nhất thay vì phải tự hỏi "lượt này có bookingDraft không".
            return root;
        }

        root ??= new JsonObject();

        Set(root, "serviceType", patch.ServiceType);
        Set(root, "departureDate", patch.DepartureDate);
        Set(root, "returnDate", patch.ReturnDate);
        SetValue(root, "isRoundTrip", patch.IsRoundTrip);
        Set(root, "fromStationCode", patch.FromStationCode);
        Set(root, "fromStationName", patch.FromStationName);
        Set(root, "toStationCode", patch.ToStationCode);
        Set(root, "toStationName", patch.ToStationName);
        SetValue(root, "adultCount", patch.AdultCount);
        SetValue(root, "childCount", patch.ChildCount);
        SetValue(root, "infantCount", patch.InfantCount);
        Set(root, "tripCode", patch.TripCode);

        if (patch.Trip is not null)
        {
            root[TripDraftProperty] = JsonSerializer.SerializeToNode(patch.Trip, NodeOptions);
        }

        if (patch.Seats is not null)
        {
            root[SeatsDraftProperty] = JsonSerializer.SerializeToNode(patch.Seats, NodeOptions);
        }

        return root;
    }

    private static JsonObject? Parse(string? draftJson)
    {
        if (string.IsNullOrWhiteSpace(draftJson))
        {
            return null;
        }

        try
        {
            // Draft không phải object (client lỡ gửi chuỗi đã stringify, mảng, số...) thì bỏ, coi
            // như chưa có draft — vẫn ghi patch vào một object mới để client có cái mà dùng tiếp.
            return JsonNode.Parse(draftJson) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Ghi khi patch có nói tới field đó; null nghĩa là "lượt này không đụng", giữ giá trị cũ.</summary>
    private static void Set(JsonObject root, string property, string? value)
    {
        if (value is not null)
        {
            root[property] = JsonValue.Create(value);
        }
    }

    /// <summary>
    /// Bản cho kiểu giá trị. Tách khỏi <see cref="Set(JsonObject, string, string?)"/> vì
    /// <c>Nullable&lt;T&gt;</c> không thoả ràng buộc <c>notnull</c> của generic — và ở đây phải
    /// phân biệt "false/0 thật" với "không có", nên không gộp bằng object được.
    /// </summary>
    private static void SetValue<T>(JsonObject root, string property, T? value)
        where T : struct
    {
        if (value.HasValue)
        {
            root[property] = JsonValue.Create(value.Value);
        }
    }
}
