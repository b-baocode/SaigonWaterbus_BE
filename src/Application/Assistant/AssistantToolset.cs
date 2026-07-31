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
                "list_stations" => await ListStationsAsync(arguments, cancellationToken),
                "search_trips" => await SearchTripsAsync(arguments, cancellationToken),
                "get_trip_seat_map" => await GetTripSeatMapAsync(arguments, cancellationToken),
                "get_sightseeing_info" => await GetSightseeingInfoAsync(arguments, cancellationToken),
                "get_pricing_info" => await GetPricingInfoAsync(arguments, cancellationToken),
                "get_promotions" => await GetPromotionsAsync(cancellationToken),
                "get_route_info" => await GetRouteInfoAsync(arguments, cancellationToken),
                "get_charter_prices" => await GetCharterPricesAsync(cancellationToken),
                "search_knowledge" => await SearchKnowledgeAsync(arguments, cancellationToken),
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
