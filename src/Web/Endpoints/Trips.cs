using System.Globalization;
using SaigonWaterbus.Application.Landmarks;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Trips : IEndpointGroup
{
    public static string RoutePrefix => "/api/trips";

    private const string ScheduleTripsExample =
        """
        {
          "routeCode": "R01-BD-TD",
          "boatCode": "BOAT-01",
          "departureTimes": ["08:00:00", "10:00:00"],
          "startTime": null,
          "endTime": null,
          "intervalMinutes": null,
          "fromDate": "2026-07-01",
          "toDate": "2026-07-31",
          "daysOfWeek": [1, 2, 3, 4, 5],
          "stops": [
            { "stopOrder": 2, "stayDurationMinutes": 5 }
          ]
        }
        """;

    private const string ClearTripsByDateExample =
        """
        {
          "operatingDate": "2026-08-30",
          "confirmDelete": true
        }
        """;

    private const string CreateDailyTripBatchExample =
        """
        {
          "operatingDate": "2026-08-30",
          "confirmCreate": true,
          "items": [],
          "plans": [
            {
              "routeCode": "WB-BD-LB",
              "boatCodes": ["WB_001", "WB_002", "WB_003", "WB_004"],
              "startTime": "14:35:00",
              "endTime": "17:00:00",
              "intervalMinutes": 10,
              "stayDurationMinutes": 5
            }
          ]
        }
        """;

    private const string UpdateStatusExample =
        """
        {
          "tripStatus": "Boarding",
          "statusNote": "Tau dang len khach tai Ben Bach Dang"
        }
        """;

    private const string RoundTripPreviewExample =
        """
        {
          "boatCode": "BOAT-01",
          "outboundRouteCode": "R01-BD-LD",
          "inboundRouteCode": "R02-LD-BD",
          "fromDate": "2026-07-01",
          "toDate": "2026-07-01",
          "startTime": "08:00:00",
          "endTime": "17:00:00",
          "daysOfWeek": null,
          "outboundStops": [
            { "stopOrder": 2, "stayDurationMinutes": 5 }
          ],
          "inboundStops": [
            { "stopOrder": 2, "stayDurationMinutes": 5 }
          ]
        }
        """;

    private const string ReplaceBoatExample =
        """
        {
          "boatId": "00000000-0000-0000-0000-000000000001"
        }
        """;

    private const string CancelNoShowExample =
        """
        {
          "statusNote": "Khách không có mặt tại bến"
        }
        """;

    private const string StartDelayExample =
        """
        {
          "reason": "Tàu đang dừng xử lý sự cố tại bến Bạch Đằng",
          "startStopOrder": 2
        }
        """;

    private const string ResumeDelayExample =
        """
        {
          "note": "Tàu tiếp tục hành trình"
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetTripList, string.Empty)
            .RequireAuthorization()
            .WithSummary("Danh sach chuyen tau (admin)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Staff",
                null,
                "Query params (tat ca optional): operatingDate (dd/MM/yyyy hoac dd-MM-yyyy), routeCode (string), status (string), tripType (string), routeType (string).",
                "status hop le: Scheduled | Boarding | InProgress | Completed | Delayed | Cancelled.",
                "tripType hop le: Regular | Charter. Trip charter sinh tu charter booking (xem sourceBookingId).",
                "routeType hop le: Regular | SightseeingLoop | Charter | CharterReference. Dung de tach chuyen waterbus thuong, ngam canh, charter va route nguon.",
                "totalPassengerCount = so khach cua chuyen, moi BookingPassenger chi dem 1 lan.",
                "Response co boatId/boatCode/boatName/boatImageUrl/boatImageUrls, fromStation/toStation, stopCount va sourceBookingCode de FE render card trip day du.",
                "Sap xep: ngay moi nhat → gio khoi hanh tang dan."));

        group.MapGet(SearchTrips, "search")
            .AllowAnonymous()
            .WithSummary("Tim chuyen waterbus thuong theo hanh trinh va ngay")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Query params: fromStationId (guid), toStationId (guid), operatingDate (dd/MM/yyyy hoac dd-MM-yyyy).",
                "Tìm chiều về của vé khứ hồi: truyền earliestDepartureTime (ISO 8601) bằng giờ khách xuống ở chiều đi; BE chỉ trả chuyến rời bến lên sau mốc này.",
                "Chi tra ve chuyen waterbus thuong (routeType=Regular, tripType=Regular); chuyen charter khong xuat hien.",
                "Chuyen ngam canh tim bang GET /api/trips/search/sightseeing (khong can chon ben).",
                "Chi tra ve chuyen co tripStatus=Scheduled/Boarding/InProgress/Delayed va chặng còn trước giờ rời bến lên tối thiểu 10 phút.",
                "FE dùng isBookable/isBookingClosed trong response để enable/disable chọn chuyến; không khóa chỉ vì tripStatus=Boarding/InProgress.",
                "availableSeats = so ghe con trong tren CHANG tim kiem (ghe ban theo chang, xem ghi chu seat map).",
                "minPrice da ap dung phu thu theo fareAdjustment neu ngay chay la cuoi tuan/le/dac biet.",
                "Response co stops[] gom full lo trinh cua trip; FE dung isSelectedFrom/isSelectedTo de in dam ben khach chon, isWithinSelectedSegment de highlight doan dang dat."));

        group.MapGet(SearchSightseeingTrips, "search/sightseeing")
            .AllowAnonymous()
            .WithSummary("Tim chuyen ngam canh theo ngay")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Query params: operatingDate (dd/MM/yyyy, dd-MM-yyyy hoac yyyy-MM-dd). Khong can fromStationId/toStationId vi tuyen ngam canh la vong lap: ben bat dau = ben ket thuc.",
                "Chi tra ve chuyen co route routeType=SightseeingLoop, tripStatus=Scheduled/Boarding/InProgress/Delayed va còn trước giờ rời bến lên tối thiểu 10 phút.",
                "FE dùng isBookable/isBookingClosed trong response để enable/disable chọn chuyến; không khóa chỉ vì tripStatus=Boarding/InProgress.",
                "Ghe ban nguyen chuyen (khong theo chang): availableSeats = tong ghe active - so ghe da co ve/dang giu.",
                "minPrice = gia ghe re nhat theo seat_types x he so loai ve re nhat, da ap dung phu thu fareAdjustment neu co.",
                "fromStopScheduledDeparture/toStopScheduledArrival = gio khoi hanh/ket thuc cua nguyen chuyen."));

        group.MapGet(GetTripById, "{id:guid}")
            .AllowAnonymous()
            .WithSummary("Chi tiet chuyen tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Tra ve TripDetailDto kem thong tin tau, staff tren tau, totalPassengerCount va stops[] sap xep theo stop_order.",
                "boat co imageUrl/imageUrls va thong tin tau; fromStation/toStation va moi stop co stationImageUrl/stationImageUrls, address, toa do va tien ich ben.",
                "Moi stop co boardingPassengerCount, alightingPassengerCount, onboardPassengerCount va segmentPassengerCount; khach di A->C dem 1 lan trong totalPassengerCount nhung tinh vao ca doan A-B va B-C.",
                "stops[] lay tu bang trip_stops (lich trinh da luu khi tao trip): gio den/di du kien tung ben, stayDurationMinutes va note (trip charter co thoi gian dung theo yeu cau booking).",
                "Trip cu tao truoc khi co trip_stops -> BE tu suy lich trinh tu route stops + gio khoi hanh nhu truoc.",
                "incidentInfo neu co su co gan voi trip: gom tau goc bi su co, tau cuu ho, tau thay the, mission, so khach bi anh huong va delay de FE show banner.",
                "Ben dau chi co gio di, ben cuoi chi co gio den."));

        group.MapGet(GetTripLandmarks, "{id:guid}/landmarks")
            .AllowAnonymous()
            .WithSummary("Diem thuyet minh cua mot chuyen (huong dan vien)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Tron bo du lieu man huong dan vien cua 1 chuyen: routePath (toa do ve duong di), stations (ben kem toa do) "
                + "va landmarks[] ma tuyen di ngang qua — da loc theo triggerRadiusMeters va sap theo thu tu gap doc duong.",
                "Landmark khong gan tuyen (dinh danh bang toa do); viec 'chuyen nay di qua diem nao' tinh o BE de moi client giong nhau. "
                + "Thay cho viec tu goi GET /api/routes/{routeId} + GET /api/stations + GET /api/landmarks roi tu chieu hinh hoc.",
                "voiceId (optional): chon giong. Bo trong (hoac giong da tat) thi dung giong mac dinh (displayOrder nho nhat).",
                "alongMeters = quang duong doc tuyen toi diem do; distanceToPathMeters = khoang cach tu diem toi tim tuyen.",
                "audioUrl null = giong do chua bake tieng cho diem nay; missingAudioCount dem so diem nhu vay.",
                "Tuyen chua ve routeGeometry thi routePath lay tam duong noi cac ben.",
                "FE tai 1 lan luc mo man hinh, sau do bam GPS (GET /api/tracking/trips/{tripId}/latest + hub /hubs/tracking) "
                + "va tu phat audio khi tau vao trong ban kinh."));

        group.MapGet(GetTripPassengers, "{id:guid}/passengers")
            .RequireAuthorization()
            .WithSummary("Danh sach khach cua mot chuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoac Staff",
                null,
                "Tra ve danh sach hanh khach/ve cua dung trip, gom bookingCode, thong tin lien he booking, ten khach, loai ve, ghe, gia ve, ga len/ga xuong, gio len/xuong du kien va trang thai ticket.",
                "Dung cho man hinh operation/trip manifest khi nhan vao mot chuyen de xem ai da mua/giu ve tren chuyen do.",
                "Booking PendingPayment con han giu cho van co the xuat hien voi ticketStatus=null; FE dua vao bookingStatus/paymentStatus/ticketStatus de hien thi.",
                "Khach booking khu hoi chi hien trong dung chieu cua passenger.TripId; khong tron chieu di va chieu ve.",
                "Chi ve Active moi canCheckIn=true; ve da check-in/check-out/cancel/expired khong check-in lai."));

        group.MapGet(GetTripSeatMap, "{id:guid}/seats")
            .AllowAnonymous()
            .WithSummary("So do ghe cua chuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous (dang nhap de thay ghe minh dang giu = HeldByMe)",
                null,
                "Tra ve toan bo ghe active cua tau theo deck/row/column kem trang thai theo chuyen.",
                "Query ?fromStationCode=&toStationCode=: chấp nhận stationCode, stationId hoặc stationName; khuyến nghị dùng stationCode. Chặng khách định đi — trạng thái ghế tính theo chặng đó "
                + "(trip Regular ban ghe theo chang: ghe chi Booked/Held neu co ve/luot giu giao chang). Bo trong = xem ca tuyen.",
                "routeType / sellsBySegment: sellsBySegment=true thi FE phai hoi ben len/xuong va gui fromStationCode/toStationCode "
                + "khi dat ve; false (vd routeType=SightseeingLoop) la di nguyen chuyen, khong hoi va khong gui ben.",
                "status: Available | Held | HeldByMe | Booked | Blocked.",
                "basePrice: ghe STANDARD tren trip Regular = gia theo quang duong cua chang (GET /api/fare-policy); "
                + "ghe khac = gia goc theo loai ghe; da ap dung phu thu fareAdjustment neu co. Gia ve = basePrice x he so loai ve (GET /api/ticket-types).",
                "fareAdjustment cho FE biet dang ap phu thu Weekend/Holiday/Special bao nhieu phan tram.",
                "holdTtlSeconds: thoi gian giu ghe tam khi goi POST /api/trips/{id}/seats/hold.",
                "Realtime: subscribe SignalR hub /hubs/trip-seats, goi JoinTrip(tripId) de nhan event SeatStatusChanged."));

        group.MapPost(HoldTripSeats, "{id:guid}/seats/hold")
            .RequireAuthorization()
            .WithSummary("Tam giu ghe khi dang chon")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                """{ "seatNumbers": ["A1", "A2"], "fromStationCode": "BB", "toStationCode": "LT" }""",
                "Giu ghe 3 phut (TTL tu gia han khi goi lai). Toi da 10 ghe.",
                "fromStationCode/toStationCode: bat buoc voi trip Regular (ghe ban theo chang); bo trong voi sightseeing.",
                "Tra ve heldSeatNumbers + failedSeatNumbers (ghe nguoi khac dang giu chang giao nhau) + holdExpiresAt.",
                "Ghe da co booking active giao chang se tra 400.",
                "Cac client khac dang xem so do ghe nhan duoc event SeatStatusChanged qua SignalR."));

        group.MapPost(ReleaseTripSeats, "{id:guid}/seats/release")
            .RequireAuthorization()
            .WithSummary("Nha ghe dang tam giu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                """{ "seatNumbers": ["A1"] }""",
                "Chi nha duoc ghe do chinh user dang giu; ghe cua nguoi khac bi bo qua.",
                "Tra ve 204."));

        group.MapPost(CreateTrip, string.Empty)
            .RequireAuthorization()
            .WithName("CreateTripLegacy")
            .ExcludeFromDescription();

        group.MapPost(PreviewTripsSchedule, "schedule/preview")
            .RequireAuthorization()
            .WithSummary("Preview tao mot hoac nhieu chuyen tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                ScheduleTripsExample,
                "API chi kiem tra lich, KHONG tao trip.",
                "Payload giong POST /api/trips/schedule.",
                "Tra ve items[] theo tung moc gio co dinh, moi item co canCreate=true/false.",
                "Neu canCreate=false thi co reason, earliestAllowedDepartureTime va conflictTripCode de FE canh bao truoc khi tao.",
                "FE nen goi endpoint nay truoc, hien canh bao neu hasWarnings=true, roi chi goi POST /api/trips/schedule sau khi admin xac nhan."));

        group.MapPost(ScheduleTrips, "schedule")
            .RequireAuthorization()
            .WithSummary("Tao mot hoac nhieu chuyen tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                ScheduleTripsExample,
                "routeCode, boatCode: bat buoc.",
                "API nay dung chung cho tao 1 chuyen va tao nhieu chuyen.",
                "Khuyen nghi FE goi POST /api/trips/schedule/preview truoc de canh bao admin; endpoint nay moi tao trip that.",
                "Tao 1 chuyen: fromDate = toDate va departureTimes co dung 1 gio.",
                "Tao nhieu chuyen: fromDate/toDate la khoang ngay; moi ngay lay cac gio trong departureTimes hoac khoang startTime/endTime/intervalMinutes.",
                "Neu khoang ngay tu 3 ngay tro len, FE co the hien daysOfWeek de chon thu trong tuan; bo trong = tat ca ngay trong khoang.",
                "Cach 1: gui departureTimes: mang gio khoi hanh (gio Vietnam +07:00), dinh dang HH:mm:ss.",
                "Cach 2: gui startTime/endTime/intervalMinutes de BE tao chuyen lien tuc theo kha nang van hanh: chuyen dau o startTime, chuyen tiep theo = gio den chuyen truoc + max(intervalMinutes, 15 phut quay dau) + thoi gian tau di chuyen ve ben xuat phat neu can. Chi tao cac chuyen co gio khoi hanh <= endTime.",
                "fromDate / toDate: khoang ngay tao chuyen (toi da 365 ngay).",
                "daysOfWeek (optional): [0=CN, 1=T2, ..., 6=T7]. Bo trong = tat ca cac ngay.",
                "stops: voi tuyen thuong co ben giua thi bat buoc gui stayDurationMinutes cho tung stopOrder ben giua, giong tao 1 chuyen.",
                "Dung duoc cho ca routeType=Regular va SightseeingLoop; routeCode quyet dinh loai chuyen.",
                "tripCode sinh hang loat: BB-{yyyyMMdd}-{routeCode}-{HHmm} cho bus, BS-{yyyyMMdd}-{routeCode}-{HHmm} cho sightseeing.",
                "Khong nhap gia theo tung dot generate. Gia chuyen tu dong lay theo chinh sach gia hien hanh luc FE xem/dat ve.",
                "Neu chuyen da ton tai (cung tuyen + cung gio), tu dong bo qua (skip).",
                "Gio khoi hanh da troi qua HOAC cach hien tai chua du 10 phut cung bi bo qua, dem vao skippedPast.",
                "CHAN TRUNG LICH TAU: chuyen nao lam tau chong gio voi chuyen khac (ke ca chuyen vua sinh trong cung lo) se bi bo qua va dem vao skippedBoatBusy. Giua 2 chuyen cua cung tau phai cach it nhat 15 phut quay dau va co them thoi gian dua tau ve ben xuat phat neu chuyen truoc ket thuc o ben khac.",
                "CHAN TRUNG BEN: cac chuyen cung routeType xuat phat cung mot ben phai cach nhau toi thieu 5 phut de staff check ve/len tau, neu khong se dem vao skippedStationBusy. Waterbus (Regular) va SightseeingLoop van hanh doc lap.",
                "Moi chuyen bi bo qua nam trong skippedItems[] kem reason, conflictTripCode va earliestAllowedDepartureTime de FE hien gio som nhat co the chay lai.",
                "Chuyen nao thieu 2 nhan vien OnBoard assignmentType=Boat phu thoi gian chuyen se bi bo qua va dem vao skippedMissingOnBoardStaff.",
                "Vi du: route dai 3h41 ma dat departureTimes cach nhau 2h thi cac chuyen sau se bi skippedBoatBusy - can gian gio hoac dung tau khac.",
                "Tra ve: { created, skipped, skippedBoatBusy, skippedStationBusy, skippedPast, createdTripCodes, skippedMissingOnBoardStaff, skippedItems }."));

        group.MapPost(PreviewRoundTripSchedule, "schedule/round-trip-preview")
            .RequireAuthorization()
            .WithSummary("Preview lich di/ve cho mot tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                RoundTripPreviewExample,
                "API chi goi y lich, KHONG tao trip.",
                "Waterbus khứ hồi: FE gui boatCode, route luot di, route luot ve, khoang ngay va gio bat dau/ket thuc.",
                "Route luot ve phai bat dau tai ben cuoi cua route luot di va ket thuc tai ben dau cua route luot di.",
                "SightseeingLoop: chi gui outboundRouteCode; inboundRouteCode de null/bo trong. BE tu preview lap lai cung tuyen sightseeing, khong can route ve gia.",
                "BE tu xen luot di/luot ve theo cong thuc: departure chuyen sau = arrival chuyen truoc + 15 phut quay dau, vi tau dang o dung ben.",
                "Cac chuyen cung routeType xuat phat cung mot ben van phai cach nhau toi thieu 5 phut; neu khong item canCreate=false va dem vao skippedStationBusy. Waterbus (Regular) va SightseeingLoop van hanh doc lap.",
                "Neu tau bi ban voi lich co san, item canCreate=false va reason/suggestedNextDepartureTime cho FE hien thi.",
                "Neu route thuong co ben giua, FE gui outboundStops/inboundStops cho stayDurationMinutes cua tung ben giua.",
                "Admin xem preview xong, FE dung cac item canCreate=true de goi POST /api/trips/schedule tao that."));

        group.MapPost(GenerateTrips, "generate")
            .RequireAuthorization()
            .WithName("GenerateTripsLegacy")
            .ExcludeFromDescription();

        group.MapPatch(UpdateTripStatus, "{id:guid}/status")
            .RequireAuthorization()
            .WithSummary("Cap nhat trang thai chuyen tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Staff",
                UpdateStatusExample,
                "tripStatus hop le: Scheduled | Boarding | InProgress | Completed | Delayed | Cancelled.",
                "statusNote: ghi chu kem theo (optional).",
                "Delayed: he thong tu bao khach co booking tren chuyen (in-app + SignalR); GPS thay tau chay se tu chuyen lai Boarding/InProgress."));

        group.MapPatch(ReplaceTripBoat, "{id:guid}/boat")
            .RequireAuthorization()
            .WithSummary("Thay tau cho trip")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                ReplaceBoatExample,
                "Dung khi trip can doi sang tau khac.",
                "Tau moi phai Active, da setup ghe, hop voi routeType cua trip va ranh lich trong khung gio trip.",
                "Neu trip da co ve, tau moi phai co day du cac ma ghe dang co ve de BE remap ve sang tau moi.",
                "Neu la trip charter, BE dong bo tau moi ve booking/charterBookingBoats va phat realtime de customer refetch.",
                "Khong cho thay tau cho trip Completed/Cancelled."));

        group.MapPost(CancelSightseeingTripNoShow, "{id:guid}/cancel-no-show")
            .RequireAuthorization()
            .WithSummary("Huy chuyen sightseeing vi khach khong den")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                CancelNoShowExample,
                "Chi ap dung routeType=SightseeingLoop.",
                "Dung khi chuyen co booking nhung khach khong co mat tai ben, Admin quyet dinh huy chuyen.",
                "BE set tripStatus=Cancelled va statusNote mac dinh neu FE khong gui.",
                "Cac ve Active cua chuyen se chuyen TicketStatus=Cancelled de khong check-in duoc sau do.",
                "Booking/payment da thanh toan GIU NGUYEN; khong tu dong refund trong flow no-show.",
                "Neu da co ve CheckedIn/CheckedOut thi tra 400, vi khong con dung nghia khach khong den."));

        group.MapPost(AdminCompleteTripAttendance, "{id:guid}/admin/complete-attendance")
            .RequireAuthorization()
            .WithSummary("[Admin] Check-in va check-out toan bo ve cua chuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin only",
                null,
                "Nhap tripId tren URL; khong can body.",
                "Day la thao tac nhanh cho van hanh/demo: moi ve Active se duoc check-in roi check-out ngay; ve dang CheckedIn se duoc check-out.",
                "BO QUA khung gio check-in/check-out thong thuong. Ve Cancelled, Expired va CheckedOut bi bo qua.",
                "BE ghi audit OverrideCheckIn/OverrideCheckOut, cap nhat booking Completed neu toan bo ve cua booking da check-out, va chi Admin duoc goi."));

        group.MapPost(StartTripDelay, "{id:guid}/delay/start")
            .RequireAuthorization()
            .WithSummary("Nhan vien tren tau bat dau bao delay")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Staff OnBoard dang duoc phan ca tren tau",
                StartDelayExample,
                "Dung khi nhan vien tren tau bam nut Delay. Trip se co delayInfo.isDelayActive=true.",
                "startStopOrder optional: ben bat dau delay. Neu FE khong gui, BE tu suy theo trip_stops actual/stopStatus.",
                "Lenh nay chua cong phut delay vao lich. Phut delay duoc tinh khi goi /delay/resume."));

        group.MapPost(ResumeTripDelay, "{id:guid}/delay/resume")
            .RequireAuthorization()
            .WithSummary("Nhan vien tren tau cho tau tiep tuc sau delay")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Staff OnBoard dang duoc phan ca tren tau",
                ResumeDelayExample,
                "Dung khi nhan vien bam tiep tuc. BE tinh so phut tu delayStartedAt den hien tai, cap nhat adjusted time cho cac ben con lai.",
                "BE tinh day chuyen cho cac trip sau cua cung boatId, ke ca trip sau 0h: gio tau san sang = adjustedArrival chuyen truoc + 5 phut quay dau.",
                "Khoang nghi du se hap thu mot phan/toan bo delay; cascade dung ngay khi delay da duoc hap thu. Route khac van co the bi anh huong neu cung tau.",
                "Response tra trip moi nhat + affectedTrips. Realtime SignalR /hubs/tracking event tripDelayUpdated gui theo boat group."));

        group.MapPost(ClearTripsByDate, "internal/clear-day")
            .RequireAuthorization()
            .WithSummary("[INTERNAL] Xoa toan bo trip trong mot ngay")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin only - cong cu BE, FE khong tich hop",
                ClearTripsByDateExample,
                "Xoa tat ca trip co operatingDate dung ngay yeu cau, gom Waterbus, Sightseeing va Charter.",
                "Đồng thời xóa toàn bộ booking liên quan (kể cả booking khứ hồi có một chiều trong ngày), passenger, ticket, payment, scan log, review, incident và GPS gắn với trip.",
                "Point transaction của booking bị xóa và số dư điểm khách được đảo ngược tương ứng. Thao tác không thể hoàn tác.",
                "confirmDelete bắt buộc true. Trip ngày khác được giữ nguyên; booking khứ hồi bị xóa toàn bộ nếu có ít nhất một chiều thuộc ngày được xóa."));

        group.MapPost(CreateDailyTripBatch, "internal/create-daily-batch")
            .RequireAuthorization()
            .WithSummary("[INTERNAL] Tao nhieu trip hon hop trong mot ngay")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin only - cong cu BE, FE khong tich hop",
                CreateDailyTripBatchExample,
                "Có thể dùng items[] để nhập giờ cụ thể cho từng tàu, hoặc plans[] để đặt startTime/endTime/intervalMinutes và phân slot quay vòng qua boatCodes.",
                "Khoảng cách 5 phút tại cùng bến áp dụng riêng trong từng routeType: Waterbus (Regular) và SightseeingLoop vận hành độc lập.",
                "stayDurationMinutes ở cấp item/plan áp dụng đồng loạt cho mọi bến giữa của route; BE tự lấy stopOrder từ route.",
                "Nếu mỗi bến dừng khác nhau thì dùng stops[] gồm stopOrder + stayDurationMinutes. Chỉ gửi một trong hai cách.",
                "SightseeingLoop không có điểm dừng: không gửi stayDurationMinutes/stops, chỉ dùng intervalMinutes để đặt khoảng cách giữa các trip.",
                "Route quyết định trip là Waterbus hay Sightseeing. Khứ hồi gửi hai items hoặc hai plans: route lượt đi và route lượt về.",
                "confirmCreate bat buoc true. Toan bo batch chay trong mot transaction; mot trip khong hop le thi rollback tat ca.",
                "Ap dung day du validation hien co: route/tau, ghe, lich tau, khoang cach tai ben, nhan su on-board, lead time va stops."));
    }

    private static async Task<IResult> GetTripList(
        ISender sender,
        string? operatingDate, string? routeCode, string? status, string? tripType, string? routeType,
        CancellationToken ct)
    {
        if (!TryParseOptionalDateOnly(operatingDate, out var parsedOperatingDate))
        {
            return Results.BadRequest(new { message = "operatingDate phải có định dạng dd/MM/yyyy, dd-MM-yyyy hoặc yyyy-MM-dd." });
        }

        return Results.Ok(await sender.Send(
            new GetTripListQuery(parsedOperatingDate, routeCode, status, tripType, routeType), ct));
    }

    private static async Task<IResult> SearchTrips(
        ISender sender,
        Guid fromStationId, Guid toStationId, string operatingDate,
        DateTimeOffset? earliestDepartureTime,
        CancellationToken ct)
    {
        if (!TryParseRequiredDateOnly(operatingDate, out var parsedOperatingDate))
        {
            return Results.BadRequest(new { message = "operatingDate phải có định dạng dd/MM/yyyy, dd-MM-yyyy hoặc yyyy-MM-dd." });
        }

        return Results.Ok(await sender.Send(
            new SearchTripsQuery(
                fromStationId,
                toStationId,
                parsedOperatingDate,
                earliestDepartureTime), ct));
    }

    private static async Task<IResult> SearchSightseeingTrips(
        ISender sender, string operatingDate, CancellationToken ct)
    {
        if (!TryParseRequiredDateOnly(operatingDate, out var parsedOperatingDate))
        {
            return Results.BadRequest(new { message = "operatingDate phải có định dạng dd/MM/yyyy, dd-MM-yyyy hoặc yyyy-MM-dd." });
        }

        return Results.Ok(await sender.Send(new SearchSightseeingTripsQuery(parsedOperatingDate), ct));
    }

    private static async Task<IResult> GetTripById(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTripDetailQuery(id), ct));

    private static async Task<IResult> GetTripLandmarks(
        ISender sender, Guid id, Guid? voiceId, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTripLandmarksQuery(id, voiceId), ct));

    private static async Task<IResult> GetTripPassengers(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTripPassengerManifestQuery(id), ct));

    private static async Task<IResult> AdminCompleteTripAttendance(
        ISender sender,
        Guid id,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new AdminCompleteTripAttendanceCommand(id), ct));

    private static async Task<IResult> GetTripSeatMap(
        ISender sender, Guid id, string? fromStationCode, string? toStationCode, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTripSeatMapQuery(id, fromStationCode, toStationCode), ct));

    private static async Task<IResult> HoldTripSeats(
        ISender sender, Guid id, TripSeatSelectionRequest request, CancellationToken ct) =>
        Results.Ok(await sender.Send(new HoldTripSeatsCommand(
            id, request.SeatNumbers, request.FromStationCode, request.ToStationCode), ct));

    private static async Task<IResult> ReleaseTripSeats(
        ISender sender, Guid id, TripSeatSelectionRequest request, CancellationToken ct)
    {
        await sender.Send(new ReleaseTripSeatsCommand(id, request.SeatNumbers), ct);
        return Results.NoContent();
    }

    public sealed record TripSeatSelectionRequest(
        IReadOnlyList<string> SeatNumbers,
        string? FromStationCode = null,
        string? ToStationCode = null);

    private static async Task<IResult> CreateTrip(ISender sender, CreateTripCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> ClearTripsByDate(
        ISender sender,
        CleanTripsCommand command,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> CreateDailyTripBatch(
        ISender sender,
        CreateDailyTripBatchCommand command,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> ScheduleTrips(ISender sender, GenerateTripsCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> PreviewTripsSchedule(
        ISender sender,
        PreviewTripsScheduleCommand command,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> GenerateTrips(ISender sender, GenerateTripsCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> PreviewRoundTripSchedule(
        ISender sender,
        PreviewRoundTripScheduleCommand command,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateTripStatus(ISender sender, Guid id, UpdateTripStatusRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateTripStatusCommand(id, req.TripStatus, req.StatusNote), ct));

    public sealed record UpdateTripStatusRequest(TripStatus TripStatus, string? StatusNote);

    private static async Task<IResult> ReplaceTripBoat(ISender sender, Guid id, ReplaceTripBoatRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new ReplaceTripBoatCommand(id, req.BoatId), ct));

    public sealed record ReplaceTripBoatRequest(Guid BoatId);

    private static async Task<IResult> CancelSightseeingTripNoShow(
        ISender sender,
        Guid id,
        CancelSightseeingTripNoShowRequest req,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new CancelSightseeingTripNoShowCommand(id, req.StatusNote), ct));

    public sealed record CancelSightseeingTripNoShowRequest(string? StatusNote);

    private static async Task<IResult> StartTripDelay(ISender sender, Guid id, StartTripDelayRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new StartTripDelayCommand(id, req.Reason, req.StartStopOrder), ct));

    private static async Task<IResult> ResumeTripDelay(ISender sender, Guid id, ResumeTripDelayRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new ResumeTripDelayCommand(id, req.Note), ct));

    public sealed record StartTripDelayRequest(string? Reason, int? StartStopOrder = null);

    public sealed record ResumeTripDelayRequest(string? Note);

    private static bool TryParseOptionalDateOnly(string? value, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!TryParseRequiredDateOnly(value, out var parsedDate))
        {
            return false;
        }

        date = parsedDate;
        return true;
    }

    private static bool TryParseRequiredDateOnly(string? value, out DateOnly date)
    {
        date = default;
        return DateOnly.TryParseExact(
            value,
            ["dd/MM/yyyy", "dd-MM-yyyy", "yyyy-MM-dd"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }
}
