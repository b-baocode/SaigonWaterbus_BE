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
          "contactEmail": "nguyenvana@example.com",
          "insuranceSelected": true,
          "insurancePackageId": "00000000-0000-0000-0000-000000000000",
          "paymentMethod": "Cash",
          "customerUserId": null,
          "customerConfirmedForPoints": false,
          "pointsToUse": 0
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
                "INFANT khong ghe co usesCompanionTicket=true, companionPassengerId/name va dung chung ticketCode/qrToken cua ADULT.",
                "serviceType = Waterbus | Sightseeing, routeType = routeType goc cua tuyen (Regular | SightseeingLoop); "
                + "tour ngam canh di nguyen chuyen nen ga len = ga xuong.",
                "Tra ve 404 neu booking khong thuoc ve user dang dang nhap."));

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
                "Response passengers[] show full hanh khach cua booking. CHILD co ghe/QR rieng; INFANT khong ghe di kem co usesCompanionTicket=true, companionPassengerId/name va dung chung ticketCode/qrToken cua ADULT.",
                "Check-in/check-out tung ve van dung POST /api/tickets/check-in hoac /api/tickets/check-out.",
                "Check-in/check-out ca nhom dung endpoint check-in-all/check-out-all ben duoi."));

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
                "Chi duoc check-in trong vong 10 phut truoc gio tau roi ben khach len cua tung ve.",
                "Tra ve manifest moi sau khi check-in."));

        group.MapPost(CheckOutAllBookingTickets, "manifest/qr/{qrToken}/check-out-all")
            .RequireAuthorization()
            .WithSummary("Check-out ca nhom bang QR chung")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoac Staff",
                null,
                "Check-out mot luot toan bo ve CheckedIn cua booking thuong.",
                "Booking khu hoi: truyen query ?tripCode=<chuyen dang tra khach> de chi check-out ve chieu do; bo trong se check-out tat ca ve dang CheckedIn.",
                "Neu nguoi goi la Staff thi phai la nhan vien OnBoard co ca assignmentType=Boat dang active tren dung tau cua tung chieu check-out.",
                "Check-out duoc thuc hien sau check-in, nhung khong qua 10 phut sau gio tau den ben khach xuong cua tung ve.",
                "Khi tat ca ve hop le da CheckedOut, booking chuyen Completed va tich diem neu co.",
                "Tra ve manifest moi sau khi check-out."));

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
                "ticketTypeCode: ADULT (nguyen gia) | CHILD (tren 2 den 12 tuoi: free waterbus, sightseeing giam %) | INFANT (duoi 2 tuoi) | SENIOR (tu 70 tuoi) | DISABLED (khuyet tat).",
                "CHILD mien phi tren waterbus thuong; sightseeing giam theo % tai /api/ticket-types/sightseeing-concession. CHILD bat buoc co ghe rieng va co QR rieng; booking phai co hanh khach ADULT cung chieu/cung chang.",
                "INFANT mien phi; neu khong chon ghe thi di kem trong QR cua hanh khach ADULT cung chieu/cung chang.",
                "SENIOR/DISABLED: mien phi tren waterbus thuong; sightseeing dung chung % giam voi CHILD tai /api/ticket-types/sightseeing-concession.",
                "INFANT/CHILD/SENIOR/DISABLED bat buoc khai bao birthYear. CHILD phai tren 2 tuoi va khong qua 12 tuoi tai ngay khoi hanh; SENIOR phai tu 70 tuoi tai ngay khoi hanh.",
                "CHILD chi can co it nhat mot ADULT co ghe di cung chieu/chang; nhieu CHILD co the di kem cung mot ADULT vi moi CHILD co ghe/QR rieng.",
                "Moi INFANT khong ghe phai co mot ADULT co ghe di kem trong cung chieu/chang.",
                "fromStationCode / toStationCode: khuyến nghị lấy từ GET /api/trips/{id} → stops[].stationCode; BE cũng chấp nhận stationId hoặc stationName.",
                "Chi bat buoc tren chuyen ban ve theo chang (sellsBySegment=true trong GET /api/trips/{id} hoac /seats); "
                    + "chuyen ngam canh (routeType=SightseeingLoop) di nguyen chuyen nen BO TRONG ca hai, BE tu lay ben dau → ben cuoi cua tuyen.",
                "fromStationCode phai co stop_order nho hon toStationCode (chuyen ban theo chang).",
                "Toi da 10 ghe trong 1 lan dat.",
                "Gia tu dong tinh theo gia cua seatTypeCode cua ghe x ticket type modifier.",
                "Email chung bat buoc: booking online lay tu email tai khoan dang nhap; email nay nhan QR tong BK + tat ca ve.",
                "passengerEmail (optional): neu nhap them cho tung hanh khach thi hanh khach do nhan them email ve rieng sau khi thanh toan.",
                "Ve khu hoi (optional): truyen them returnTripCode + returnItems de mua ve 2 chieu trong 1 booking; hai chieu doc lap (trip, ghe, hanh khach rieng), khong giam gia, tong tien = cong 2 chieu.",
                "returnTripCode va returnItems phai di cung nhau; returnItems theo cung rule voi items (toi da 10 ghe/chieu, tre em di kem tinh theo tung chieu).",
                "Bao hiem: dung goi PassengerInsurance active/default, BE tu cong phi theo so passenger item. Gui insuranceSelected=false de khong chon; gui insurancePackageId de chon goi cu the.",
                "bookingStatus sau khi tao: PendingPayment; ghe duoc giu toi da 15 phut, nhung holdExpiresAt khong vuot gio dong ban cua chang (gio tau roi ben len - 10 phut). Qua han booking tu Expired va nha ghe ca 2 chieu.",
                "Tra ve 400 neu ghe da bi dat hoac dang duoc nguoi khac tam giu (race condition)."));

        group.MapPost(PreviewBooking, "preview")
            .RequireAuthorization()
            .WithSummary("Kiem tra va tinh gia booking truoc khi tao")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                CreateBookingExample,
                "Validate chuyến, ghế, hành khách, bảo hiểm và mã khuyến mãi nhưng không tạo booking, không giữ ghế.",
                "Response trả về subtotal/discount/total, line items và holdExpiresAt dự kiến.",
                "Luôn kiểm tra lại lần cuối khi POST /api/bookings vì ghế và giá có thể thay đổi."));

        group.MapGet(LookupCounterBookingCustomer, "counter/customers/lookup")
            .RequireAuthorization()
            .WithSummary("Tra cuu tai khoan khach hang tai quay")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Staff hoặc Manager",
                null,
                "Query: keyword = so dien thoai hoac email.",
                "Chi tra ve customer Active. FE phai hien thong tin khach + pointBalance de staff/khach xac nhan truoc khi tao booking.",
                "Sau khi staff bam OK xac nhan, gui customerUserId + customerConfirmedForPoints=true trong POST /api/bookings/counter de booking duoc tich diem khi hoan tat dich vu; neu khach muon dung diem thi gui them pointsToUse.",
                "Neu khong tim thay hoac khach khong xac nhan thi tao booking khach vang lai, customerUserId = null."));

        group.MapPost(CreateCounterBooking, "counter")
            .RequireAuthorization()
            .WithSummary("Ban ve tai quay (staff)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Staff hoặc Manager",
                CreateCounterBookingExample,
                "Staff/Manager dat ve ho khach mua truc tiep tai quay; khach KHONG bat buoc co tai khoan.",
                "Neu khach muon tich diem: FE goi GET /api/bookings/counter/customers/lookup?keyword=<phone|email>, hien khach tim thay, staff bam OK thi gui customerUserId + customerConfirmedForPoints=true.",
                "Neu khach muon dung diem tai quay: gui pointsToUse > 0 cung customerUserId + customerConfirmedForPoints=true; BE tru diem ngay de giu so du, toi da 50% gia tri don.",
                "items / returnItems / ticketTypeCode / fromStationCode / toStationCode: giong het POST /api/bookings.",
                "contactName + contactPhone + contactEmail bat buoc (staff nhap); contactEmail nhan email tong QR booking.",
                "passengerEmail trong items/returnItems optional; neu nhap them thi gui them email ve rieng cho hanh khach do.",
                "paymentMethod = Cash: ghi nhan thu tien mat ngay -> booking Confirmed, phat hanh ve/QR va gui email ngay trong 1 lan goi.",
                "paymentMethod = BankTransfer: ghi nhan khach da chuyen khoan tai quay -> booking Confirmed, phat hanh ve/QR va gui email ngay trong 1 lan goi.",
                "paymentMethod = PayOs: tra ve checkoutUrl + qrCode cho khach quet; ve chi phat hanh sau khi PayOS bao da thanh toan. "
                    + "Staff theo doi bang POST /api/payments/{paymentId}/sync; qua holdExpiresAt chua tra thi booking het han va nha ghe.",
                "Ban duoc CA KHI TAU DA KHOI HANH: bo han dong ban truoc gio chay, chap nhan chuyen Scheduled/Boarding/Delayed/InProgress; "
                    + "chi tu choi chuyen da Completed hoac Cancelled.",
                "Ghe van kiem tra nhu binh thuong: da ban / dang duoc nguoi khac giu tren chang giao nhau se bi tu choi.",
                "Khong ho tro ma khuyen mai tai quay. Diem chi duoc TICH sau khi booking/trip hoan tat neu customerUserId hop le va customerConfirmedForPoints=true; customerUserId null thi khong tich diem.",
                "Bao hiem PassengerInsurance active/default duoc cong theo so passenger item; gui insuranceSelected=false de khong chon.",
                "Don 0d (tong tien sau tinh gia/giam gia bang 0) luon ghi nhan nhu thu tai quay du chon paymentMethod nao."));
    }

    private static async Task<IResult> GetBookings(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetBookingListQuery(), ct));

    private static async Task<IResult> GetBookingDetail(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetBookingDetailQuery(id), ct));

    private static async Task<IResult> CreateBooking(
        ISender sender, CreateBookingCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> PreviewBooking(
        ISender sender, PreviewBookingCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> CreateCounterBooking(
        ISender sender, CreateCounterBookingCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> LookupCounterBookingCustomer(
        ISender sender, string keyword, CancellationToken ct) =>
        Results.Ok(await sender.Send(new LookupCounterBookingCustomerQuery(keyword), ct));

    private static async Task<IResult> GetBookingManifestByCode(
        ISender sender, string bookingCode, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetBookingManifestByCodeQuery(bookingCode), ct));

    private static async Task<IResult> GetBookingManifestByQrToken(
        ISender sender, string qrToken, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetBookingManifestByQrTokenQuery(qrToken), ct));

    private static async Task<IResult> CheckInAllBookingTickets(
        ISender sender, string qrToken, string? tripCode, CancellationToken ct) =>
        Results.Ok(await sender.Send(new CheckInAllBookingTicketsCommand(qrToken, tripCode), ct));

    private static async Task<IResult> CheckOutAllBookingTickets(
        ISender sender, string qrToken, string? tripCode, CancellationToken ct) =>
        Results.Ok(await sender.Send(new CheckOutAllBookingTicketsCommand(qrToken, tripCode), ct));

    private static async Task<IResult> ResendBookingTickets(
        ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new ResendBookingTicketsCommand(id), ct));
}
