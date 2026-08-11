using System.Text;
using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Domain.Entities;

namespace SaigonWaterbus.Application.TourGuide;

/// <summary>Vị trí tàu lấy từ bản tin GPS mới nhất của chuyến, dùng khi client không gửi toạ độ.</summary>
public sealed record TourGuideTripPosition(
    double Latitude,
    double Longitude,
    double? Heading,
    DateTimeOffset RecordedAt);

/// <summary>
/// Ngữ cảnh chuyến đi để nhét thẳng vào system prompt.
/// <paramref name="Position"/> null nghĩa là chuyến chưa có GPS hoặc bản tin đã quá cũ.
/// </summary>
public sealed record TourGuideTripContext(string PromptBlock, TourGuideTripPosition? Position);

/// <summary>Địa danh đang được thuyết minh, tra theo id để lấy đúng lời mô tả đã duyệt.</summary>
public sealed record TourGuideLandmarkContext(string Name, string? Description);

/// <summary>
/// Đọc dữ liệu chuyến + địa danh cho hướng dẫn viên giọng nói.
///
/// CỐ Ý NHÉT THẲNG VÀO PROMPT CHỨ KHÔNG LÀM THÊM TOOL: dữ liệu chuyến luôn cần (khách hỏi
/// "bao lâu nữa tới nơi", "còn ghé bến nào" là chuyện thường nhất trên tàu) mà mỗi tool là
/// thêm một vòng gọi LLM — một lượt hỏi đáp đang mất 4.5–5.5 giây, không còn chỗ để đắt thêm.
/// </summary>
public sealed class TourGuideContextReader
{
    /// <summary>Giờ Việt Nam. Đổi sẵn ở đây để model khỏi phải cộng 7 tiếng — chỗ nó hay nhầm nhất.</summary>
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>
    /// Bản tin GPS cũ hơn mức này thì coi như không có: thà nói "chưa xác định được vị trí" còn
    /// hơn kể về địa danh cách đó vài cây số.
    /// </summary>
    private static readonly TimeSpan MaxPositionAge = TimeSpan.FromMinutes(5);

    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public TourGuideContextReader(IApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Trả null khi không tìm thấy chuyến. CỐ Ý không ném NotFound: khách đang nói chuyện bằng
    /// giọng nói, một id sai không đáng để cả lượt hỏi câm — hướng dẫn viên vẫn trả lời được
    /// về địa danh và giá vé, chỉ mất phần lịch trình.
    /// </summary>
    public async Task<TourGuideTripContext?> ReadTripAsync(Guid tripId, CancellationToken cancellationToken)
    {
        var trip = await _context.Set<Trip>()
            .AsNoTracking()
            .Include(t => t.Route)
                .ThenInclude(r => r.RouteStops)
                    .ThenInclude(rs => rs.Station)
            .Include(t => t.TripStops)
                .ThenInclude(ts => ts.Station)
            .Include(t => t.Boat)
            .SingleOrDefaultAsync(t => t.Id == tripId, cancellationToken);

        if (trip is null)
        {
            return null;
        }

        var location = await _context.BoatLatestLocations
            .AsNoTracking()
            .Include(l => l.NextStation)
            .Where(l => l.TripId == tripId)
            .OrderByDescending(l => l.Sequence)
            .FirstOrDefaultAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var position = location is not null && now - location.RecordedAt <= MaxPositionAge
            ? new TourGuideTripPosition(
                (double)location.Latitude,
                (double)location.Longitude,
                location.Heading,
                location.RecordedAt)
            : null;

        return new TourGuideTripContext(BuildPromptBlock(trip, location, position, now), position);
    }

    public async Task<TourGuideLandmarkContext?> ReadLandmarkAsync(
        Guid landmarkId, CancellationToken cancellationToken)
    {
        var landmark = await _context.Landmarks
            .AsNoTracking()
            .Where(l => l.Id == landmarkId)
            .Select(l => new TourGuideLandmarkContext(l.LandmarkName, l.Description))
            .SingleOrDefaultAsync(cancellationToken);

        return landmark;
    }

    private static string BuildPromptBlock(
        Trip trip,
        BoatLatestLocation? location,
        TourGuideTripPosition? position,
        DateTimeOffset now)
    {
        var departure = trip.AdjustedDepartureTime ?? trip.DepartureTime;
        var arrival = trip.AdjustedArrivalTime ?? trip.ArrivalTime;

        var builder = new StringBuilder();
        builder.AppendLine(
            "CHUYẾN KHÁCH ĐANG ĐI (dữ liệu hệ thống, mọi giờ dưới đây ĐÃ đổi sang giờ Việt Nam —");
        builder.AppendLine("đừng cộng thêm 7 tiếng nữa):");
        builder.AppendLine($"- Tuyến: {DescribeRoute(trip)}.{DescribeBoat(trip)}");
        builder.AppendLine(
            $"- Khởi hành {Clock(departure)}, dự kiến tới nơi {Clock(arrival)}. "
            + $"Trạng thái: {DescribeStatus(trip)}.");

        var stops = DescribeStops(trip, now);
        if (stops.Count > 0)
        {
            builder.AppendLine($"- Các bến theo thứ tự: {string.Join(" → ", stops)}.");
        }

        var next = DescribeNextStation(location);
        if (next is not null)
        {
            builder.AppendLine($"- {next}");
        }

        if (position is null)
        {
            builder.AppendLine(
                "- Chưa có tín hiệu GPS mới của tàu. Đừng đoán tàu đang ở đâu trên tuyến.");
        }

        builder.AppendLine(
            "Khách nói \"chuyến này\", \"tàu mình\", \"sắp tới bến nào\", \"bao lâu nữa tới nơi\" là "
            + "hỏi về chuyến ngay trên đây — trả lời thẳng bằng dữ liệu này. Đây là nguồn DUY NHẤT "
            + "về lịch trình: bạn không có tool tra chuyến, nên đừng nói gì về chuyến khác hay "
            + "ngày khác.");

        return builder.ToString();
    }

    private static string DescribeRoute(Trip trip)
    {
        var name = string.IsNullOrWhiteSpace(trip.Route?.RouteName)
            ? "chưa rõ tên"
            : trip.Route!.RouteName;

        return name;
    }

    private static string DescribeBoat(Trip trip) =>
        string.IsNullOrWhiteSpace(trip.Boat?.Name) ? string.Empty : $" Tàu {trip.Boat!.Name}.";

    private static string DescribeStatus(Trip trip)
    {
        var status = trip.TripStatus switch
        {
            Domain.Enums.TripStatus.Scheduled => "chưa khởi hành",
            Domain.Enums.TripStatus.Boarding => "đang đón khách lên tàu",
            Domain.Enums.TripStatus.Delayed => "bị trễ giờ",
            Domain.Enums.TripStatus.InProgress => "đang chạy",
            Domain.Enums.TripStatus.Completed => "đã kết thúc",
            Domain.Enums.TripStatus.Cancelled => "đã huỷ",
            _ => trip.TripStatus.ToString(),
        };

        return trip.DelayMinutes > 0 ? $"{status}, trễ {trip.DelayMinutes} phút" : status;
    }

    /// <summary>
    /// Bến lấy từ trip_stops; chuyến cũ chưa có trip_stops thì rơi về route_stops (mất giờ dự kiến
    /// nhưng vẫn kể được thứ tự bến) — cùng cách xử lý với GetTripLandmarksQuery.
    /// </summary>
    private static List<string> DescribeStops(Trip trip, DateTimeOffset now)
    {
        if (trip.TripStops.Count > 0)
        {
            return trip.TripStops
                .Where(ts => ts.Station is not null)
                .OrderBy(ts => ts.StopOrder)
                .Select(ts =>
                {
                    var time = ts.AdjustedArrivalTime ?? ts.PlannedArrivalTime
                        ?? ts.AdjustedDepartureTime ?? ts.PlannedDepartureTime;
                    var clock = time is null ? string.Empty : $" {Clock(time.Value)}";
                    return $"{ts.StopOrder}. {ts.Station.StationName}{clock} ({DescribeStopState(ts, now)})";
                })
                .ToList();
        }

        return (trip.Route?.RouteStops ?? [])
            .Where(rs => rs.Station is not null)
            .OrderBy(rs => rs.StopOrder)
            .Select(rs => $"{rs.StopOrder}. {rs.Station!.StationName}")
            .ToList();
    }

    /// <summary>
    /// Ưu tiên giờ THỰC TẾ đã ghi nhận; chưa có thì mới suy từ giờ dự kiến so với hiện tại — và
    /// nói rõ là "theo lịch" để model không khẳng định chắc chắn tàu đã ghé.
    /// </summary>
    private static string DescribeStopState(TripStop stop, DateTimeOffset now)
    {
        if (stop.ActualDepartureTime is not null)
        {
            return "đã rời bến";
        }

        if (stop.ActualArrivalTime is not null)
        {
            return "tàu đang dừng ở đây";
        }

        var planned = stop.AdjustedArrivalTime ?? stop.PlannedArrivalTime;
        return planned is not null && planned < now ? "theo lịch thì đã qua" : "chưa tới";
    }

    private static string? DescribeNextStation(BoatLatestLocation? location)
    {
        if (location?.NextStation is null)
        {
            return null;
        }

        var remaining = location.RemainingMinutesToNextStation is { } minutes && minutes >= 0
            ? $", còn khoảng {minutes} phút"
            : string.Empty;

        return $"Bến kế tiếp: {location.NextStation.StationName}{remaining}.";
    }

    private static string Clock(DateTimeOffset value) =>
        value.ToOffset(VietnamOffset).ToString("H:mm");
}
