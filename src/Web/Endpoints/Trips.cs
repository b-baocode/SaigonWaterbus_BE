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
          "routeCode": "R01-BD-TD",
          "boatCode": "BOAT-01",
          "operatingDate": "10/08/2026",
          "departureTime": "2026-08-10T08:30:00+07:00",
          "seatTypePrices": [
            { "seatTypeCode": "CABIN", "price": 25000 },
            { "seatTypeCode": "SKY", "price": 40000 }
          ]
        }
        """;

    private const string GenerateTripsExample =
        """
        {
          "routeCode": "R01-BD-TD",
          "boatCode": "BOAT-01",
          "departureTimes": ["06:00:00", "08:00:00", "10:00:00"],
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

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetTripList, string.Empty)
            .RequireAuthorization()
            .WithSummary("Danh sach chuyen tau (admin)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoac Staff",
                null,
                "Query params (tat ca optional): operatingDate (dd/MM/yyyy hoac dd-MM-yyyy), routeCode (string), status (string), tripType (string), routeType (string).",
                "status hop le: Scheduled | Boarding | Departed | Arrived | Cancelled.",
                "tripType hop le: Regular | Charter. Trip charter sinh tu charter booking (xem sourceBookingId).",
                "routeType hop le: Regular | SightseeingLoop | Charter | CharterReference. Dung de tach chuyen waterbus thuong, ngam canh, charter va route nguon.",
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
                "Chi tra ve chuyen co tripStatus=Scheduled va departureTime > now.",
                "availableSeats = so ghe con trong tren CHANG tim kiem (ghe ban theo chang, xem ghi chu seat map)."));

        group.MapGet(SearchSightseeingTrips, "search/sightseeing")
            .AllowAnonymous()
            .WithSummary("Tim chuyen ngam canh theo ngay")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Query params: operatingDate (dd/MM/yyyy, dd-MM-yyyy hoac yyyy-MM-dd). Khong can fromStationId/toStationId vi tuyen ngam canh la vong lap: ben bat dau = ben ket thuc.",
                "Chi tra ve chuyen co route routeType=SightseeingLoop, tripStatus=Scheduled va departureTime > now.",
                "Ghe ban nguyen chuyen (khong theo chang): availableSeats = tong ghe active - so ghe da co ve/dang giu.",
                "minPrice = gia ghe re nhat da chot trong trip_seats x he so loai ve re nhat.",
                "fromStopScheduledDeparture/toStopScheduledArrival = gio khoi hanh/ket thuc cua nguyen chuyen."));

        group.MapGet(GetTripById, "{id:guid}")
            .AllowAnonymous()
            .WithSummary("Chi tiet chuyen tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Tra ve TripDetailDto kem stops[] sap xep theo stop_order.",
                "stops[] lay tu bang trip_stops (lich trinh da luu khi tao trip): gio den/di du kien tung ben, stayDurationMinutes va note (trip charter co thoi gian dung theo yeu cau booking).",
                "Trip cu tao truoc khi co trip_stops -> BE tu suy lich trinh tu route stops + gio khoi hanh nhu truoc.",
                "Ben dau chi co gio di, ben cuoi chi co gio den."));

        group.MapGet(GetTripSeatMap, "{id:guid}/seats")
            .AllowAnonymous()
            .WithSummary("So do ghe cua chuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous (dang nhap de thay ghe minh dang giu = HeldByMe)",
                null,
                "Tra ve toan bo ghe active cua tau theo deck/row/column kem trang thai theo chuyen.",
                "Query ?fromStationCode=&toStationCode=: chang khach dinh di — trang thai ghe tinh theo chang do "
                + "(trip Regular ban ghe theo chang: ghe chi Booked/Held neu co ve/luot giu giao chang). Bo trong = xem ca tuyen.",
                "routeType / sellsBySegment: sellsBySegment=true thi FE phai hoi ben len/xuong va gui fromStationCode/toStationCode "
                + "khi dat ve; false (vd routeType=SightseeingLoop) la di nguyen chuyen, khong hoi va khong gui ben.",
                "status: Available | Held | HeldByMe | Booked | Blocked.",
                "basePrice: ghe STANDARD tren trip Regular = gia theo quang duong cua chang (GET /api/fare-policy); "
                + "ghe khac = gia goc theo loai ghe. Gia ve = basePrice x he so loai ve (GET /api/ticket-types).",
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
                "Admin hoac Manager",
                CreateTripExample,
                "Route phai Active va co it nhat 2 ben dung.",
                "boatCode BAT BUOC: trip luon gan tau de sinh trip_seats (khong co ghe thi khong ban ve duoc).",
                "capacity KHONG con nhap tay - capacitySnapshot tu dong = so ghe ACTIVE cua tau (co the nho hon Boat.SeatCount neu co ghe bi vo hieu hoa).",
                "Tau phai Status=Active va SeatsConfigured=true, va co it nhat 1 ghe active; neu khong tra 400.",
                "CHAN TRUNG LICH TAU: mot tau khong duoc gan 2 chuyen chong gio (ke ca khac tuyen); giua 2 chuyen phai cach it nhat 15 phut quay dau -> neu khong tra 400.",
                "departureTime phai lon hon thoi diem hien tai.",
                "seatTypePrices (optional): chot gia ve theo loai ghe cho rieng chuyen nay (bus sightseeing tuy chinh gia).",
                "Loai ghe khong nhap gia se tu dong lay gia goc tu GET /api/seat-types dien vao trip_seats.",
                "Gia ve khi dat = trip_seats.price x he so loai ve (ADULT x1; INFANT/SENIOR/DISABLED mien phi, chi ghe STANDARD).",
                "tripCode tu sinh: TR-{yyyyMMdd}-{routeCode}-{4 so ngau nhien}."));

        group.MapPost(GenerateTrips, "generate")
            .RequireAuthorization()
            .WithSummary("Tao hang loat chuyen tau theo lich")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                GenerateTripsExample,
                "routeCode, boatCode: bat buoc.",
                "departureTimes: mang gio khoi hanh (gio Vietnam +07:00), dinh dang HH:mm:ss.",
                "fromDate / toDate: khoang ngay tao chuyen (toi da 365 ngay).",
                "daysOfWeek (optional): [0=CN, 1=T2, ..., 6=T7]. Bo trong = tat ca cac ngay.",
                "seatTypePrices (optional): chot gia ve theo loai ghe cho tat ca chuyen duoc tao trong dot nay.",
                "Loai ghe khong nhap gia se lay gia goc tu GET /api/seat-types.",
                "Neu chuyen da ton tai (cung tuyen + cung gio), tu dong bo qua (skip).",
                "CHAN TRUNG LICH TAU: chuyen nao lam tau chong gio voi chuyen khac (ke ca chuyen vua sinh trong cung lo) se bi bo qua va dem vao skippedBoatBusy. Giua 2 chuyen cua cung tau phai cach it nhat 15 phut quay dau.",
                "Vi du: route dai 3h41 ma dat departureTimes cach nhau 2h thi cac chuyen sau se bi skippedBoatBusy - can gian gio hoac dung tau khac.",
                "Tra ve: { created, skipped, skippedBoatBusy, createdTripCodes }."));

        group.MapPatch(UpdateTripStatus, "{id:guid}/status")
            .RequireAuthorization()
            .WithSummary("Cap nhat trang thai chuyen tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoac Staff",
                UpdateStatusExample,
                "tripStatus hop le: Scheduled | Boarding | Departed | Arrived | Cancelled.",
                "statusNote: ghi chu kem theo (optional)."));
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
