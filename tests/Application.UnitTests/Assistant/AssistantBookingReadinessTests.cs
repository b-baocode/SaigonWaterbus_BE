using System.Text.Json.Nodes;
using NUnit.Framework;
using SaigonWaterbus.Application.Assistant;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Assistant;

public class AssistantBookingReadinessTests
{
    private static JsonNode Draft(string json) => JsonNode.Parse(json)!;

    [Test]
    public void KhongCoPhieu_ChuaSanSang() =>
        AssistantBookingReadiness.Resolve(null).ShouldBe(AssistantBookingStage.NotReady);

    [Test]
    public void ThieuNgay_ChuaSanSang() =>
        AssistantBookingReadiness.Resolve(Draft(
            """{ "fromStationCode": "ST-BD", "toStationCode": "ST-TT" }"""))
            .ShouldBe(AssistantBookingStage.NotReady);

    [Test]
    public void ThieuChang_ChuaSanSang_VoiWaterbus() =>
        AssistantBookingReadiness.Resolve(Draft(
            """{ "serviceType": "Waterbus", "departureDate": "2026-08-14", "fromStationCode": "ST-BD" }"""))
            .ShouldBe(AssistantBookingStage.NotReady);

    [Test]
    public void NgamCanh_ChiCanNgay_LaSanSang() =>
        AssistantBookingReadiness.Resolve(Draft(
            """{ "serviceType": "Sightseeing", "departureDate": "2026-08-14" }"""))
            .ShouldBe(AssistantBookingStage.Search);

    [Test]
    public void DuChangVaNgay_ChuaCoChuyen_ThiVaoBuocTimChuyen() =>
        AssistantBookingReadiness.Resolve(Draft(
            """
            { "serviceType": "Waterbus", "departureDate": "2026-08-14",
              "fromStationCode": "ST-BD", "toStationCode": "ST-TT" }
            """))
            .ShouldBe(AssistantBookingStage.Search);

    [Test]
    public void CoChuyen_ChuaCoGhe_ThiVaoBuocChonGhe() =>
        AssistantBookingReadiness.Resolve(Draft(
            """
            { "serviceType": "Waterbus", "departureDate": "2026-08-14",
              "fromStationCode": "ST-BD", "toStationCode": "ST-TT",
              "selectedDepartureTrip": { "tripId": "a7b580cb-7247-4654-b6c4-00c7e5033956" },
              "selectedSeatsDeparture": [] }
            """))
            .ShouldBe(AssistantBookingStage.SelectSeats);

    [Test]
    public void DaChonGhe_ThiToiBuocGiuCho() =>
        AssistantBookingReadiness.Resolve(Draft(
            """
            { "serviceType": "Waterbus", "departureDate": "2026-08-14",
              "fromStationCode": "ST-BD", "toStationCode": "ST-TT",
              "selectedDepartureTrip": { "tripId": "a7b580cb-7247-4654-b6c4-00c7e5033956" },
              "selectedSeatsDeparture": [{ "seatNumber": "1-A1" }, { "seatNumber": "1-A2" }] }
            """))
            .ShouldBe(AssistantBookingStage.HoldSeats);

    [Test]
    public void KhuHoi_MoiCoChuyenDi_ThiVanDungOBuocTimChuyen() =>
        AssistantBookingReadiness.Resolve(Draft(
            """
            { "serviceType": "Waterbus", "departureDate": "2026-08-14", "returnDate": "2026-08-20",
              "isRoundTrip": true,
              "fromStationCode": "ST-BD", "toStationCode": "ST-TT",
              "selectedDepartureTrip": { "tripId": "a7b580cb-7247-4654-b6c4-00c7e5033956" },
              "selectedSeatsDeparture": [] }
            """))
            .ShouldBe(AssistantBookingStage.Search);

    [Test]
    public void KhuHoi_DuCaHaiChuyen_ThiVaoBuocChonGhe() =>
        AssistantBookingReadiness.Resolve(Draft(
            """
            { "serviceType": "Waterbus", "departureDate": "2026-08-14", "returnDate": "2026-08-20",
              "isRoundTrip": true,
              "fromStationCode": "ST-BD", "toStationCode": "ST-TT",
              "selectedDepartureTrip": { "tripId": "a7b580cb-7247-4654-b6c4-00c7e5033956" },
              "selectedReturnTrip": { "tripId": "b1c2d3e4-1111-2222-3333-444455556666" },
              "selectedSeatsDeparture": [] }
            """))
            .ShouldBe(AssistantBookingStage.SelectSeats);

    [Test]
    public void ChuaSanSang_ThiKhongCoNut() =>
        AssistantBookingReadiness.BuildAction(Draft("""{ "stage": "CollectingInfo" }"""), english: false)
            .ShouldBeNull();

    [Test]
    public void Nut_MangRouteVaStepDungVoiTungMuc()
    {
        var search = AssistantBookingReadiness.BuildAction(Draft(
            """
            { "serviceType": "Waterbus", "departureDate": "2026-08-14",
              "fromStationCode": "ST-BD", "toStationCode": "ST-TT" }
            """), english: false)!;
        search.Type.ShouldBe(AssistantBookingReadiness.OpenBookingActionType);
        search.Route.ShouldBe(AssistantBookingReadiness.WaterbusRoute);
        search.Step.ShouldBe(1);
        search.Label.ShouldBe("Xem chuyến");

        var hold = AssistantBookingReadiness.BuildAction(Draft(
            """
            { "serviceType": "Waterbus", "departureDate": "2026-08-14",
              "fromStationCode": "ST-BD", "toStationCode": "ST-TT",
              "selectedDepartureTrip": { "tripId": "a7b580cb-7247-4654-b6c4-00c7e5033956" },
              "selectedSeatsDeparture": [{ "seatNumber": "1-A1" }, { "seatNumber": "1-A2" }] }
            """), english: false)!;
        hold.Step.ShouldBe(2);
        hold.Label.ShouldBe("Giữ ghế & thanh toán");
    }

    [Test]
    public void NgamCanh_NutTroVeTrangNgamCanh()
    {
        var action = AssistantBookingReadiness.BuildAction(Draft(
            """{ "serviceType": "Sightseeing", "departureDate": "2026-08-14" }"""), english: true)!;

        action.Route.ShouldBe(AssistantBookingReadiness.SightseeingRoute);
        action.Label.ShouldBe("View trips");
    }
}
