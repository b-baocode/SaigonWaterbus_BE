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
            AdultCount: 3);

        var merged = AssistantBookingDraftMerger.Merge(Draft, patch)!;

        merged["departureDate"]!.GetValue<string>().ShouldBe("2026-08-20");
        merged["toStationName"]!.GetValue<string>().ShouldBe("Bến Thủ Thiêm");
        merged["adultCount"]!.GetValue<int>().ShouldBe(3);
        // childCount va serviceType khong nam trong patch nen phai con nguyen.
        merged["childCount"]!.GetValue<int>().ShouldBe(0);
        merged["serviceType"]!.GetValue<string>().ShouldBe("Waterbus");
    }

    [Test]
    public void GiuNguyenMoiFieldLaCuaClient()
    {
        var merged = AssistantBookingDraftMerger.Merge(Draft, new AssistantDraftPatch(AdultCount: 3))!;

        merged["stage"]!.GetValue<string>().ShouldBe("SelectingTrip");
        merged["passengers"]!.AsArray().Count.ShouldBe(1);
        merged["contact"]!["phone"]!.GetValue<string>().ShouldBe("0900000001");
    }

    [Test]
    public void Trip_GhiVaoSelectedDepartureTrip_KhongTaoKhoaTrip()
    {
        var tripId = Guid.NewGuid();
        var patch = new AssistantDraftPatch(Trip: new AssistantPickedTrip(
            tripId,
            "BB-20260814-WB-BD-LB-0800",
            new DateTimeOffset(2026, 8, 14, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 14, 1, 5, 0, TimeSpan.Zero),
            7000m,
            79));

        var merged = AssistantBookingDraftMerger.Merge(Draft, patch)!;

        merged["trip"].ShouldBeNull();
        var trip = merged["selectedDepartureTrip"].ShouldNotBeNull();
        trip["tripId"]!.GetValue<Guid>().ShouldBe(tripId);
        trip["tripCode"]!.GetValue<string>().ShouldBe("BB-20260814-WB-BD-LB-0800");
        trip["availableSeats"]!.GetValue<int>().ShouldBe(79);
    }

    [Test]
    public void Seats_GhiVaoSelectedSeatsDeparture_KhongTaoKhoaSeats()
    {
        var patch = new AssistantDraftPatch(
            TripCode: "BB-20260814-WB-BD-LB-0800",
            Seats:
            [
                new AssistantPickedSeat("1-A1", 1, 7000m, "Standard"),
                new AssistantPickedSeat("1-A2", 1, 7000m, "Standard"),
            ]);

        var merged = AssistantBookingDraftMerger.Merge(Draft, patch)!;

        merged["seats"].ShouldBeNull();
        var seats = merged["selectedSeatsDeparture"]!.AsArray();
        seats.Count.ShouldBe(2);
        seats[0]!["seatNumber"]!.GetValue<string>().ShouldBe("1-A1");
        seats[0]!["seatTypeName"]!.GetValue<string>().ShouldBe("Standard");
        merged["tripCode"]!.GetValue<string>().ShouldBe("BB-20260814-WB-BD-LB-0800");
    }

    [TestCase("\"da stringify roi\"")]
    [TestCase("[1, 2, 3]")]
    [TestCase("{ khong phai json")]
    [TestCase("")]
    public void DraftKhongDungKieu_VanTraVeObjectChuaThayDoiCuaLuotNay(string draft)
    {
        var merged = AssistantBookingDraftMerger.Merge(draft, new AssistantDraftPatch(AdultCount: 2));

        merged.ShouldBeOfType<JsonObject>();
        merged!["adultCount"]!.GetValue<int>().ShouldBe(2);
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
