using System.Globalization;
using System.Text.Json;
using SaigonWaterbus.Application.Common;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Fares;
using SaigonWaterbus.Application.InsurancePackages;
using SaigonWaterbus.Application.Knowledge;
using SaigonWaterbus.Application.Landmarks;
using SaigonWaterbus.Application.Promotions;
using SaigonWaterbus.Application.Routes;
using SaigonWaterbus.Application.Seats;
using SaigonWaterbus.Application.Stations;
using SaigonWaterbus.Application.TicketTypes;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Constants;
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
    /// <summary>Số mã ghế trống đưa làm ví dụ cho mỗi nhóm (tầng × loại ghế).</summary>
    private const int MaxSeatSamples = 8;

    /// <summary>Chặn payload địa danh phình to khi dữ liệu tour guide tăng lên.</summary>
    private const int MaxLandmarks = 25;

    /// <summary>Địa danh quanh tàu: kể vài cái gần nhất là đủ, nói nhiều thì rối người nghe.</summary>
    private const int MaxNearbyLandmarks = 5;

    private const double DefaultNearbyRadiusMeters = 800;

    /// <summary>Xa hơn mức này thì không còn là "cái khách đang nhìn thấy" nữa.</summary>
    private const double MaxNearbyRadiusMeters = 3000;

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

    /// <summary>
    /// Bộ tool rút gọn cho một luồng cụ thể. Truyền null = mở hết (chatbox text).
    ///
    /// Ném nếu tên không tồn tại: sai chính tả một cái tên ở đây thì tool đó lặng lẽ biến mất
    /// khỏi prompt, và chỉ phát hiện được khi khách hỏi trúng câu đó.
    /// </summary>
    public IReadOnlyList<ChatToolDefinition> DefinitionsFor(IReadOnlySet<string>? allowed)
    {
        if (allowed is null)
        {
            return Definitions;
        }

        var unknown = allowed.Where(name => !Definitions.Any(d => d.Name == name)).ToArray();
        if (unknown.Length > 0)
        {
            throw new ArgumentException(
                $"Không có tool nào tên: {string.Join(", ", unknown)}.", nameof(allowed));
        }

        return Definitions.Where(d => allowed.Contains(d.Name)).ToArray();
    }

    /// <param name="allowed">
    /// Danh sách tool được phép ở luồng đang chạy (null = mở hết). Phải chặn ở đây chứ không chỉ
    /// giấu định nghĩa: model vẫn có thể gọi tên tool nó nhớ từ lượt trước trong lịch sử hội thoại.
    /// </param>
    /// <param name="runContext">
    /// Ngữ cảnh của lượt chat hiện tại (form đặt vé khách đang mở). Null ở những luồng không có
    /// form — khi đó tool cần form sẽ báo lỗi tử tế thay vì ném.
    /// </param>
    public async Task<string> ExecuteAsync(
        string name,
        JsonElement arguments,
        IReadOnlySet<string>? allowed,
        AssistantRunContext? runContext,
        CancellationToken cancellationToken)
    {
        if (allowed is not null && !allowed.Contains(name))
        {
            return Error($"Tool '{name}' không dùng được ở đây. Trả lời khách bằng những tool đang có, "
                       + "hoặc nói thẳng là phần này bạn không hỗ trợ.");
        }

        try
        {
            return name switch
            {
                "list_stations" => await ListStationsAsync(arguments, cancellationToken),
                "search_trips" => await SearchTripsAsync(arguments, cancellationToken),
                "get_trip_seat_map" => await GetTripSeatMapAsync(arguments, cancellationToken),
                "pick_seats" => await PickSeatsAsync(arguments, runContext, cancellationToken),
                "get_sightseeing_info" => await GetSightseeingInfoAsync(arguments, cancellationToken),
                "get_pricing_info" => await GetPricingInfoAsync(arguments, cancellationToken),
                "get_promotions" => await GetPromotionsAsync(cancellationToken),
                "get_route_info" => await GetRouteInfoAsync(arguments, cancellationToken),
                "get_charter_prices" => await GetCharterPricesAsync(cancellationToken),
                "search_knowledge" => await SearchKnowledgeAsync(arguments, cancellationToken),
                "get_nearby_landmarks" => await GetNearbyLandmarksAsync(arguments, cancellationToken),
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

    /// <summary>
    /// Không có station_name → danh sách ga rút gọn (tên + mã) để model biết ga hợp lệ.
    /// Có station_name → chi tiết đúng ga đó.
    ///
    /// LƯU Ý PII: StationDto mang theo Managers/Staff (họ tên, liên hệ nhân viên) —
    /// TUYỆT ĐỐI không đưa vào kết quả tool, vì trợ lý chạy ẩn danh.
    /// </summary>
    private async Task<string> ListStationsAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var stations = await _sender.Send(new GetStationListQuery(), cancellationToken);

        var wanted = GetString(arguments, "station_name");
        if (string.IsNullOrWhiteSpace(wanted))
        {
            var simplified = stations
                .Select(s => new { name = s.StationName, code = s.StationCode })
                .ToArray();
            return JsonSerializer.Serialize(new { stations = simplified });
        }

        var station = ResolveStation(stations, wanted);
        if (station is null)
        {
            return Error($"Không tìm thấy ga '{wanted}'. Các ga hiện có: {StationNames(stations)}");
        }

        var detail = await _sender.Send(new GetStationDetailQuery(station.StationId), cancellationToken);
        return JsonSerializer.Serialize(new
        {
            name = detail.StationName,
            code = detail.StationCode,
            address = detail.Address,
            description = detail.Description,
            opening_time = detail.OpeningTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
            closing_time = detail.ClosingTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
            is_waterbus_station = detail.IsWaterbusStation,
            status = detail.Status,
            tien_ich = new
            {
                phong_cho = detail.HasWaitingArea,
                bai_do_xe = detail.HasParking,
                quay_ve = detail.HasTicketCounter,
            },
        });
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
    private static string Normalize(string value) => VietnameseTextSupport.Normalize(value);

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

    /// <summary>
    /// Sơ đồ ghế của một chuyến. Model chỉ biết trip_code (lấy từ kết quả search_trips),
    /// nên tool tự tra ra TripId.
    ///
    /// TÓM TẮT, KHÔNG DUMP: query trả về nguyên danh sách ghế (~11 KB JSON cho tàu 80 ghế)
    /// — nhồi hết vào LLM thì vừa chậm vừa tốn token mà model cũng không đọc nổi. Ở đây gộp
    /// số ghế trống theo tầng × loại ghế, kèm vài mã ghế trống làm ví dụ.
    /// </summary>
    private async Task<string> GetTripSeatMapAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var tripCode = GetString(arguments, "trip_code");
        if (string.IsNullOrWhiteSpace(tripCode))
        {
            return Error("Thiếu trip_code. Gọi search_trips trước để lấy mã chuyến (trip_code).");
        }

        // ToLower() thay vì string.Equals(..., OrdinalIgnoreCase): EF Core dịch được ToLower()
        // sang SQL lower(), còn StringComparison thì không (ném lúc chạy).
        var normalizedCode = tripCode.Trim().ToLowerInvariant();
        var trip = await _context.Set<Trip>()
            .AsNoTracking()
            .Where(t => t.TripCode.ToLower() == normalizedCode)
            .Select(t => new { t.Id, t.TripCode })
            .FirstOrDefaultAsync(cancellationToken);

        if (trip is null)
        {
            return Error($"Không tìm thấy chuyến có mã '{tripCode}'. Gọi search_trips để lấy mã chuyến đúng.");
        }

        // Ghế bán theo chặng: trạng thái ghế phụ thuộc chặng khách đi, nên truyền mã ga nếu có.
        var stations = await _sender.Send(new GetStationListQuery(), cancellationToken);
        var fromCode = ResolveStation(stations, GetString(arguments, "from_station"))?.StationCode;
        var toCode = ResolveStation(stations, GetString(arguments, "to_station"))?.StationCode;

        var map = await _sender.Send(new GetTripSeatMapQuery(trip.Id, fromCode, toCode), cancellationToken);

        var available = map.Seats
            .Where(s => s.Status == GetTripSeatMapQueryHandler.StatusAvailable)
            .ToList();

        var byGroup = available
            .GroupBy(s => new { s.Deck, Type = s.SeatTypeName ?? s.SeatTypeCode, s.BasePrice })
            .OrderBy(g => g.Key.Deck).ThenBy(g => g.Key.Type)
            .Select(g => new
            {
                tang = g.Key.Deck,
                loai_ghe = g.Key.Type,
                // KHÔNG phải giá gốc bảng seat_types: đây là giá của ghế này CHO CHẶNG NÀY,
                // đã tính theo km và đã áp phụ thu ngày, nhưng chưa nhân hệ số loại vé
                // (trẻ em/người cao tuổi...). Đặt tên dài cho model khỏi gọi tắt thành "giá vé".
                gia_ghe_chang_nay_ve_nguoi_lon = g.Key.BasePrice,
                so_ghe_trong = g.Count(),
                vi_du_ma_ghe = g.Take(MaxSeatSamples).Select(s => s.SeatNumber).ToArray(),
            })
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            trip_code = map.TripCode,
            boat_name = map.BoatName,
            route_type = map.RouteType,
            ban_ghe_theo_chang = map.SellsBySegment,
            chang = fromCode is null && toCode is null ? null : new { from = fromCode, to = toCode },
            total_seats = map.TotalSeats,
            available_seats = map.AvailableSeats,
            is_booking_closed = map.IsBookingClosed,
            ghe_trong_theo_tang = byGroup,
            luu_y = (map.SellsBySegment
                ? "Ghe ban theo chang: so ghe trong chi dung cho chang from-to o tren, dung noi chung chung. "
                : "Chuyen nay ban ghe nguyen chuyen, khong theo chang. ")
                + "Truong gia_ghe_chang_nay_ve_nguoi_lon la gia ve NGUOI LON cho ghe do tren chang do; "
                + "ve tre em/nguoi cao tuoi con nhan them he so loai ve (xem get_pricing_info).",
        });
    }

    /// <summary>
    /// Chọn hộ khách ghế trống rồi ĐIỀN vào form đặt vé đang mở trong khung chat.
    ///
    /// Chuyến, chặng và số ghế lấy từ draft form (<see cref="AssistantRunContext"/>), KHÔNG lấy từ
    /// tham số model sinh ra: model chỉ được quyết "chọn ghế kiểu gì", còn "chọn ghế của chuyến
    /// nào, mấy ghế" là sự thật của form. Nhờ vậy trợ lý không thể điền ghế của một chuyến khác.
    ///
    /// Ghế trả về đã kiểm tra còn trống ngay lúc gọi, nhưng KHÔNG được giữ chỗ — khách vẫn phải
    /// bấm "Giữ ghế và tiếp tục" trên form, và lúc đó hệ thống mới kiểm tra lần cuối.
    /// </summary>
    private async Task<string> PickSeatsAsync(
        JsonElement arguments,
        AssistantRunContext? runContext,
        CancellationToken cancellationToken)
    {
        var draft = runContext?.BookingDraft;
        if (draft?.TripId is null)
        {
            return Error("Chua biet khach dang xem chuyen nao. Chi chon ghe duoc khi form dat ve "
                       + "trong chat DA chon xong chuyen di. Hay moi khach chon chuyen tren form truoc.");
        }

        if (draft.SeatCount <= 0)
        {
            return Error("Form chua co so khach nen chua biet can may ghe. Moi khach dien so hanh "
                       + "khach tren form truoc.");
        }

        // Ghế bán theo chặng nên trạng thái ghế phụ thuộc chặng; thiếu mã ga thì thử tra từ tên ga
        // khách đã chọn, không có nữa thì để null = xét cả tuyến (an toàn hơn: ghế bận sẽ bị loại).
        var stations = await _sender.Send(new GetStationListQuery(), cancellationToken);
        var fromCode = draft.FromStationCode ?? ResolveStation(stations, draft.FromStationName ?? string.Empty)?.StationCode;
        var toCode = draft.ToStationCode ?? ResolveStation(stations, draft.ToStationName ?? string.Empty)?.StationCode;

        var map = await _sender.Send(new GetTripSeatMapQuery(draft.TripId.Value, fromCode, toCode), cancellationToken);
        if (map.IsBookingClosed)
        {
            return Error("Chuyen nay da dong ban ve nen khong chon ghe duoc nua. Noi that voi khach "
                       + "va moi khach chon chuyen khac.");
        }

        var available = map.Seats
            .Where(s => s.Status == GetTripSeatMapQueryHandler.StatusAvailable)
            .ToList();

        var deck = GetDouble(arguments, "deck") is { } deckValue ? (int)deckValue : (int?)null;
        if (deck is not null)
        {
            available = available.Where(s => s.Deck == deck).ToList();
        }

        var seatType = GetString(arguments, "seat_type");
        if (!string.IsNullOrWhiteSpace(seatType))
        {
            var wanted = Normalize(seatType);
            available = available
                .Where(s => Normalize(s.SeatTypeName ?? s.SeatTypeCode).Contains(wanted, StringComparison.Ordinal)
                         || Normalize(s.SeatTypeCode).Contains(wanted, StringComparison.Ordinal))
                .ToList();
        }

        if (available.Count < draft.SeatCount)
        {
            return Error($"Chi con {available.Count} ghe trong khop yeu cau, khong du {draft.SeatCount} ghe. "
                       + "Noi that con lai bao nhieu va hoi khach co muon doi tang/loai ghe hay doi chuyen khong.");
        }

        var picked = PickBestSeats(available, draft.SeatCount, GetBool(arguments, "cheapest") == true);
        var seats = picked
            .Select(s => new AssistantPickedSeat(s.SeatNumber, s.Deck, s.BasePrice, s.SeatTypeName))
            .ToArray();

        runContext!.SetDraftPatch(new AssistantDraftPatch(AssistantDraftPatch.SeatsDeparture, map.TripCode, seats));

        return JsonSerializer.Serialize(new
        {
            da_dien_vao_form = true,
            trip_code = map.TripCode,
            ghe_da_chon = seats.Select(s => new
            {
                ma_ghe = s.SeatNumber,
                tang = s.Deck,
                loai_ghe = s.SeatTypeName,
                gia_ve_nguoi_lon = s.Price,
            }).ToArray(),
            ngoi_canh_nhau = IsAdjacentBlock(picked),
            luu_y = "Ghe da duoc dien san vao form trong chat. Bao khach biet da chon ghe nao va "
                  + "nhac khach bam nut giu ghe tren form de sang buoc tiep theo. TUYET DOI khong "
                  + "noi la da giu cho — chua giu, ghe van co the bi nguoi khac lay. Khach muon doi "
                  + "thi khach tu bam lai tren so do ghe, hoac goi lai tool nay voi tang/loai ghe khac.",
        });
    }

    /// <summary>
    /// Chọn <paramref name="count"/> ghế trong danh sách ghế trống.
    ///
    /// Ưu tiên: ngồi cạnh nhau (cùng tầng, cùng hàng, số cột liên tiếp) trước, rồi tới "ghế đẹp" =
    /// tầng cao, hạng cao (lấy giá làm thước đo hạng ghế). Khách đòi rẻ thì đảo ngược tiêu chí giá.
    /// Không đủ ghế liền kề thì lấy ghế rời — thà chọn được còn hơn bắt khách tự mò.
    /// </summary>
    private static IReadOnlyList<TripSeatMapSeatDto> PickBestSeats(
        IReadOnlyList<TripSeatMapSeatDto> available,
        int count,
        bool cheapest)
    {
        var ranked = cheapest
            ? available.OrderBy(s => s.BasePrice).ThenBy(s => s.Deck).ThenBy(s => s.Row).ThenBy(s => s.Column).ToList()
            : available.OrderByDescending(s => s.Deck).ThenByDescending(s => s.BasePrice).ThenBy(s => s.Row).ThenBy(s => s.Column).ToList();

        if (count <= 1)
        {
            return ranked.Take(count).ToList();
        }

        var blocks = new List<List<TripSeatMapSeatDto>>();
        foreach (var group in available.GroupBy(s => new { s.Deck, s.Row }))
        {
            var run = new List<TripSeatMapSeatDto>();
            foreach (var seat in group.OrderBy(s => s.Column))
            {
                if (run.Count > 0 && seat.Column != run[^1].Column + 1)
                {
                    AddIfLongEnough(blocks, run, count);
                    run = [];
                }

                run.Add(seat);
            }

            AddIfLongEnough(blocks, run, count);
        }

        var windows = blocks.Select(block => block.Take(count).ToList()).ToList();
        var best = cheapest
            ? windows.OrderBy(w => w.Sum(s => s.BasePrice)).ThenBy(w => w[0].Deck).FirstOrDefault()
            : windows.OrderByDescending(w => w[0].Deck).ThenByDescending(w => w.Sum(s => s.BasePrice)).FirstOrDefault();

        return best ?? ranked.Take(count).ToList();
    }

    private static void AddIfLongEnough(
        List<List<TripSeatMapSeatDto>> blocks,
        List<TripSeatMapSeatDto> run,
        int count)
    {
        if (run.Count >= count)
        {
            blocks.Add(run);
        }
    }

    private static bool IsAdjacentBlock(IReadOnlyList<TripSeatMapSeatDto> seats) =>
        seats.Count <= 1
        || (seats.All(s => s.Deck == seats[0].Deck && s.Row == seats[0].Row)
            && seats.Select(s => s.Column).OrderBy(x => x).Zip(
                   seats.Select(s => s.Column).OrderBy(x => x).Skip(1),
                   (previous, next) => next - previous)
               .All(gap => gap == 1));

    /// <summary>
    /// Tour ngắm cảnh + địa danh dọc tuyến. Không có date thì chỉ trả địa danh và nhắc model
    /// hỏi ngày. Địa danh gắn theo toạ độ, KHÔNG gắn tuyến — nên chỉ đưa tên + mô tả, bỏ
    /// toạ độ/audio (vô dụng trong hội thoại text và làm phình payload).
    /// </summary>
    private async Task<string> GetSightseeingInfoAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var landmarks = await _sender.Send(new GetLandmarksQuery(null), cancellationToken);
        var landmarkList = landmarks
            .Take(MaxLandmarks)
            .Select(l => new { name = l.LandmarkName, description = l.Description })
            .ToArray();

        var dateStr = GetString(arguments, "date");
        if (string.IsNullOrWhiteSpace(dateStr))
        {
            return JsonSerializer.Serialize(new
            {
                dia_danh = landmarkList,
                tong_so_dia_danh = landmarks.Count,
                message = "Chua co ngay di. Hoi khach muon di ngay nao roi goi lai tool nay kem date.",
            });
        }

        if (!DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return Error($"Ngày '{dateStr}' không hợp lệ. Dùng định dạng yyyy-MM-dd.");
        }

        var trips = await _sender.Send(new SearchSightseeingTripsQuery(date), cancellationToken);

        return JsonSerializer.Serialize(new
        {
            date = dateStr,
            chuyen_ngam_canh = trips,
            message = trips.Count == 0 ? "Ngay nay khong co chuyen ngam canh nao." : null,
            dia_danh = landmarkList,
            tong_so_dia_danh = landmarks.Count,
            luu_y = "Chuyen ngam canh ban ghe NGUYEN CHUYEN (khong theo chang). Dia danh la diem "
                  + "thuyet minh doc duong, khong phai ga len/xuong.",
        });
    }

    /// <summary>
    /// Địa danh quanh MỘT toạ độ, sắp theo khoảng cách. Dùng cho hướng dẫn viên bằng giọng nói:
    /// khách đang ngồi trên tàu chỉ ra ngoài hỏi "toà nhà kia là gì".
    ///
    /// Khác <see cref="GetSightseeingInfoAsync"/> (liệt kê toàn bộ địa danh để giới thiệu tour):
    /// tool này gắn với vị trí THỰC của tàu ngay lúc hỏi.
    ///
    /// Có heading thì tính luôn hướng tương đối (trái/phải/trước/sau) trong code — đừng bắt
    /// model tự làm lượng giác, nó sẽ nói sai bên.
    /// </summary>
    private async Task<string> GetNearbyLandmarksAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var latitude = GetDouble(arguments, "latitude");
        var longitude = GetDouble(arguments, "longitude");

        if (latitude is null || longitude is null)
        {
            return Error("Thiếu latitude/longitude. Toạ độ tàu do hệ thống cung cấp trong ngữ cảnh, "
                       + "không được tự bịa số.");
        }

        var radius = GetDouble(arguments, "radius_meters") ?? DefaultNearbyRadiusMeters;
        radius = Math.Clamp(radius, 50, MaxNearbyRadiusMeters);
        var heading = GetDouble(arguments, "heading_degrees");

        var landmarks = await _sender.Send(new GetLandmarksQuery(null), cancellationToken);

        var nearby = landmarks
            .Select(l => new
            {
                Landmark = l,
                Distance = RouteGeoJsonImportSupport.HaversineMeters(
                    latitude.Value, longitude.Value, (double)l.Latitude, (double)l.Longitude),
                Bearing = BearingDegrees(
                    latitude.Value, longitude.Value, (double)l.Latitude, (double)l.Longitude),
            })
            .Where(x => x.Distance <= radius)
            .OrderBy(x => x.Distance)
            .Take(MaxNearbyLandmarks)
            .Select(x => new
            {
                ten = x.Landmark.LandmarkName,
                mo_ta = x.Landmark.Description,
                khoang_cach_m = (int)Math.Round(x.Distance),
                phia = RelativeSide(x.Bearing, heading),
            })
            .ToArray();

        if (nearby.Length == 0)
        {
            return JsonSerializer.Serialize(new
            {
                dia_danh_gan_day = Array.Empty<object>(),
                ban_kinh_m = (int)radius,
                message = "Khong co dia danh nao trong ban kinh nay. Noi that voi khach la doan nay "
                        + "chua co diem thuyet minh, DUNG bia ten dia danh.",
            });
        }

        return JsonSerializer.Serialize(new
        {
            dia_danh_gan_day = nearby,
            ban_kinh_m = (int)radius,
            luu_y = heading is null
                ? "Khong biet huong mui tau nen truong 'phia' de trong — dung noi trai/phai voi khach."
                : "Truong 'phia' tinh theo huong mui tau, dung duoc de noi 'ben trai/ben phai'.",
        });
    }

    /// <summary>
    /// Mọi thứ liên quan tới giá: công thức tính, loại vé, giá theo loại ghế, phụ thu theo ngày,
    /// gói bảo hiểm. Gộp 5 nguồn vào 1 tool để khỏi tốn thêm vòng gọi LLM cho từng câu hỏi giá.
    /// </summary>
    private async Task<string> GetPricingInfoAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var farePolicy = await _sender.Send(new GetFarePolicyQuery(), cancellationToken);
        var ticketTypes = await _sender.Send(new GetTicketTypeListQuery(), cancellationToken);
        var seatTypes = await _sender.Send(new GetSeatTypeListQuery(), cancellationToken);
        var insurance = await _sender.Send(new GetInsurancePackageListQuery(), cancellationToken);

        object? adjustment = null;
        var dateStr = GetString(arguments, "date");
        if (!string.IsNullOrWhiteSpace(dateStr))
        {
            if (!DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return Error($"Ngày '{dateStr}' không hợp lệ. Dùng định dạng yyyy-MM-dd.");
            }

            var effective = await _sender.Send(new GetEffectiveFareAdjustmentQuery(date), cancellationToken);
            adjustment = effective is null
                ? new { date = dateStr, co_phu_thu = false }
                : new
                {
                    date = dateStr,
                    co_phu_thu = true,
                    ten = effective.Name,
                    loai = effective.Scope,
                    phan_tram_phu_thu = effective.SurchargePercent,
                    he_so = effective.Multiplier,
                };
        }

        return JsonSerializer.Serialize(new
        {
            cong_thuc_gia = new
            {
                gia_co_ban = farePolicy.BaseFare,
                don_gia_moi_km = farePolicy.PricePerKm,
                lam_tron_den = farePolicy.RoundingStep,
                currency = farePolicy.Currency,
                cach_tinh = "Gia ve mot chang = (gia co ban + don gia/km x so km cua chang) x he so loai "
                          + "ghe x he so loai ve, roi lam tron. Neu ngay di co phu thu thi nhan them he so phu thu.",
            },
            loai_ve = ticketTypes.Select(t => new
            {
                code = t.TicketTypeCode,
                name = t.TicketTypeName,
                description = t.Description,
                he_so_gia = t.PriceModifier,
                he_so_gia_ngam_canh = t.SightseeingPriceModifier,
            }).ToArray(),
            loai_ghe = seatTypes.Select(s => new
            {
                code = s.Code,
                name = s.Name,
                gia_goc = s.BasePrice,
                currency = s.Currency,
                pricing_mode = s.PricingMode,
                note = s.PriceNote,
            }).ToArray(),
            phu_thu_theo_ngay = adjustment,
            bao_hiem = insurance.Select(i => new
            {
                code = i.Code,
                name = i.Name,
                ap_dung_cho = i.BookingType,
                bat_buoc = i.IsRequired,
                phi_moi_khach = i.UnitPremiumAmount,
                muc_boi_thuong = i.CoverageAmount,
                currency = i.Currency,
                dieu_kien = i.Conditions,
            }).ToArray(),
            luu_y = "Day la bang gia va cong thuc. Gia CHINH XAC cua mot chuyen cu the phai lay tu "
                  + "search_trips (minPrice da tinh ca phu thu), dung tu nhan tay roi bao khach.",
        });
    }

    /// <summary>
    /// Khuyến mãi đang chạy (chỉ mã công khai). CỐ Ý không có tool kiểm tra một mã bất kỳ:
    /// như vậy khách có thể nhờ trợ lý dò mã giảm giá hàng loạt.
    /// </summary>
    private async Task<string> GetPromotionsAsync(CancellationToken cancellationToken)
    {
        var promotions = await _sender.Send(new GetPublicPromotionListQuery(), cancellationToken);
        if (promotions.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                khuyen_mai = Array.Empty<object>(),
                message = "Hien khong co chuong trinh khuyen mai nao dang chay.",
            });
        }

        return JsonSerializer.Serialize(new
        {
            khuyen_mai = promotions.Select(p => new
            {
                code = p.PromotionCode,
                name = p.PromotionName,
                description = p.Description,
                loai = p.PromotionType.ToString(),
                gia_tri_giam = p.DiscountValue,
                giam_toi_da = p.MaxDiscountAmount,
                don_toi_thieu = p.MinOrderValue,
                hieu_luc_tu = p.ValidFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                hieu_luc_den = p.ValidTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            }).ToArray(),
            luu_y = "Loai Percentage = giam theo phan tram, FixedAmount = giam so tien co dinh. "
                  + "Ma co the con dieu kien rieng, khach nhap ma o buoc thanh toan moi biet chac.",
        });
    }

    /// <summary>
    /// Tuyến và lộ trình. Không có route_name → danh sách tuyến; có → chi tiết kèm các ga theo
    /// thứ tự và số km từng đoạn.
    ///
    /// CHỈ lấy Regular và SightseeingLoop — đó là tuyến khách thật sự đi được. Hai loại kia bị
    /// loại vì đều là chuyện nội bộ của nghiệp vụ thuê tàu: Charter là tuyến hệ thống tự sinh cho
    /// từng booking, còn CharterReference là các đoạn nối rời rạc (0.37–2.99 km) chỉ dùng để ghép
    /// lộ trình khi báo giá. Liệt kê chúng ra làm câu trả lời dài gấp mấy lần mà khách không dùng
    /// được gì. Giá thuê tàu đã có get_charter_prices lo.
    ///
    /// Cũng bỏ RouteGeometry (mảng toạ độ, rất nặng).
    /// </summary>
    private async Task<string> GetRouteInfoAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var routes = (await _sender.Send(new GetRouteListQuery(), cancellationToken))
            .Where(r => r.RouteType == RouteTypes.Regular || r.RouteType == RouteTypes.SightseeingLoop)
            .ToList();

        var wanted = GetString(arguments, "route_name");
        if (string.IsNullOrWhiteSpace(wanted))
        {
            return JsonSerializer.Serialize(new
            {
                tuyen = routes.Select(r => new
                {
                    code = r.RouteCode,
                    name = r.RouteName,
                    loai = r.RouteType,
                    nhan = r.RouteLabel,
                    tong_km = r.BaseDistanceKm,
                    thoi_luong_phut = r.EstimatedDurationMin,
                }).ToArray(),
            });
        }

        var q = Normalize(wanted);
        var route = routes.FirstOrDefault(r => Normalize(r.RouteName) == q || Normalize(r.RouteCode) == q)
                 ?? routes.FirstOrDefault(r => Normalize(r.RouteName).Contains(q, StringComparison.Ordinal)
                                            || Normalize(r.RouteCode).Contains(q, StringComparison.Ordinal));

        if (route is null)
        {
            return Error($"Không tìm thấy tuyến '{wanted}'. Các tuyến hiện có: "
                       + string.Join(", ", routes.Select(r => r.RouteName)));
        }

        var detail = await _sender.Send(new GetRouteDetailQuery(route.RouteId), cancellationToken);
        return JsonSerializer.Serialize(new
        {
            code = detail.RouteCode,
            name = detail.RouteName,
            loai = detail.RouteType,
            description = detail.Description,
            tong_km = detail.BaseDistanceKm,
            thoi_luong_phut = detail.EstimatedDurationMin,
            lo_trinh = detail.Stops.Select(s => new
            {
                thu_tu = s.StopOrder,
                ga = s.StationName,
                km_tu_ga_truoc = s.DistanceFromPreviousKm,
                phut_di_chuyen = s.StandardTravelMin,
                cho_len = s.IsPickupAllowed,
                cho_xuong = s.IsDropoffAllowed,
            }).ToArray(),
        });
    }

    /// <summary>
    /// Tra kho kiến thức do Admin soạn (chính sách, quy định, hướng dẫn) — thứ KHÔNG suy ra được
    /// từ dữ liệu vận hành. Không tìm thấy thì trả found=false để model nói "chưa có thông tin"
    /// thay vì tự nghĩ ra điều khoản.
    /// </summary>
    private async Task<string> SearchKnowledgeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var query = GetString(arguments, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return Error("Thiếu query. Truyền nguyên câu hỏi của khách vào tham số query.");
        }

        var hits = await _sender.Send(new SearchKnowledgeQuery(query), cancellationToken);

        if (hits.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                found = false,
                message = "Khong tim thay muc kien thuc nao khop. Hay noi voi khach la minh chua co "
                        + "thong tin ve noi dung nay va moi khach lien he nhan vien. TUYET DOI khong "
                        + "tu suy dien hay bia ra dieu khoan, khong bia so dien thoai/email.",
            });
        }

        return JsonSerializer.Serialize(new
        {
            found = true,
            ket_qua = hits,
            luu_y = "Chi tra loi dua tren noi dung o tren, trich dung y. Neu noi dung khong tra loi "
                  + "duoc dung cau khach hoi thi noi ro la chua co thong tin phan do, dung suy dien them.",
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

    /// <summary>
    /// Số từ tham số tool. Nhận cả dạng chuỗi vì model thỉnh thoảng trả "10.7726" thay vì
    /// 10.7726 dù schema khai number.
    /// </summary>
    private static double? GetDouble(JsonElement args, string property)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDouble(out var number) ? number : null,
            JsonValueKind.String => double.TryParse(
                value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null,
            _ => null,
        };
    }

    /// <summary>Cờ boolean từ tham số tool. Nhận cả "true"/"false" dạng chuỗi cho chắc.</summary>
    private static bool? GetBool(JsonElement args, string property)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) ? parsed : null,
            _ => null,
        };
    }

    /// <summary>Góc phương vị từ điểm 1 tới điểm 2, 0° = chính Bắc, tăng theo chiều kim đồng hồ.</summary>
    private static double BearingDegrees(double lat1, double lon1, double lat2, double lon2)
    {
        var phi1 = double.DegreesToRadians(lat1);
        var phi2 = double.DegreesToRadians(lat2);
        var deltaLambda = double.DegreesToRadians(lon2 - lon1);

        var y = Math.Sin(deltaLambda) * Math.Cos(phi2);
        var x = (Math.Cos(phi1) * Math.Sin(phi2))
              - (Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(deltaLambda));

        return (double.RadiansToDegrees(Math.Atan2(y, x)) + 360) % 360;
    }

    /// <summary>
    /// Địa danh nằm bên nào so với mũi tàu. Không biết mũi tàu quay đâu thì trả null —
    /// thà không nói còn hơn nói nhầm trái thành phải.
    /// </summary>
    private static string? RelativeSide(double bearing, double? heading)
    {
        if (heading is null)
        {
            return null;
        }

        // Đưa về [-180, 180): âm là bên trái, dương là bên phải.
        var relative = ((bearing - heading.Value + 540) % 360) - 180;

        return relative switch
        {
            >= -45 and <= 45 => "phía trước",
            > 45 and <= 135 => "bên phải",
            < -45 and >= -135 => "bên trái",
            _ => "phía sau",
        };
    }

    private static ChatToolDefinition[] BuildDefinitions() =>
    [
        new ChatToolDefinition(
            "list_stations",
            "Danh sách ga/bến tàu, hoặc chi tiết MỘT ga. Gọi không tham số khi khách hỏi có những "
            + "ga nào, hoặc khi cần biết tên ga hợp lệ trước khi tìm chuyến. Truyền station_name khi "
            + "khách hỏi sâu về một ga: ở đâu, địa chỉ, mấy giờ mở/đóng cửa, có bãi đỗ xe, quầy vé "
            + "hay phòng chờ không.",
            ParseSchema("""
            {
              "type": "object",
              "properties": {
                "station_name": {
                  "type": "string",
                  "description": "Bỏ trống để lấy danh sách tất cả ga. Đặt tên ga (ví dụ 'Bạch Đằng') để lấy chi tiết ga đó."
                }
              }
            }
            """)),

        new ChatToolDefinition(
            "get_trip_seat_map",
            "Xem tình hình ghế của MỘT chuyến cụ thể: còn bao nhiêu ghế trống, ở tầng nào, loại ghế "
            + "nào, giá gốc bao nhiêu. Gọi khi khách hỏi sâu về ghế của một chuyến (ghế tầng 2, ghế "
            + "VIP, còn ghế nào...). BẮT BUỘC gọi search_trips trước để lấy trip_code.",
            ParseSchema("""
            {
              "type": "object",
              "properties": {
                "trip_code":    { "type": "string", "description": "Mã chuyến lấy từ kết quả search_trips (trường tripCode)." },
                "from_station": { "type": "string", "description": "Tên ga đi. Bắt buộc với chuyến bán ghế theo chặng, vì ghế trống tính theo chặng." },
                "to_station":   { "type": "string", "description": "Tên ga đến." }
              },
              "required": ["trip_code"]
            }
            """)),

        new ChatToolDefinition(
            "pick_seats",
            "CHỌN HỘ khách ghế trống rồi điền thẳng vào form đặt vé đang mở trong khung chat. Gọi khi "
            + "khách nhờ chọn giúp: \"chọn giùm mình 2 ghế\", \"ghế nào cũng được\", \"cho mình 2 ghế "
            + "cạnh nhau\", \"chọn ghế đẹp giúp mình\". Chuyến, chặng và SỐ GHẾ server tự lấy từ form "
            + "nên KHÔNG cần truyền — chỉ truyền tham số khi khách nói rõ ý thích. Chỉ dùng được khi "
            + "form đã chọn xong chuyến đi; chưa chọn chuyến thì tool báo lỗi và bạn mời khách chọn "
            + "chuyến trước. Tool KHÔNG giữ chỗ: khách vẫn phải bấm nút giữ ghế trên form.",
            ParseSchema("""
            {
              "type": "object",
              "properties": {
                "deck":      { "type": "number",  "description": "Chỉ chọn ghế ở tầng này (1, 2...). Bỏ trống = tầng nào cũng được." },
                "seat_type": { "type": "string",  "description": "Lọc theo loại ghế khách muốn, ví dụ 'VIP'. Bỏ trống = loại nào cũng được." },
                "cheapest":  { "type": "boolean", "description": "true khi khách muốn ghế rẻ nhất. Bỏ trống/false = ưu tiên ngồi cạnh nhau và ghế đẹp (tầng cao, hạng cao)." }
              }
            }
            """)),

        new ChatToolDefinition(
            "get_sightseeing_info",
            "Thông tin dịch vụ NGẮM CẢNH: các chuyến tàu ngắm cảnh trong một ngày và danh sách địa "
            + "danh được thuyết minh dọc đường. Gọi khi khách hỏi tour ngắm cảnh, đi ngắm cảnh sông, "
            + "du lịch trên sông, hoặc dọc tuyến có gì đáng xem. KHÁC hẳn chuyến waterbus thường "
            + "(search_trips) và khác thuê nguyên tàu (get_charter_prices).",
            ParseSchema("""
            {
              "type": "object",
              "properties": {
                "date": { "type": "string", "description": "Ngày muốn đi, định dạng yyyy-MM-dd. Bỏ trống nếu khách chưa nói ngày." }
              }
            }
            """)),

        new ChatToolDefinition(
            "get_pricing_info",
            "Bảng giá và cách tính giá vé: công thức tính theo quãng đường, các loại vé và hệ số giá "
            + "(trẻ em, sinh viên, người cao tuổi...), giá gốc theo loại ghế, phụ thu theo ngày "
            + "(cuối tuần/lễ), và các gói bảo hiểm. Gọi khi khách hỏi giá vé tính thế nào, có giảm giá "
            + "cho trẻ em/sinh viên không, đi ngày lễ có đắt hơn không, có bảo hiểm không. KHÔNG dùng "
            + "để báo giá một chuyến cụ thể — giá chuyến lấy từ search_trips.",
            ParseSchema("""
            {
              "type": "object",
              "properties": {
                "date": { "type": "string", "description": "Ngày cần kiểm tra phụ thu, định dạng yyyy-MM-dd. Bỏ trống nếu khách chỉ hỏi bảng giá chung." }
              }
            }
            """)),

        new ChatToolDefinition(
            "get_promotions",
            "Danh sách chương trình khuyến mãi đang chạy kèm mã giảm giá công khai, mức giảm và thời "
            + "gian hiệu lực. Gọi khi khách hỏi có khuyến mãi, ưu đãi, mã giảm giá nào không.",
            ParseSchema("""
            { "type": "object", "properties": {} }
            """)),

        new ChatToolDefinition(
            "get_route_info",
            "Thông tin TUYẾN khách đi được (waterbus thường và tuyến ngắm cảnh): danh sách tuyến, hoặc "
            + "lộ trình chi tiết của một tuyến (đi qua những ga nào theo thứ tự, mỗi đoạn bao nhiêu km, "
            + "bao nhiêu phút). Gọi khi khách hỏi có những tuyến nào, tuyến đó đi qua đâu, dài bao nhiêu "
            + "km, đi mất bao lâu. KHÔNG dùng cho câu hỏi về thuê nguyên tàu — cái đó gọi get_charter_prices.",
            ParseSchema("""
            {
              "type": "object",
              "properties": {
                "route_name": { "type": "string", "description": "Bỏ trống để lấy danh sách tuyến. Đặt tên hoặc mã tuyến để lấy lộ trình chi tiết." }
              }
            }
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
            "search_knowledge",
            "Tra kho kiến thức của Waterbus: chính sách hoàn vé / huỷ vé / đổi vé, quy định hành lý, "
            + "quy định đi tàu, hướng dẫn đặt vé và thanh toán, điều khoản dịch vụ. Gọi khi khách hỏi "
            + "\"có được...\", \"quy định thế nào\", \"chính sách gì\", \"làm sao để...\" — tức là những câu "
            + "KHÔNG tra được từ dữ liệu ga/chuyến/giá. QUAN TRỌNG: kho kiến thức viết bằng TIẾNG VIỆT "
            + "và tìm bằng khớp từ, nên nếu khách hỏi bằng tiếng Anh (hay tiếng khác) thì phải DỊCH "
            + "câu hỏi sang tiếng Việt trước khi truyền vào query, ví dụ \"How much luggage can I bring?\" "
            + "→ query = \"quy định hành lý mang lên tàu bao nhiêu kg\".",
            ParseSchema("""
            {
              "type": "object",
              "properties": {
                "query": { "type": "string", "description": "Câu hỏi của khách, BẰNG TIẾNG VIỆT (dịch nếu khách hỏi tiếng khác), ví dụ 'huỷ vé trước 1 ngày có được hoàn tiền không'." }
              },
              "required": ["query"]
            }
            """)),

        new ChatToolDefinition(
            "get_charter_prices",
            "Lấy bảng giá thuê NGUYÊN CHIẾC tàu (tiếng Anh: request booking) theo số tầng và đơn vị "
            + "thuê (giờ/ngày), kèm đội tàu hiện có. Gọi khi khách hỏi giá thuê tàu, thuê bao trọn "
            + "chuyến, thuê tàu tổ chức tiệc/sự kiện, hỏi \"request booking\", hoặc hỏi tàu chứa "
            + "được bao nhiêu người.",
            ParseSchema("""
            { "type": "object", "properties": {} }
            """)),

        new ChatToolDefinition(
            "get_nearby_landmarks",
            "Địa danh Ở NGAY QUANH TÀU lúc này, sắp theo khoảng cách gần nhất. Gọi khi khách đang "
            + "đi tàu và hỏi về thứ họ NHÌN THẤY: \"toà nhà kia là gì\", \"bên trái là cầu gì\", "
            + "\"chỗ mình đang đi qua có gì hay\", \"kể về nơi này đi\". Toạ độ tàu và hướng mũi tàu "
            + "đã được cung cấp trong phần ngữ cảnh của cuộc trò chuyện — dùng đúng số đó, TUYỆT ĐỐI "
            + "không tự bịa toạ độ. KHÁC get_sightseeing_info (liệt kê địa danh để giới thiệu tour, "
            + "không gắn vị trí thật).",
            ParseSchema("""
            {
              "type": "object",
              "properties": {
                "latitude":        { "type": "number", "description": "Vĩ độ hiện tại của tàu, lấy từ ngữ cảnh cuộc trò chuyện." },
                "longitude":       { "type": "number", "description": "Kinh độ hiện tại của tàu, lấy từ ngữ cảnh cuộc trò chuyện." },
                "radius_meters":   { "type": "number", "description": "Bán kính tìm, mặc định 800m. Tăng lên khi quanh đó không có gì; tối đa 3000." },
                "heading_degrees": { "type": "number", "description": "Hướng mũi tàu (0=Bắc, 90=Đông), lấy từ ngữ cảnh. Bỏ trống thì không nói được trái/phải." }
              },
              "required": ["latitude", "longitude"]
            }
            """)),
    ];

    private static JsonElement ParseSchema(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();
}
