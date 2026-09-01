using System.Text.Json;
using NUnit.Framework;
using SaigonWaterbus.Application.Assistant;
using SaigonWaterbus.Application.Trips;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Assistant;

/// <summary>
/// Chặn hồi quy cho bug 01/09/2026: trợ lý báo chuyến 20:00 thành 13:00 — đúng 7 tiếng, vì kết
/// quả tool đưa thẳng DateTimeOffset (timestamptz nên offset luôn +00:00) cho model đọc.
///
/// Loại bug này KHÔNG tự lộ ra: số chuyến đúng, khoảng cách giữa các chuyến đúng, chỉ mỗi con số
/// giờ là sai, và model tra lại vẫn ra đúng con số đó nên càng khẳng định là mình đúng.
/// </summary>
public class AssistantToolsetTimeTests
{
    /// <summary>Giờ trong DB là UTC: 13:00Z chính là chuyến 20:00 mà khách thấy trên trang lịch.</summary>
    [Test]
    public void ClockDoiSangGioVietNam() =>
        AssistantToolset.Clock(new DateTimeOffset(2026, 9, 1, 13, 0, 0, TimeSpan.Zero))
            .ShouldBe("20:00");

    /// <summary>
    /// Ca này là lý do KHÔNG dặn model "cộng thêm 7 tiếng": qua nửa đêm thì phải sang ngày hôm
    /// sau, cộng nhẩm kiểu gì cũng có lúc sai.
    /// </summary>
    [Test]
    public void ClockQuaNuaDemVanDung() =>
        AssistantToolset.Clock(new DateTimeOffset(2026, 9, 1, 18, 30, 0, TimeSpan.Zero))
            .ShouldBe("01:30");

    /// <summary>Khuyến mãi bắt đầu 00:00 ngày 01/09 giờ VN được lưu là 31/08 17:00 UTC.</summary>
    [Test]
    public void DayDoiNgayTheoGioVietNam() =>
        AssistantToolset.Day(new DateTimeOffset(2026, 8, 31, 17, 0, 0, TimeSpan.Zero))
            .ShouldBe("2026-09-01");

    [Test]
    public void ChuyenGuiChoModelDungGioVietNamVaKhongLoDuLieuThua()
    {
        var json = JsonSerializer.Serialize(AssistantToolset.ToModelTrip(SampleTrip()));

        json.ShouldContain("\"gio_khoi_hanh\":\"20:00\"");
        json.ShouldContain("\"gio_den\":\"21:50\"");
        // Không được còn dấu vết của DateTimeOffset thô hay id nội bộ.
        json.ShouldNotContain("+00:00");
        json.ShouldNotContain("2026-09-01T");
        json.ShouldNotContain("tripId", Case.Insensitive);
        json.ShouldNotContain("stops", Case.Insensitive);
    }

    /// <summary>Giờ khởi hành phải là giờ tại BẾN KHÁCH LÊN, không phải giờ rời bến đầu tuyến.</summary>
    [Test]
    public void UuTienGioTaiBenKhachLen()
    {
        var trip = SampleTrip() with
        {
            DepartureTime = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
        };

        JsonSerializer.Serialize(AssistantToolset.ToModelTrip(trip))
            .ShouldContain("\"gio_khoi_hanh\":\"20:00\"");
    }

    /// <summary>Delay đã kết thúc mà vẫn kể ra thì model đi báo khách chuyến đúng giờ là bị trễ.</summary>
    [Test]
    public void KhongBaoTreKhiDelayDaKetThuc()
    {
        var trip = SampleTrip() with
        {
            DelayInfo = new TripDelayInfoDto(15, "Thời tiết", IsDelayActive: false, null, null, null, 0),
        };

        var json = JsonSerializer.Serialize(AssistantToolset.ToModelTrip(trip));

        json.ShouldContain("\"tre_phut\":null");
        json.ShouldNotContain("Thời tiết");
    }

    private static TripSummaryDto SampleTrip() => new(
        TripId: Guid.NewGuid(),
        TripCode: "TRIP-001",
        RouteName: "Ngắm cảnh sông Sài Gòn",
        RouteType: "SightseeingLoop",
        DepartureTime: new DateTimeOffset(2026, 9, 1, 13, 0, 0, TimeSpan.Zero),
        ArrivalTime: new DateTimeOffset(2026, 9, 1, 14, 50, 0, TimeSpan.Zero),
        FromStopScheduledDeparture: new DateTimeOffset(2026, 9, 1, 13, 0, 0, TimeSpan.Zero),
        ToStopScheduledArrival: new DateTimeOffset(2026, 9, 1, 14, 50, 0, TimeSpan.Zero),
        AvailableSeats: 40,
        TotalSeats: 60,
        MinPrice: 150_000m,
        TripStatus: "Scheduled");
}
