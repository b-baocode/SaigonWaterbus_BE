using System.Text.Json.Nodes;
using NUnit.Framework;
using SaigonWaterbus.Application.Assistant;
using Shouldly;

namespace SaigonWaterbus.Application.UnitTests.Assistant;

public class AssistantBookingDraftMergerTests
{
    private const string Draft =
        """
        {
          "stage": "SelectingTrip",
          "serviceType": "Waterbus",
          "adultCount": 2,
          "childCount": 0,
          "departureDate": "2026-08-14",
          "fromStationName": "Bến Bạch Đằng",
          "selectedDepartureTrip": null,
          "selectedSeatsDeparture": [],
          "passengers": [{ "fullName": "Nguyễn Văn A" }],
          "contact": { "phone": "0900000001" }
        }
        """;

    [Test]
    public void KhongCoDraftVaKhongCoThayDoi_TraNull() =>
        AssistantBookingDraftMerger.Merge(null, null).ShouldBeNull();

    [Test]
    public void KhongCoThayDoi_TraLaiNguyenDraftCu()
    {
        var merged = AssistantBookingDraftMerger.Merge(Draft, new AssistantDraftPatch());

        merged.ShouldNotBeNull();
        merged["stage"]!.GetValue<string>().ShouldBe("SelectingTrip");
        merged["adultCount"]!.GetValue<int>().ShouldBe(2);
    }

    [Test]
    public void FieldKhacNull_GhiDe_FieldNull_GiuNguyen()
    {
        var patch = new AssistantDraftPatch(
            DepartureDate: "2026-08-20",
            ToStationName: "Bến Thủ Thiêm",
            IsRoundTrip: true);

        var merged = AssistantBookingDraftMerger.Merge(Draft, patch)!;

        merged["departureDate"]!.GetValue<string>().ShouldBe("2026-08-20");
        merged["toStationName"]!.GetValue<string>().ShouldBe("Bến Thủ Thiêm");
        merged["isRoundTrip"]!.GetValue<bool>().ShouldBeTrue();
        // Hai field nay khong nam trong patch nen phai con nguyen.
        merged["fromStationName"]!.GetValue<string>().ShouldBe("Bến Bạch Đằng");
        merged["serviceType"]!.GetValue<string>().ShouldBe("Waterbus");
    }

    [Test]
    public void GiuNguyenMoiFieldLaCuaClient()
    {
        var merged = AssistantBookingDraftMerger.Merge(
            Draft, new AssistantDraftPatch(DepartureDate: "2026-08-20"))!;

        merged["stage"]!.GetValue<string>().ShouldBe("SelectingTrip");
        merged["passengers"]!.AsArray().Count.ShouldBe(1);
        merged["contact"]!["phone"]!.GetValue<string>().ShouldBe("0900000001");
        // Con ca so khach client tu giu: BE khong doc nua nhung cung khong duoc xoa.
        merged["adultCount"]!.GetValue<int>().ShouldBe(2);
    }

    private static AssistantPickedTrip Trip(Guid tripId, string code) => new(
        tripId,
        code,
        "Bach Dang - Linh Dong",
        new DateTimeOffset(2026, 8, 14, 1, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 14, 1, 5, 0, TimeSpan.Zero),
        7000m,
        79,
        80,
        Guid.NewGuid());

    [Test]
    public void Trip_GhiVaoSelectedDepartureTrip_KhongTaoKhoaTrip()
    {
        var tripId = Guid.NewGuid();
        var merged = AssistantBookingDraftMerger.Merge(
            Draft, new AssistantDraftPatch(Trip: Trip(tripId, "BB-20260814-WB-BD-LB-0800")))!;

        merged["trip"].ShouldBeNull();
        var trip = merged["selectedDepartureTrip"].ShouldNotBeNull();
        trip["tripId"]!.GetValue<Guid>().ShouldBe(tripId);
        trip["tripCode"]!.GetValue<string>().ShouldBe("BB-20260814-WB-BD-LB-0800");
        trip["availableSeats"]!.GetValue<int>().ShouldBe(79);
        // Ba field FE can them: hien "con trong x/y", tai anh tau, va tach ten ben cho tuyen vong.
        trip["totalSeats"]!.GetValue<int>().ShouldBe(80);
        trip["boatId"].ShouldNotBeNull();
        trip["routeName"]!.GetValue<string>().ShouldBe("Bach Dang - Linh Dong");
    }

    [Test]
    public void ReturnTrip_GhiVaoSelectedReturnTrip()
    {
        var returnId = Guid.NewGuid();
        var merged = AssistantBookingDraftMerger.Merge(
            Draft, new AssistantDraftPatch(ReturnTrip: Trip(returnId, "BB-20260820-WB-LB-BD-1700")))!;

        merged["returnTrip"].ShouldBeNull();
        merged["selectedReturnTrip"]!["tripId"]!.GetValue<Guid>().ShouldBe(returnId);
        // Chieu di khong bi dung toi: van la null nhu trong draft ban dau.
        merged["selectedDepartureTrip"].ShouldBeNull();
    }

    [Test]
    public void StationId_GhiVaoDraft()
    {
        var fromId = Guid.NewGuid();
        var toId = Guid.NewGuid();
        var merged = AssistantBookingDraftMerger.Merge(
            Draft, new AssistantDraftPatch(FromStationId: fromId, ToStationId: toId))!;

        merged["fromStationId"]!.GetValue<Guid>().ShouldBe(fromId);
        merged["toStationId"]!.GetValue<Guid>().ShouldBe(toId);
    }

    [Test]
    public void Seats_GhiVaoSelectedSeatsDeparture_KhongTaoKhoaSeats()
    {
        var patch = new AssistantDraftPatch(
            Seats:
            [
                new AssistantPickedSeat("1-A1", 1, "A", 1, 7000m, "Standard"),
                new AssistantPickedSeat("1-A2", 1, "A", 2, 7000m, "Standard"),
            ]);

        var merged = AssistantBookingDraftMerger.Merge(Draft, patch)!;

        merged["seats"].ShouldBeNull();
        var seats = merged["selectedSeatsDeparture"]!.AsArray();
        seats.Count.ShouldBe(2);
        seats[0]!["seatNumber"]!.GetValue<string>().ShouldBe("1-A1");
        seats[0]!["seatTypeName"]!.GetValue<string>().ShouldBe("Standard");
        // row/column de trang dat ve ve dung o tren luoi ghe.
        seats[0]!["row"]!.GetValue<string>().ShouldBe("A");
        seats[1]!["column"]!.GetValue<int>().ShouldBe(2);
    }

    [Test]
    public void ReturnSeats_GhiVaoSelectedSeatsReturn()
    {
        var merged = AssistantBookingDraftMerger.Merge(
            Draft,
            new AssistantDraftPatch(ReturnSeats: [new AssistantPickedSeat("2-B3", 2, "B", 3, 9000m, "VIP")]))!;

        merged["returnSeats"].ShouldBeNull();
        merged["selectedSeatsReturn"]!.AsArray().Count.ShouldBe(1);
        merged["selectedSeatsReturn"]![0]!["seatNumber"]!.GetValue<string>().ShouldBe("2-B3");
        // Ghe chieu di van rong nhu trong draft ban dau.
        merged["selectedSeatsDeparture"]!.AsArray().Count.ShouldBe(0);
    }

    [TestCase("\"da stringify roi\"")]
    [TestCase("[1, 2, 3]")]
    [TestCase("{ khong phai json")]
    [TestCase("")]
    public void DraftKhongDungKieu_VanTraVeObjectChuaThayDoiCuaLuotNay(string draft)
    {
        var merged = AssistantBookingDraftMerger.Merge(draft, new AssistantDraftPatch(DepartureDate: "2026-08-14"));

        merged.ShouldBeOfType<JsonObject>();
        merged!["departureDate"]!.GetValue<string>().ShouldBe("2026-08-14");
    }

    [Test]
    public void KhongGuiDraft_VanTraVeThayDoiDeClientDungTiep()
    {
        var merged = AssistantBookingDraftMerger.Merge(null, new AssistantDraftPatch(
            ServiceType: "Sightseeing",
            DepartureDate: "2026-08-14"))!;

        merged["serviceType"]!.GetValue<string>().ShouldBe("Sightseeing");
        merged["departureDate"]!.GetValue<string>().ShouldBe("2026-08-14");
    }

    [Test]
    public void IsRoundTrip_False_VanDuocGhiDe()
    {
        var merged = AssistantBookingDraftMerger.Merge(
            """{ "isRoundTrip": true }""",
            new AssistantDraftPatch(IsRoundTrip: false))!;

        merged["isRoundTrip"]!.GetValue<bool>().ShouldBeFalse();
    }
}
