using SaigonWaterbus.Application.Bookings;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Bookings : IEndpointGroup
{
    public static string RoutePrefix => "/api/bookings";

    private const string CreateBookingExample =
        """
        {
          "tripCode": "BB-20260610-R01-BD-LD-1234",
          "items": [
            {
              "seatNumber": "A1",
              "ticketTypeCode": "ADULT",
              "fromStationCode": "BD",
              "toStationCode": "TADA",
              "passengerName": "Nguyen Van A",
              "passengerPhone": "0901234567",
              "passengerEmail": "nguyenvana@example.com"
            },
            {
              "seatNumber": null,
              "ticketTypeCode": "INFANT",
              "fromStationCode": "BD",
              "toStationCode": "TADA",
              "passengerName": "Be Nguyen Van B",
              "birthYear": 2025
            }
          ],
          "promotionCode": null,
          "insuranceSelected": true,
          "insurancePackageId": "00000000-0000-0000-0000-000000000000",
          "returnTripCode": "BB-20260610-R01-LD-BD-5678",
          "returnItems": [
            {
              "seatNumber": "B2",
              "ticketTypeCode": "ADULT",
              "fromStationCode": "TADA",
              "toStationCode": "BD",
              "passengerName": "Nguyen Van A",
              "passengerPhone": "0901234567",
              "passengerEmail": "nguyenvana@example.com"
            }
          ]
        }
        """;

    private const string CreateCounterBookingExample =
        """
        {
          "tripCode": "BB-20260610-R01-BD-LD-1234",
          "items": [
            {
              "seatNumber": "A1",
              "ticketTypeCode": "ADULT",
              "fromStationCode": "BD",
              "toStationCode": "TADA",
              "passengerName": "Nguyen Van A"
            }
          ],
          "contactName": "Nguyen Van A",
          "contactPhone": "0901234567",
          "contactEmail": null,
          "insuranceSelected": true,
          "insurancePackageId": "00000000-0000-0000-0000-000000000000",
          "paymentMethod": "Cash"
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetBookings, string.Empty)
            .RequireAuthorization()
            .WithSummary("Lich su dat ve cua toi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Tra ve tat ca booking cua user dang dang nhap, moi nhat truoc.",
                "itemCount: so ve con hieu luc (chua bi cancel).",
                "pointsUsed/pointsEarned va insurance duoc tra kem de FE hien nhanh tren danh sach.",
                "serviceType = Waterbus | Sightseeing (dich vu khach mua, suy tu routeType cua chuyen); "
                + "FE dung de hien dung nhan va man chi tiet."));

        group.MapGet(GetBookingDetail, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Chi tiet booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Tra ve day du thong tin booking kem danh sach ve (hanh khach, cho, gio len/xuong).",
                "serviceType = Waterbus | Sightseeing, routeType = routeType goc cua tuyen (Regular | SightseeingLoop); "
                + "tour ngam canh di nguyen chuyen nen ga len = ga xuong.",
                "Tra ve 404 neu booking khong thuoc ve user dang dang nhap."));

        group.MapPost(CancelBooking, "{id:guid}/cancel")
            .RequireAuthorization()
            .WithSummary("Huy booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Huy toan bo booking va tat ca BookingItem ben trong.",
                "Khong the huy khi tau da khoi hanh (departureTime <= now).",
                "Hoan lai luot su dung ma khuyen mai neu co.",
                "Tra ve 204 khi huy thanh cong."));

        group.MapGet(GetBookingManifestByCode, "manifest/{bookingCode}")
            .RequireAuthorization()
            .WithSummary("Tra cuu manifest booking bang ma dat cho")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Chu booking hoac Admin/Manager/Staff",
                null,
                "Tra ve danh sach hanh khach + ve + trang thai check-in cua booking thuong.",
                "bookingQrToken: QR chung cua booking (chi co sau khi thanh toan du)."));

        group.MapGet(GetBookingManifestByQrToken, "manifest/qr/{qrToken}")
            .RequireAuthorization()
            .WithSummary("Tra cuu manifest booking bang QR chung")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Chu booking hoac Admin/Manager/Staff",
                null,
                "Staff quet QR chung tren ve dien tu de mo manifest ca nhom.",
                "Check-in tung ve van dung POST /api/tickets/check-in/{codeOrToken}."));

        group.MapPost(CheckInAllBookingTickets, "manifest/qr/{qrToken}/check-in-all")
            .RequireAuthorization()
            .WithSummary("Check-in ca nhom bang QR chung")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoac Staff",
                null,
                "Check-in mot luot toan bo ve Active cua booking thuong.",
                "Booking khu hoi: truyen query ?tripCode=<chuyen dang boarding> de chi check-in ve chieu do; bo trong se check-in tat ca.",
                "Neu nguoi goi la Staff thi phai la nhan vien OnBoard co ca assignmentType=Boat dang active tren dung tau cua tung chieu check-in.",
                "Yeu cau booking da Confirmed va thanh toan du.",
                "Tra ve manifest moi sau khi check-in."));

        group.MapPost(ResendBookingTickets, "{id:guid}/resend-tickets")
            .RequireAuthorization()
            .WithSummary("Gui lai email ve dien tu (QR)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Chu booking hoac Admin/Manager/Staff",
                null,
                "Gui lai email ve: nguoi dat nhan email tong (QR chung + tat ca QR rieng); hanh khach co email nhan boarding pass rieng.",
                "Chi ap dung cho booking thuong da Confirmed va thanh toan du.",
                "Neu ve/QR chung chua duoc phat hanh (vd webhook loi) se phat hanh bu truoc khi gui."));

        group.MapPost(CreateBooking, string.Empty)
            .RequireAuthorization()
            .WithSummary("Dat ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                CreateBookingExample,
                "tripCode: lay tu GET /api/trips hoac GET /api/trips/search → tripCode.",
                "seatNumber: lay tu GET /api/trips/{id}/seats → seats[].seatNumber (chi chon ghe status=Available).",
                "ticketTypeCode: ADULT (nguyen gia) | CHILD (mien phi waterbus thuong) | INFANT (duoi 2 tuoi) | SENIOR (tren 70) | DISABLED (khuyet tat).",
                "INFANT khong chiem ghe (ngoi long cung nguoi lon) MIEN PHI tren CA waterbus thuong lan sightseeing; van co ve/QR.",
                "Waterbus thuong: CHILD/SENIOR/DISABLED/INFANT mien phi.",
                "Sightseeing: CHILD/SENIOR/DISABLED dung chung % giam tai /api/ticket-types/sightseeing-concession; INFANT mien phi.",
                "INFANT bat buoc khai bao birthYear (tre duoi 2 tuoi tinh theo ngay khoi hanh chuyen).",
                "Moi INFANT khong chiem ghe phai co it nhat mot hanh khach nguoi lon co ghe di kem trong cung booking.",
                "fromStationCode / toStationCode: khuyến nghị lấy từ GET /api/trips/{id} → stops[].stationCode; BE cũng chấp nhận stationId hoặc stationName.",
                "Chi bat buoc tren chuyen ban ve theo chang (sellsBySegment=true trong GET /api/trips/{id} hoac /seats); "
                    + "chuyen ngam canh (routeType=SightseeingLoop) di nguyen chuyen nen BO TRONG ca hai, BE tu lay ben dau → ben cuoi cua tuyen.",
                "fromStationCode phai co stop_order nho hon toStationCode (chuyen ban theo chang).",
                "Toi da 10 ghe trong 1 lan dat.",
                "Gia tu dong tinh theo gia cua seatTypeCode cua ghe x ticket type modifier.",
                "passengerEmail (optional): hanh khach co email se nhan rieng ve dien tu (QR) cua minh sau khi thanh toan.",
                "Ve khu hoi (optional): truyen them returnTripCode + returnItems de mua ve 2 chieu trong 1 booking; hai chieu doc lap (trip, ghe, hanh khach rieng), khong giam gia, tong tien = cong 2 chieu.",
                "returnTripCode va returnItems phai di cung nhau; returnItems theo cung rule voi items (toi da 10 ghe/chieu, INFANT tinh theo tung chieu).",
                "Bao hiem: dung goi PassengerInsurance active/default, BE tu cong phi theo so passenger item. Gui insuranceSelected=false de khong chon; gui insurancePackageId de chon goi cu the.",
                "bookingStatus sau khi tao: PendingPayment; ghe duoc giu toi da 15 phut, nhung holdExpiresAt khong vuot gio dong ban cua chang (gio tau roi ben len - 10 phut). Qua han booking tu Expired va nha ghe ca 2 chieu.",
                "Tra ve 400 neu ghe da bi dat hoac dang duoc nguoi khac tam giu (race condition)."));

        group.MapPost(CreateCounterBooking, "counter")
            .RequireAuthorization()
            .WithSummary("Ban ve tai quay (staff)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Chi Staff",
                CreateCounterBookingExample,
                "Staff dat ve ho khach mua truc tiep tai quay; khach KHONG can co tai khoan.",
                "items / returnItems / ticketTypeCode / fromStationCode / toStationCode: giong het POST /api/bookings.",
                "contactName + contactPhone bat buoc (staff nhap); contactEmail optional - co email thi khach nhan email ve dien tu.",
                "paymentMethod = Cash: ghi nhan thu tien mat ngay -> booking Confirmed, phat hanh ve/QR va gui email ngay trong 1 lan goi.",
                "paymentMethod = PayOs: tra ve checkoutUrl + qrCode cho khach quet; ve chi phat hanh sau khi PayOS bao da thanh toan. "
                    + "Staff theo doi bang POST /api/payments/{paymentId}/sync; qua holdExpiresAt chua tra thi booking het han va nha ghe.",
                "Ban duoc CA KHI TAU DA KHOI HANH: bo han dong ban truoc gio chay, chap nhan chuyen Scheduled/Boarding/Delayed/InProgress; "
                    + "chi tu choi chuyen da Completed hoac Cancelled.",
                "Ghe van kiem tra nhu binh thuong: da ban / dang duoc nguoi khac giu tren chang giao nhau se bi tu choi.",
                "Khong ho tro ma khuyen mai va diem tich luy (booking khach vang lai khong gan tai khoan).",
                "Bao hiem PassengerInsurance active/default duoc cong theo so passenger item; gui insuranceSelected=false de khong chon.",
                "Don 0d (tong tien sau tinh gia/giam gia bang 0) luon ghi nhan nhu thu tai quay du chon paymentMethod nao."));
    }

    private static async Task<IResult> GetBookings(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetBookingListQuery(), ct));

    private static async Task<IResult> GetBookingDetail(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetBookingDetailQuery(id), ct));

    private static async Task<IResult> CancelBooking(ISender sender, Guid id, CancellationToken ct)
    {
        await sender.Send(new CancelBookingCommand(id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateBooking(
        ISender sender, CreateBookingCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> CreateCounterBooking(
        ISender sender, CreateCounterBookingCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> GetBookingManifestByCode(
        ISender sender, string bookingCode, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetBookingManifestByCodeQuery(bookingCode), ct));

    private static async Task<IResult> GetBookingManifestByQrToken(
        ISender sender, string qrToken, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetBookingManifestByQrTokenQuery(qrToken), ct));

    private static async Task<IResult> CheckInAllBookingTickets(
        ISender sender, string qrToken, string? tripCode, CancellationToken ct) =>
        Results.Ok(await sender.Send(new CheckInAllBookingTicketsCommand(qrToken, tripCode), ct));

    private static async Task<IResult> ResendBookingTickets(
        ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new ResendBookingTicketsCommand(id), ct));
}
