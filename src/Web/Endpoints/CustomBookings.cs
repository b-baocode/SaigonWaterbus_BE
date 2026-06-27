using Microsoft.AspNetCore.Mvc;
using SaigonWaterbus.Application.CustomBookings;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class CustomBookings : IEndpointGroup
{
    public static string RoutePrefix => "/api/custom-bookings";

    private const string CreateCustomBookingExample =
        """
        {
          "departureDate": "2026-07-10",
          "rentalUnit": "Day",
          "durationValue": 1,
          "adultCount": 15,
          "childCount": 5,
          "preferredNumberOfDecks": 2,
          "preferredSeatSetupType": "StandardAndVip",
          "boatRequirements": "Muốn tàu có khu VIP và không gian tổ chức sinh nhật",
          "promotionCode": "SUMMER10",
          "startTime": "08:00:00",
          "fromStationId": null,
          "toStationId": null,
          "itineraryStops": [
            {
              "stationId": "00000000-0000-0000-0000-000000000000",
              "stopOrder": 1,
              "stayDurationMinutes": 30,
              "note": "Don khach"
            },
            {
              "stationId": "00000000-0000-0000-0000-000000000000",
              "stopOrder": 2,
              "stayDurationMinutes": 45,
              "note": "Dung chup hinh"
            }
          ],
          "specialRequests": "Can trang tri sinh nhat"
        }
        """;

    private const string QuoteCustomBookingExample =
        """
        {
          "boatId": "00000000-0000-0000-0000-000000000000",
          "subtotalAmount": null,
          "rentalUnit": "Day",
          "durationValue": 1,
          "promotionCode": "SUMMER10"
        }
        """;

    private const string UpdateStatusExample =
        """
        {
          "bookingStatus": "Completed"
        }
        """;

    private const string UpdatePassengersExample =
        """
        {
          "passengers": [
            {
              "fullName": "Nguyen Van A",
              "dateOfBirth": "20/05/1995"
            },
            {
              "fullName": "Tran Thi B",
              "dateOfBirth": "02/09/2016"
            }
          ]
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetRentalBoats, "rental-boats")
            .WithSummary("Xem gia tau tham khao cho custom booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Public",
                null,
                "Cho khach xem bang gia tau tham khao truoc khi tao custom booking.",
                "Khong giu tau, khong lock lich, khong phai gia cuoi cung.",
                "Chi tra ve tau Active, co gia theo rentalUnit, va du suc chua theo adultCount + childCount.",
                "Khach khong chon tau trong booking; admin van la nguoi chot tau/gia bang API quote.",
                "Vi du: /api/custom-bookings/rental-boats?adultCount=15&childCount=5&rentalUnit=Day&durationValue=1&preferredNumberOfDecks=2"));

        group.MapGet(GetAdminCustomBookings, "admin")
            .RequireAuthorization()
            .WithSummary("Admin xem danh sach custom booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Tra ve tat ca custom booking de admin xu ly yeu cau, nhap tau va chot gia."));

        group.MapGet(GetAdminCustomBookingDetail, "admin/{id:guid}")
            .RequireAuthorization()
            .WithSummary("Admin xem chi tiet custom booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Admin xem chi tiet custom booking bat ky, khong bi gioi han theo owner."));

        group.MapPatch(UpdateAdminCustomBookingStatus, "admin/{id:guid}/status")
            .RequireAuthorization()
            .WithSummary("Admin doi trang thai custom booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateStatusExample,
                "Doi bookingStatus cho custom booking hien co.",
                "Khong dung endpoint nay de chot gia; muon sang Quoted khi chua co gia/tau thi dung PUT /api/custom-bookings/admin/{id}/quote.",
                "Confirmed yeu cau booking da co thanh toan dat coc hoac thanh toan du.",
                "Completed yeu cau paymentStatus = Paid.",
                "Refunded yeu cau paymentStatus = Refunded; muon hoan tien thi dung API refund truoc.",
                "Status hop le cho custom booking: PendingQuote, Quoted, Confirmed, Cancelled, Expired, Completed, Refunded."));

        group.MapPut(QuoteCustomBooking, "admin/{id:guid}/quote")
            .RequireAuthorization()
            .WithSummary("Admin nhap tau va chot gia custom booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                QuoteCustomBookingExample,
                "Dung khi customer da gui yeu cau custom booking nhung chua co tau/gia.",
                "boatId: tau duoc admin chon, tau phai Active va du suc chua theo adultCount + childCount customer da nhap.",
                "subtotalAmount: optional; bo trong/null de backend tu tinh theo gia tau, thoi gian/quang duong; gui so tien neu admin muon override gia chot.",
                "Hour: backend can co du lieu km/thoi gian tu GeoJSON/toa do ben; neu thieu thi tra 400 hoac admin phai nhap subtotalAmount thu cong.",
                "Hour: so gio tinh tien = max(durationValue, thoi gian hanh trinh lam tron len gio).",
                "Day: neu co du lieu km/thoi gian thi so ngay tinh tien = max(durationValue, thoi gian hanh trinh lam tron theo block 12 gio). Neu thieu km/thoi gian thi tinh theo dailyRentalPrice * durationValue.",
                "promotionCode: tuy chon; gui chuoi rong de bo promotion hien tai.",
                "Sau khi quote thanh cong, bookingStatus = Quoted va customer moi tao payment duoc.",
                "Khong cho quote neu booking da co payment Pending/Paid."));

        group.MapGet(GetCustomBookings, string.Empty)
            .RequireAuthorization()
            .WithSummary("Danh sach yeu cau thue tau cua toi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Tra ve tat ca yeu cau thue tron tau (custom booking) cua user dang dang nhap, moi nhat truoc."));

        group.MapGet(GetCustomBookingDetail, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Chi tiet yeu cau thue tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Tra ve day du thong tin mot yeu cau thue tau.",
                "Tra ve 404 neu khong thuoc ve user dang dang nhap."));

        group.MapPost(CancelCustomBooking, "{id:guid}/cancel")
            .RequireAuthorization()
            .WithSummary("Huy yeu cau thue tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Huy mot yeu cau thue tau con hieu luc.",
                "Khong the huy khi da Completed/Refunded.",
                "Tra ve 204 khi huy thanh cong."));

        group.MapPut(UpdateCustomBookingPassengers, "{id:guid}/passengers")
            .RequireAuthorization()
            .WithSummary("Nhap/cap nhat danh sach hanh khach len tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                UpdatePassengersExample,
                "Thay the toan bo danh sach hanh khach cua custom booking.",
                "Moi hanh khach chi can fullName va dateOfBirth.",
                "Backend tu tinh passengerType: Adult tu 12 tuoi tro len, Child duoi 12 tuoi.",
                "So hanh khach khong duoc vuot qua passengerCount da dang ky."));

        group.MapPost(ImportCustomBookingPassengers, "{id:guid}/passengers/import")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<IFormFile>("multipart/form-data")
            .WithSummary("Upload file danh sach hanh khach len tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Chi upload sau khi custom booking da thanh toan du: PaymentStatus = Paid.",
                "Gui multipart/form-data voi field file.",
                "Ho tro .xlsx, .csv, .tsv, .txt.",
                "File chi can cot ten hanh khach va ngay sinh. Header chap nhan: fullName/name/ho ten va dateOfBirth/dob/ngay sinh.",
                "Backend tu tinh adultCount/childCount theo moc 12 tuoi."));

        group.MapPost(CreateCustomBooking, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao yeu cau thue tron tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                CreateCustomBookingExample,
                "Customer khong chon tau. Admin se nhap tau va chot gia bang API quote.",
                "departureDate: phai cach ngay tao booking it nhat 7 ngay.",
                "rentalUnit: Hour hoac Day, la don vi thue khach mong muon.",
                "durationValue: so gio hoac so ngay thue (1-60).",
                "adultCount / childCount: so nguoi lon va tre em khach du kien di; passengerCount backend tu tinh.",
                "preferredNumberOfDecks / preferredSeatSetupType / boatRequirements: yeu cau tham khao de admin chon tau.",
                "fromStationId / toStationId: tuy chon, lay tu GET /api/stations → id.",
                "itineraryStops: tuy chon, danh sach diem dung trung gian; stationId lay tu GET /api/stations → id.",
                "promotionCode: tuy chon, dung chung bang promotions voi booking thuong.",
                "Sau khi thanh toan du, co the upload file danh sach bang POST /api/custom-bookings/{id}/passengers/import.",
                "bookingStatus sau khi tao: PendingQuote."));
    }

    private static async Task<IResult> GetRentalBoats(
        ISender sender,
        [FromQuery] int adultCount,
        [FromQuery] int childCount,
        [FromQuery] BoatRentalUnit rentalUnit = BoatRentalUnit.Day,
        [FromQuery] int durationValue = 1,
        [FromQuery] int? preferredNumberOfDecks = null,
        [FromQuery] SeatSetupType? preferredSeatSetupType = null,
        CancellationToken ct = default) =>
        Results.Ok(await sender.Send(new GetRentalBoatEstimatesQuery(
            adultCount,
            childCount,
            rentalUnit,
            durationValue,
            preferredNumberOfDecks,
            preferredSeatSetupType), ct));

    private static async Task<IResult> GetAdminCustomBookings(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetAdminCustomBookingListQuery(), ct));

    private static async Task<IResult> GetAdminCustomBookingDetail(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetAdminCustomBookingDetailQuery(id), ct));

    private static async Task<IResult> UpdateAdminCustomBookingStatus(
        ISender sender,
        Guid id,
        UpdateCustomBookingStatusRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateCustomBookingStatusCommand(id, request.BookingStatus), ct));

    private static async Task<IResult> QuoteCustomBooking(
        ISender sender,
        Guid id,
        QuoteCustomBookingRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new QuoteCustomBookingCommand(
            id,
            request.BoatId,
            request.SubtotalAmount,
            request.RentalUnit,
            request.DurationValue,
            request.PromotionCode), ct));

    private static async Task<IResult> GetCustomBookings(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetCustomBookingListQuery(), ct));

    private static async Task<IResult> GetCustomBookingDetail(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetCustomBookingDetailQuery(id), ct));

    private static async Task<IResult> CancelCustomBooking(ISender sender, Guid id, CancellationToken ct)
    {
        await sender.Send(new CancelCustomBookingCommand(id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateCustomBookingPassengers(
        ISender sender,
        Guid id,
        UpdateCustomBookingPassengersRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateCustomBookingPassengersCommand(id, request.Passengers), ct));

    private static async Task<IResult> ImportCustomBookingPassengers(
        ISender sender,
        Guid id,
        HttpRequest request,
        CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { message = "Gui multipart/form-data voi field file." });
        }

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new { message = "File danh sach hanh khach la bat buoc." });
        }

        await using var fileStream = file.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream, ct);

        return Results.Ok(await sender.Send(
            new ImportCustomBookingPassengersCommand(id, file.FileName, memoryStream.ToArray()),
            ct));
    }

    private static async Task<IResult> CreateCustomBooking(
        ISender sender, CreateCustomBookingRequest request, CancellationToken ct) =>
        Results.Ok(await sender.Send(new CreateCustomBookingCommand(
            request.DepartureDate,
            request.RentalUnit,
            request.DurationValue,
            request.AdultCount,
            request.ChildCount,
            request.StartTime,
            request.FromStationId,
            request.ToStationId,
            request.ItineraryStops,
            request.PreferredNumberOfDecks,
            request.PreferredSeatSetupType,
            request.BoatRequirements,
            request.PromotionCode,
            request.SpecialRequests), ct));
}
