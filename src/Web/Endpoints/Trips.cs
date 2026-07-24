using System.Globalization;
using SaigonWaterbus.Application.Trips;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Trips : IEndpointGroup
{
    public static string RoutePrefix => "/api/trips";

    private const string CreateTripExample =
        """
        {
          "routeId": "00000000-0000-0000-0000-000000000000",
          "routeCode": "R01-BD-TD",
          "boatCode": "BOAT-01",
          "operatingDate": "10/08/2026",
          "departureTime": "2026-08-10T08:30:00+07:00",
          "stops": [
            { "stopOrder": 2, "stayDurationMinutes": 5 }
          ]
        }
        """;

    private const string GenerateTripsExample =
        """
        {
          "routeCode": "R01-BD-TD",
          "boatCode": "BOAT-01",
          "departureTimes": null,
          "startTime": "06:00:00",
          "endTime": "18:00:00",
          "intervalMinutes": 30,
          "fromDate": "2026-07-01",
          "toDate": "2026-07-31",
          "daysOfWeek": [1, 2, 3, 4, 5]
        }
        """;

    private const string UpdateStatusExample =
        """
        {
          "tripStatus": "Boarding",
          "statusNote": "Tau dang len khach tai Ben Bach Dang"
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
                "Chi tra ve chuyen waterbus thuong (routeType=Regular, tripType=Regular); chuyen charter khong xuat hien.",
                "Chuyen ngam canh tim bang GET /api/trips/search/sightseeing (khong can chon ben).",
                "Chi tra ve chuyen co tripStatus=Scheduled/Boarding/InProgress/Delayed va chặng còn trước giờ rời bến lên tối thiểu 10 phút.",
                "FE dùng isBookable/isBookingClosed trong response để enable/disable chọn chuyến; không khóa chỉ vì tripStatus=Boarding/InProgress.",
                "availableSeats = so ghe con trong tren CHANG tim kiem (ghe ban theo chang, xem ghi chu seat map).",
                "minPrice da ap dung phu thu theo fareAdjustment neu ngay chay la cuoi tuan/le/dac biet."));

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
            .WithSummary("Tao chuyen tau moi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                CreateTripExample,
                "Gui routeId hoac routeCode. Route phai Active va co it nhat 2 ben dung.",
                "boatCode BAT BUOC: trip luon gan tau de sinh trip_seats (khong co ghe thi khong ban ve duoc).",
                "stops: voi tuyen thuong co ben giua thi bat buoc gui stayDurationMinutes cho tung stopOrder ben giua (co the la 0); BE dung so phut nay de tinh gio roi ben va cac ben sau, khong lay thoi gian dung co san tu route.",
                "capacity KHONG con nhap tay - capacitySnapshot tu dong = so ghe ACTIVE cua tau (co the nho hon Boat.SeatCount neu co ghe bi vo hieu hoa).",
                "Tau phai Status=Active va SeatsConfigured=true, va co it nhat 1 ghe active; neu khong tra 400.",
                "CHAN TRUNG LICH TAU: mot tau khong duoc gan 2 chuyen chong gio (ke ca khac tuyen); giua 2 chuyen phai cach it nhat 15 phut quay dau -> neu khong tra 400.",
                "Tàu phải có ít nhất 2 nhân viên OnBoard được phân ca assignmentType=Boat phủ toàn bộ thời gian chuyến; nếu thiếu trả 400.",
                "departureTime phai cach thoi diem hien tai it nhat 20 phut; neu khong tra 400.",
                "operatingDate phai khop ngay cua departureTime theo gio Viet Nam (+07); lech ngay tra 400.",
                "Khong nhap gia theo tung trip. Gia ve lay tu GET/PUT /api/seat-types, GET/PUT /api/fare-policy va phu thu /api/fare-policy/adjustments.",
                "Gia ve khi dat = gia goc/chang da ap phu thu x he so loai ve (ADULT x1; INFANT/SENIOR/DISABLED mien phi, chi ghe STANDARD).",
                "tripCode tu sinh theo loai chuyen: BB-{yyyyMMdd}-{routeCode}-{4 so ngau nhien} cho bus, BS-{yyyyMMdd}-{routeCode}-{4 so ngau nhien} cho sightseeing. Charter booking sinh BR-{yyyyMMdd}-{bookingCode}-{boatOrder}."));

        group.MapPost(GenerateTrips, "generate")
            .RequireAuthorization()
            .WithSummary("Tao hang loat chuyen tau theo lich")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                GenerateTripsExample,
                "routeCode, boatCode: bat buoc.",
                "Cach 1: gui departureTimes: mang gio khoi hanh (gio Vietnam +07:00), dinh dang HH:mm:ss.",
                "Cach 2: gui startTime/endTime/intervalMinutes de BE tu tao chuyen lien tuc trong khoang gio. Vi du 06:00-18:00 moi 30 phut.",
                "fromDate / toDate: khoang ngay tao chuyen (toi da 365 ngay).",
                "daysOfWeek (optional): [0=CN, 1=T2, ..., 6=T7]. Bo trong = tat ca cac ngay.",
                "Dung duoc cho ca routeType=Regular va SightseeingLoop; routeCode quyet dinh loai chuyen.",
                "tripCode sinh hang loat: BB-{yyyyMMdd}-{routeCode}-{HHmm} cho bus, BS-{yyyyMMdd}-{routeCode}-{HHmm} cho sightseeing.",
                "Khong nhap gia theo tung dot generate. Gia chuyen tu dong lay theo chinh sach gia hien hanh luc FE xem/dat ve.",
                "Neu chuyen da ton tai (cung tuyen + cung gio), tu dong bo qua (skip).",
                "Gio khoi hanh da troi qua HOAC cach hien tai chua du 20 phut cung bi bo qua, dem vao skippedPast.",
                "CHAN TRUNG LICH TAU: chuyen nao lam tau chong gio voi chuyen khac (ke ca chuyen vua sinh trong cung lo) se bi bo qua va dem vao skippedBoatBusy. Giua 2 chuyen cua cung tau phai cach it nhat 15 phut quay dau.",
                "Chuyen nao thieu 2 nhan vien OnBoard assignmentType=Boat phu thoi gian chuyen se bi bo qua va dem vao skippedMissingOnBoardStaff.",
                "Vi du: route dai 3h41 ma dat departureTimes cach nhau 2h thi cac chuyen sau se bi skippedBoatBusy - can gian gio hoac dung tau khac.",
                "Tra ve: { created, skipped, skippedBoatBusy, skippedPast, createdTripCodes, skippedMissingOnBoardStaff }."));

        group.MapPatch(UpdateTripStatus, "{id:guid}/status")
            .RequireAuthorization()
            .WithSummary("Cap nhat trang thai chuyen tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Staff",
                UpdateStatusExample,
                "tripStatus hop le: Scheduled | Boarding | InProgress | Completed | Delayed | Cancelled.",
                "statusNote: ghi chu kem theo (optional).",
                "Delayed: he thong tu bao khach co booking tren chuyen (in-app + SignalR); GPS thay tau chay se tu chuyen lai Boarding/InProgress."));

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
                "BE tinh day chuyen cho cac trip sau cua cung boatId, cung operatingDate theo cong thuc: gio tau san sang = adjustedArrival chuyen truoc + 15 phut quay dau.",
                "Neu gio tau san sang lon hon gio khoi hanh du kien cua chuyen sau thi chuyen sau bi delay dung phan bi lan gio; route khac van co the bi anh huong neu cung tau.",
                "Response tra trip moi nhat + affectedTrips. Realtime SignalR /hubs/tracking event tripDelayUpdated gui theo boat group."));
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
        CancellationToken ct)
    {
        if (!TryParseRequiredDateOnly(operatingDate, out var parsedOperatingDate))
        {
            return Results.BadRequest(new { message = "operatingDate phải có định dạng dd/MM/yyyy, dd-MM-yyyy hoặc yyyy-MM-dd." });
        }

        return Results.Ok(await sender.Send(
            new SearchTripsQuery(fromStationId, toStationId, parsedOperatingDate), ct));
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

    private static async Task<IResult> GenerateTrips(ISender sender, GenerateTripsCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateTripStatus(ISender sender, Guid id, UpdateTripStatusRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateTripStatusCommand(id, req.TripStatus, req.StatusNote), ct));

    public sealed record UpdateTripStatusRequest(TripStatus TripStatus, string? StatusNote);

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
