using System.Globalization;
using System.Text;
using System.Text.Json;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Stations;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Entities;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Application.Assistant;

/// <summary>
/// Bộ công cụ (tool) mà trợ lý ảo được phép gọi. Mỗi tool là wrapper mỏng quanh
/// một query MediatR sẵn có — gọi thẳng trong process, không đi vòng qua HTTP.
///
/// Nguyên tắc v1: CHỈ ĐỌC. Không tool nào ghi dữ liệu (đặt vé, thanh toán...).
/// Tool nào đụng dữ liệu người dùng phải lấy userId từ JWT ở tầng Web, không nhận
/// từ tham số do model sinh ra.
/// </summary>
public sealed class AssistantToolset
{
    private readonly ISender _sender;
    private readonly IApplicationDbContext _context;

    public AssistantToolset(ISender sender, IApplicationDbContext context)
    {
        _sender = sender;
        // Bảng giá thuê tàu chỉ có query dành cho Admin/Manager, mà trợ lý chạy ẩn danh —
        // nên đọc thẳng bảng (chỉ đọc) thay vì nới quyền của query đó.
        _context = context;
    }

    public IReadOnlyList<ChatToolDefinition> Definitions { get; } = BuildDefinitions();

    public async Task<string> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken)
    {
        try
        {
            return name switch
            {
                "list_stations" => await ListStationsAsync(cancellationToken),
                "search_trips" => await SearchTripsAsync(arguments, cancellationToken),
                "get_charter_prices" => await GetCharterPricesAsync(cancellationToken),
                _ => Error($"Tool '{name}' không tồn tại."),
            };
        }
        catch (Exception ex)
        {
            // Trả lỗi về cho model dưới dạng dữ liệu thay vì ném ra ngoài, để model
            // có thể xin lỗi khách một cách tự nhiên thay vì cả request 500.
            return Error($"Lỗi khi chạy tool '{name}': {ex.Message}");
        }
    }

    private async Task<string> ListStationsAsync(CancellationToken cancellationToken)
    {
        var stations = await _sender.Send(new GetStationListQuery(), cancellationToken);
        var simplified = stations
            .Select(s => new { name = s.StationName, code = s.StationCode })
            .ToArray();
        return JsonSerializer.Serialize(new { stations = simplified });
    }

    private async Task<string> SearchTripsAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var fromName = GetString(arguments, "from_station");
        var toName = GetString(arguments, "to_station");
        var dateStr = GetString(arguments, "date");

        if (!DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return Error($"Ngày '{dateStr}' không hợp lệ. Dùng định dạng yyyy-MM-dd (ví dụ 2026-07-25).");
        }

        var stations = await _sender.Send(new GetStationListQuery(), cancellationToken);

        var from = ResolveStation(stations, fromName);
        if (from is null)
        {
            return Error($"Không tìm thấy ga đi '{fromName}'. Các ga hiện có: {StationNames(stations)}");
        }

        var to = ResolveStation(stations, toName);
        if (to is null)
        {
            return Error($"Không tìm thấy ga đến '{toName}'. Các ga hiện có: {StationNames(stations)}");
        }

        var trips = await _sender.Send(new SearchTripsQuery(from.StationId, to.StationId, date), cancellationToken);
        if (trips.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                from = from.StationName,
                to = to.StationName,
                date = dateStr,
                message = "Không có chuyến nào phù hợp.",
                trips = Array.Empty<object>(),
            });
        }

        return JsonSerializer.Serialize(new
        {
            from = from.StationName,
            to = to.StationName,
            date = dateStr,
            trips,
        });
    }

    private static StationDto? ResolveStation(IReadOnlyList<StationDto> stations, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var q = Normalize(query);

        var exact = stations.FirstOrDefault(s => Normalize(s.StationName) == q);
        if (exact is not null)
        {
            return exact;
        }

        return stations.FirstOrDefault(s =>
        {
            var n = Normalize(s.StationName);
            return n.Contains(q, StringComparison.Ordinal) || q.Contains(n, StringComparison.Ordinal);
        });
    }

    /// <summary>Bỏ dấu tiếng Việt + lowercase để so khớp tên ga khoan dung với cách gõ của khách.</summary>
    private static string Normalize(string value)
    {
        var formD = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString()
            .Replace("đ", "d", StringComparison.Ordinal)
            .Trim();
    }

    /// <summary>
    /// Bảng giá thuê nguyên tàu: đơn giá theo (số tầng × đơn vị thuê), kèm đội tàu hiện có
    /// để khách ước lượng sức chứa. Giá chốt cuối vẫn do nhân viên báo, tool chỉ đưa đơn giá.
    /// </summary>
    private async Task<string> GetCharterPricesAsync(CancellationToken cancellationToken)
    {
        var policies = await _context.Set<CharterBoatRentalPricePolicy>()
            .AsNoTracking()
            .OrderBy(x => x.NumberOfDecks).ThenBy(x => x.RentalUnit)
            .Select(x => new
            {
                decks = x.NumberOfDecks,
                unit = x.RentalUnit == BoatRentalUnit.Hour ? "gio" : "ngay",
                unit_price = x.UnitPrice,
                currency = x.Currency,
            })
            .ToListAsync(cancellationToken);

        if (policies.Count == 0)
        {
            return Error("Hệ thống chưa cấu hình bảng giá thuê tàu. Hãy mời khách liên hệ nhân viên "
                       + "để được báo giá, và đừng tự đưa ra con số nào.");
        }

        var fleet = await _context.Boats
            .AsNoTracking()
            .GroupBy(x => x.NumberOfDecks)
            .Select(g => new
            {
                decks = g.Key,
                so_tau = g.Count(),
                suc_chua_min = g.Min(x => x.SeatCount),
                suc_chua_max = g.Max(x => x.SeatCount),
            })
            .OrderBy(x => x.decks)
            .ToListAsync(cancellationToken);

        return JsonSerializer.Serialize(new
        {
            bang_gia_thue = policies,
            doi_tau = fleet,
            cach_tinh = "Tam tinh = don gia x thoi luong thue (thue theo gio thi tinh theo so gio "
                      + "thuc te, theo ngay thi tinh theo so ngay). Chua gom bao hiem va khuyen mai. "
                      + "Bao gia chinh thuc do nhan vien chot sau khi khach gui yeu cau thue tau.",
        });
    }

    private static string StationNames(IReadOnlyList<StationDto> stations) =>
        string.Join(", ", stations.Select(s => s.StationName));

    private static string Error(string message) =>
        JsonSerializer.Serialize(new { error = message });

    private static string GetString(JsonElement args, string property) =>
        args.ValueKind == JsonValueKind.Object
        && args.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static ChatToolDefinition[] BuildDefinitions() =>
    [
        new ChatToolDefinition(
            "list_stations",
            "Lấy danh sách toàn bộ ga/bến tàu của hệ thống. Gọi khi khách hỏi có những ga nào, "
            + "hoặc khi cần biết tên ga hợp lệ trước khi tìm chuyến.",
            ParseSchema("""
            { "type": "object", "properties": {} }
            """)),

        new ChatToolDefinition(
            "search_trips",
            "Tìm các chuyến tàu waterbus theo ga đi, ga đến và ngày. Gọi khi khách hỏi về "
            + "lịch trình, giờ chạy, còn chỗ hay không, giá vé của một chặng.",
            ParseSchema("""
            {
              "type": "object",
              "properties": {
                "from_station": { "type": "string", "description": "Tên ga đi, ví dụ 'Bạch Đằng'." },
                "to_station":   { "type": "string", "description": "Tên ga đến, ví dụ 'Thủ Thiêm'." },
                "date":         { "type": "string", "description": "Ngày đi, định dạng yyyy-MM-dd." }
              },
              "required": ["from_station", "to_station", "date"]
            }
            """)),

        new ChatToolDefinition(
            "get_charter_prices",
            "Lấy bảng giá thuê NGUYÊN CHIẾC tàu (charter) theo số tầng và đơn vị thuê (giờ/ngày), "
            + "kèm đội tàu hiện có. Gọi khi khách hỏi giá thuê tàu, thuê bao trọn chuyến, thuê tàu "
            + "tổ chức tiệc/sự kiện, hoặc hỏi tàu chứa được bao nhiêu người.",
            ParseSchema("""
            { "type": "object", "properties": {} }
            """)),
    ];

    private static JsonElement ParseSchema(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();
}
