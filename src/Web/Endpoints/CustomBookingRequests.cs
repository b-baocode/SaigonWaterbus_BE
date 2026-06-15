using SaigonWaterbus.Application.CustomBookingRequests;
using SaigonWaterbus.Domain.Enums;
using System.Globalization;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class CustomBookingRequests : IEndpointGroup
{
    public static string RoutePrefix => "/api/custom-booking-requests";

    public static string OpenApiTag => "CustomBookingRequests";

    private const string CreateExample =
        """
        {
          "useAccountContact": true,
          "contactName": null,
          "contactPhone": null,
          "contactEmail": null,
          "preferredVesselId": "00000000-0000-0000-0000-000000000000",
          "departureDate": "20/06/2026",
          "preferredStartTime": "08:30:00",
          "fromStationId": "00000000-0000-0000-0000-000000000000",
          "toStationId": "00000000-0000-0000-0000-000000000000",
          "adultCount": 6,
          "childCount": 2,
          "itineraryStops": [
            {
              "stopOrder": 1,
              "stationId": "00000000-0000-0000-0000-000000000000",
              "stayDurationMinutes": 90,
              "note": "Tham quan"
            }
          ]
        }
        """;

    private const string QuoteExample =
        """
        {
          "quotedPrice": 5000000,
          "depositPercent": 50,
          "currency": "VND",
          "validUntil": "2026-06-15T23:59:59+07:00"
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetCustomBookingRequests)
            .RequireAuthorization()
            .WithSummary("Danh sach yeu cau thue tau custom")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Customer chi thay yeu cau cua minh.",
                "Admin/Manager thay tat ca yeu cau.",
                "Query optional: status, departureDate."));

        group.MapGet(GetCustomBookingStatuses, "statuses")
            .RequireAuthorization()
            .WithSummary("Danh sach trang thai yeu cau thue tau custom")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Dung cho Admin/Manager/Customer hien thi filter va label trang thai.",
                "Moi response custom booking deu co field status.",
                "Admin/Manager co the loc danh sach bang query ?status=PendingReview, ?status=Quoted, ..."));

        group.MapGet(GetCustomBookingRequestDetail, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Chi tiet yeu cau thue tau custom")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Customer chi xem duoc yeu cau cua minh.",
                "Admin/Manager xem duoc tat ca thong tin va bao gia."));

        group.MapPost(CreateCustomBookingRequest)
            .RequireAuthorization()
            .WithSummary("Khach gui yeu cau thue tau custom")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer",
                CreateExample,
                "useAccountContact=true thi backend lay fullName/phone/email tu tai khoan dang nhap.",
                "useAccountContact=false thi khach gui contactName/contactPhone/contactEmail rieng.",
                "preferredVesselId la tau khach chon tu GET /api/fares/vessel-rental-prices.",
                "departureDate dung dinh dang dd/MM/yyyy hoac dd-MM-yyyy.",
                "preferredStartTime la gio khach bat dau len tau; khach khong can nhap gio ket thuc.",
                "adultCount + childCount la tong so khach va khong duoc vuot qua suc chua tau.",
                "fromStationId la ben khach bat dau, toStationId la ben khach ket thuc; hai ben duoc phep trung nhau.",
                "Neu fromStationId va toStationId trung nhau thi phai co it nhat mot itineraryStops.",
                "itineraryStops chi gom cac diem ghe o giua, khong can gui pickup/dropoff.",
                "Moi itineraryStops can stationId, stopOrder va stayDurationMinutes.",
                "Backend tu tinh routeEstimate, gio ket thuc du kien, tong km va thoi gian dua tren route_segments; neu thieu segment thi fallback theo toa do ben.",
                "Backend se check user dang nhap va link user neu phone/email trung user co san.",
                "Status ban dau la PendingReview. Chua tao booking va chua xu ly payment."));

        group.MapPost(QuoteCustomBookingRequest, "{id:guid}/quote")
            .RequireAuthorization()
            .WithSummary("Admin/Manager bao gia yeu cau thue tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                QuoteExample,
                "quotedPrice la tong gia thue tau admin bao cho khach.",
                "depositPercent la phan tram dat coc admin yeu cau.",
                "Backend tu tinh depositAmount = quotedPrice * depositPercent / 100.",
                "Backend tu tinh remainingAmount = quotedPrice - depositAmount.",
                "validUntil la optional; neu gui ISO datetime co timezone, backend se luu UTC de tranh loi PostgreSQL timestamp.",
                "Sau khi bao gia, status = Quoted."));

        group.MapPost(AcceptCustomBookingQuote, "{id:guid}/accept-quote")
            .RequireAuthorization()
            .WithSummary("Khach chot dong y bao gia")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer",
                null,
                "Chi chu yeu cau moi duoc chot bao gia.",
                "Chi chot duoc khi status=Quoted va bao gia chua het han.",
                "Tam thoi chua co payment: sau khi khach dong y, status = Confirmed va xem nhu chot thanh cong.",
                "Sau nay khi co payment, diem nay se chuyen sang tao payment truoc; payment thanh cong moi Confirmed."));
    }

    private static async Task<IResult> GetCustomBookingRequests(
        ISender sender,
        CustomBookingRequestStatus? status,
        string? departureDate,
        CancellationToken ct)
    {
        if (!TryParseOptionalDateOnly(departureDate, out var parsedDepartureDate))
        {
            return Results.BadRequest(new { message = "departureDate phải có định dạng dd/MM/yyyy, dd-MM-yyyy hoặc yyyy-MM-dd." });
        }

        return Results.Ok(await sender.Send(new GetCustomBookingRequestsQuery(status, parsedDepartureDate), ct));
    }

    private static async Task<IResult> GetCustomBookingRequestDetail(
        ISender sender,
        Guid id,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetCustomBookingRequestDetailQuery(id), ct));

    private static IResult GetCustomBookingStatuses() =>
        Results.Ok(new[]
        {
            new CustomBookingStatusApiResponse(
                CustomBookingRequestStatus.PendingReview,
                "Chờ admin xem xét",
                "Khách vừa gửi yêu cầu. Admin/Manager cần kiểm tra thông tin chuyến và báo giá.",
                ["GET /api/custom-booking-requests/{id}", "POST /api/custom-booking-requests/{id}/quote"]),
            new CustomBookingStatusApiResponse(
                CustomBookingRequestStatus.Quoted,
                "Đã báo giá",
                "Admin/Manager đã gửi báo giá. Khách xem lại thông tin và quyết định chốt.",
                ["POST /api/custom-booking-requests/{id}/accept-quote"]),
            new CustomBookingStatusApiResponse(
                CustomBookingRequestStatus.QuoteAccepted,
                "Khách đã đồng ý báo giá",
                "Trạng thái cũ để tương thích dữ liệu trước đây. Luồng mới sẽ dùng Confirmed.",
                []),
            new CustomBookingStatusApiResponse(
                CustomBookingRequestStatus.Confirmed,
                "Đã chốt thành công",
                "Khách đã đồng ý báo giá. Tạm thời chưa có payment nên yêu cầu được xem là chốt thành công.",
                []),
            new CustomBookingStatusApiResponse(
                CustomBookingRequestStatus.QuoteRejected,
                "Khách từ chối báo giá",
                "Khách không đồng ý báo giá.",
                []),
            new CustomBookingStatusApiResponse(
                CustomBookingRequestStatus.Cancelled,
                "Đã hủy",
                "Yêu cầu đã bị hủy.",
                [])
        });

    private static async Task<IResult> CreateCustomBookingRequest(
        ISender sender,
        CreateCustomBookingRequestCommand command,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> QuoteCustomBookingRequest(
        ISender sender,
        Guid id,
        QuoteCustomBookingRequestApiRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new QuoteCustomBookingRequestCommand(
            id,
            request.QuotedPrice,
            request.DepositPercent,
            request.Currency,
            request.ValidUntil), ct));

    private static async Task<IResult> AcceptCustomBookingQuote(
        ISender sender,
        Guid id,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new AcceptCustomBookingQuoteCommand(id), ct));

    public sealed record QuoteCustomBookingRequestApiRequest(
        decimal QuotedPrice,
        decimal DepositPercent,
        string? Currency = null,
        DateTimeOffset? ValidUntil = null);

    public sealed record CustomBookingStatusApiResponse(
        CustomBookingRequestStatus Status,
        string Label,
        string Description,
        IReadOnlyCollection<string> NextActions);

    private static bool TryParseOptionalDateOnly(string? value, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!DateOnly.TryParseExact(
                value,
                ["dd/MM/yyyy", "dd-MM-yyyy", "yyyy-MM-dd"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate))
        {
            return false;
        }

        date = parsedDate;
        return true;
    }
}
