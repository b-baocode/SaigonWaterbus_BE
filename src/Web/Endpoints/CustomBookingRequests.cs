using System.Globalization;
using SaigonWaterbus.Application.CustomBookingRequests;
using SaigonWaterbus.Domain.Enums;

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
          "serviceId": null,
          "requestedNumberOfDecks": 2,
          "requestedSeatSetupType": "StandardAndVip",
          "rentalUnit": "Hour",
          "departureDate": "20/06/2026",
          "preferredStartTime": "08:30:00",
          "fromStationId": "00000000-0000-0000-0000-000000000000",
          "toStationId": "00000000-0000-0000-0000-000000000000",
          "adultCount": 6,
          "childCount": 2,
          "specialRequests": "Cần hỗ trợ trang trí sinh nhật",
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
          "depositPercent": 50,
          "priceNote": "Giá được hệ thống tính theo tàu và đơn vị thuê khách đã chọn."
        }
        """;

    private const string UpdateExample =
        """
        {
          "serviceId": null,
          "requestedNumberOfDecks": 2,
          "requestedSeatSetupType": "StandardAndVip",
          "rentalUnit": "Hour",
          "departureDate": "20/06/2026",
          "preferredStartTime": "08:30:00",
          "fromStationId": "00000000-0000-0000-0000-000000000000",
          "toStationId": "00000000-0000-0000-0000-000000000000",
          "adultCount": 6,
          "childCount": 2,
          "specialRequests": "Cần hỗ trợ trang trí sinh nhật",
          "itineraryStops": []
        }
        """;

    private const string AssignVesselExample =
        """
        {
          "vesselId": "00000000-0000-0000-0000-000000000000"
        }
        """;

    private const string CancelExample =
        """
        {
          "reason": "Khách thay đổi kế hoạch."
        }
        """;

    private const string AssignManagerExample =
        """
        {
          "managerUserId": "00000000-0000-0000-0000-000000000000"
        }
        """;

    private const string OperationPlanExample =
        """
        {
          "staffAssignments": [
            {
              "staffUserId": "00000000-0000-0000-0000-000000000000",
              "dutyNote": "Hỗ trợ đón khách và kiểm tra danh sách."
            }
          ],
          "services": [
            {
              "serviceName": "Trang trí sinh nhật",
              "quantity": 1,
              "note": "Thực hiện theo nội dung đã bao gồm trong báo giá."
            }
          ]
        }
        """;

    private const string ScanTicketExample =
        """
        {
          "qrToken": "swb:custom-booking:QR_TOKEN_FROM_ACCEPT_QUOTE"
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetCustomBookingRequests)
            .RequireAuthorization()
            .WithSummary("Danh sách yêu cầu thuê tàu custom")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Customer chỉ thấy yêu cầu của mình.",
                "Admin thấy tất cả yêu cầu.",
                "Manager chỉ thấy yêu cầu được Admin giao; Staff chỉ thấy yêu cầu được Manager phân công.",
                "Query optional: status, departureDate."));

        group.MapGet(GetCustomBookingStatuses, "statuses")
            .RequireAuthorization()
            .WithSummary("Danh sách trạng thái custom booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "State machine chỉ gồm PendingReview, Quoted, Confirmed, Cancelled.",
                "Response có label, mô tả và nextActions cho frontend."));

        group.MapGet(GetCustomBookingRentalServices, "rental-services")
            .RequireAuthorization()
            .WithSummary("Danh sách dịch vụ thuê tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer/Admin/Manager/Staff",
                null,
                "Trả về các service đang active có BookingMode=VesselRental.",
                "Dùng để lấy serviceId cho custom booking; nếu không gửi serviceId khi tạo request thì backend dùng WT mặc định."));

        group.MapGet(GetCustomBookingPricingOptions, "pricing-options")
            .RequireAuthorization()
            .WithSummary("Giá thuê tàu tham khảo")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer/Admin/Manager",
                null,
                "Query bắt buộc: requestedNumberOfDecks, requestedSeatSetupType, rentalUnit, passengerCount.",
                "Chỉ tính từ tàu Active, đã setup ghế, đúng cấu hình, đủ sức chứa và có giá thuê theo đơn vị khách chọn.",
                "Không trả vesselId cho khách và không phải báo giá cuối cùng."));

        group.MapGet(GetCustomBookingRequestDetail, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Chi tiết yêu cầu thuê tàu custom")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Customer chỉ xem được yêu cầu của mình.",
                "Admin xem mọi yêu cầu; Manager/Staff chỉ xem yêu cầu được phân công."));

        group.MapPost(CreateCustomBookingRequest)
            .RequireAuthorization()
            .WithSummary("Khách gửi yêu cầu thuê tàu custom")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer",
                CreateExample,
                "serviceId optional; nếu không gửi, backend tự chọn dịch vụ thuê tàu mặc định WT.",
                "Nếu gửi serviceId thì service phải Active và có BookingMode=VesselRental.",
                "Khách không chọn tàu cụ thể, chỉ chọn số tầng và kiểu ghế.",
                "rentalUnit là Hour hoặc Day; backend dùng đơn vị này để lọc tàu và tự tính báo giá sau khi Admin gán tàu.",
                "requestedSeatSetupType nhận FullStandard hoặc StandardAndVip.",
                "Booking luôn phải có email nhận thông tin vé và email phải thuộc @gmail.com hoặc @fpt.edu.vn.",
                "useAccountContact=true ưu tiên email trong profile; nếu profile chưa có email thì phải gửi contactEmail.",
                "Email nhập trong booking chỉ lưu cho yêu cầu này, không cập nhật profile.",
                "useAccountContact=false phải gửi contactName, contactPhone và contactEmail.",
                "Ngày và giờ khởi hành phải ở tương lai.",
                "Tổng khách tối đa 500; sức chứa tàu được kiểm tra khi Admin gán tàu.",
                "Hai điểm liên tiếp trong lịch trình không được trùng nhau.",
                "Backend tự tính khoảng cách, thời lượng và giờ kết thúc dự kiến.",
                "Status ban đầu là PendingReview."));

        group.MapPut(UpdateCustomBookingRequest, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Khách sửa yêu cầu thuê tàu custom")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer là chủ yêu cầu",
                UpdateExample,
                "Chỉ sửa được khi status=PendingReview và Admin chưa gán tàu.",
                "serviceId optional; nếu gửi thì service phải Active và có BookingMode=VesselRental.",
                "API cập nhật toàn bộ lịch trình, số khách, tiêu chí tàu và yêu cầu đặc biệt.",
                "Thông tin liên hệ không bị thay đổi."));

        group.MapGet(GetCustomBookingVesselCandidates, "{id:guid}/vessel-candidates")
            .RequireAuthorization()
            .WithSummary("Admin xem tàu phù hợp")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Chỉ dùng khi status=PendingReview.",
                "Backend lọc đúng số tầng, kiểu ghế, sức chứa, trạng thái setup và giá thuê theo rentalUnit khách chọn.",
                "estimatedBasePrice là unitPrice nhân số giờ/ngày thuê làm tròn lên theo thời lượng chuyến."));

        group.MapPut(AssignCustomBookingVessel, "{id:guid}/assigned-vessel")
            .RequireAuthorization()
            .WithSummary("Admin gán tàu cho yêu cầu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                AssignVesselExample,
                "Chỉ gán hoặc đổi tàu khi status=PendingReview.",
                "Backend kiểm tra lại trạng thái tàu, sơ đồ ghế, số tầng, kiểu ghế, sức chứa và giá thuê.",
                "Backend kiểm tra tàu không bị giữ bởi custom booking khác trong cùng khung giờ."));

        group.MapPost(QuoteCustomBookingRequest, "{id:guid}/quote")
            .RequireAuthorization()
            .WithSummary("Admin báo giá yêu cầu thuê tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                QuoteExample,
                "Phải gán tàu phù hợp trước khi báo giá.",
                "Backend tự tính quotedPrice từ giá thuê của tàu theo rentalUnit khách chọn và thời lượng chuyến.",
                "Backend tự tính tiền cọc và số tiền còn lại.",
                "Backend tự đặt validUntil là 24 giờ sau lúc báo giá hoặc giờ khởi hành, lấy thời điểm đến trước.",
                "Có thể cập nhật lại báo giá khi status=Quoted.",
                "Sau khi báo giá, status=Quoted."));

        group.MapPost(AcceptCustomBookingQuote, "{id:guid}/accept-quote")
            .RequireAuthorization()
            .WithSummary("Khách chấp nhận báo giá")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer là chủ yêu cầu",
                null,
                "Chỉ chấp nhận khi status=Quoted, báo giá chưa hết hạn và tàu vẫn hợp lệ.",
                "Sau khi chấp nhận, status=Confirmed.",
                "Backend tạo vé QR nếu chưa có vé active và trả ticket.qrPayload một lần để frontend render QR.",
                "Hiện chưa tạo payment hoặc lịch chạy; đó là bước nghiệp vụ tiếp theo."));

        group.MapGet(GetCustomBookingTicket, "{id:guid}/ticket")
            .RequireAuthorization()
            .WithSummary("Xem thông tin vé QR custom booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer chủ yêu cầu, Admin, Manager/Staff được phân công",
                null,
                "Trả metadata của vé QR active.",
                "Vì backend chỉ lưu hash token, endpoint này không trả lại qrToken/qrPayload. Nếu mất QR cần cấp lại token bằng flow riêng."));

        group.MapPost(ScanCustomBookingTicket, "tickets/scan")
            .RequireAuthorization()
            .WithSummary("Scan/check-in vé QR custom booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                ScanTicketExample,
                "qrToken nhận raw token hoặc payload dạng swb:custom-booking:{token}.",
                "Backend kiểm tra vé active, booking Confirmed, chưa hết hạn và chưa dùng.",
                "Scan thành công sẽ set status=Used, qrUsedAt và qrUsedByUserId.",
                "Scan lại cùng mã sẽ báo vé đã được sử dụng."));

        group.MapPost(CancelCustomBookingRequest, "{id:guid}/cancel")
            .RequireAuthorization()
            .WithSummary("Khách hoặc Admin hủy yêu cầu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer là chủ yêu cầu, Admin hoặc Manager",
                CancelExample,
                "Chỉ hủy được khi status=PendingReview hoặc Quoted.",
                "Khách từ chối báo giá cũng dùng endpoint này và ghi lý do.",
                "Backend lưu người hủy, thời điểm hủy và lý do."));

        group.MapGet(GetCustomBookingManagerCandidates, "{id:guid}/manager-candidates")
            .RequireAuthorization()
            .WithSummary("Admin xem Manager phù hợp")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Chỉ dùng sau khi khách đã xác nhận báo giá.",
                "Chỉ trả Manager Active đang được gắn với bến khởi hành."));

        group.MapPut(AssignCustomBookingManager, "{id:guid}/assigned-manager")
            .RequireAuthorization()
            .WithSummary("Admin giao booking cho Manager")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                AssignManagerExample,
                "Chỉ giao booking có status=Confirmed.",
                "Manager phải Active và phụ trách bến khởi hành.",
                "Nếu đổi Manager, kế hoạch Staff và dịch vụ vận hành cũ sẽ bị xóa."));

        group.MapGet(GetCustomBookingStaffCandidates, "{id:guid}/staff-candidates")
            .RequireAuthorization()
            .WithSummary("Manager xem Staff có thể phân công")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager được giao booking",
                null,
                "Chỉ trả Staff Active đang được gắn với bến khởi hành."));

        group.MapPut(UpdateCustomBookingOperationPlan, "{id:guid}/operation-plan")
            .RequireAuthorization()
            .WithSummary("Manager phân Staff và dịch vụ vận hành")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Manager được giao booking",
                OperationPlanExample,
                "PUT thay thế toàn bộ danh sách Staff và dịch vụ vận hành hiện tại.",
                "Dịch vụ ở đây chỉ là kế hoạch thực hiện, không làm thay đổi giá đã xác nhận.",
                "Dịch vụ phát sinh có tính tiền phải chuyển Admin báo giá lại."));
    }

    private static async Task<IResult> GetCustomBookingRequests(
        ISender sender,
        CustomBookingRequestStatus? status,
        string? departureDate,
        CancellationToken ct)
    {
        if (!TryParseOptionalDateOnly(departureDate, out var parsedDepartureDate))
        {
            return Results.BadRequest(new
            {
                message = "departureDate phải có định dạng dd/MM/yyyy, dd-MM-yyyy hoặc yyyy-MM-dd."
            });
        }

        return Results.Ok(await sender.Send(new GetCustomBookingRequestsQuery(status, parsedDepartureDate), ct));
    }

    private static async Task<IResult> GetCustomBookingRequestDetail(
        ISender sender,
        Guid id,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetCustomBookingRequestDetailQuery(id), ct));

    private static async Task<IResult> GetCustomBookingPricingOptions(
        ISender sender,
        int requestedNumberOfDecks,
        SeatSetupType requestedSeatSetupType,
        VesselRentalUnit rentalUnit,
        int passengerCount,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetCustomBookingPricingOptionsQuery(
            requestedNumberOfDecks,
            requestedSeatSetupType,
            rentalUnit,
            passengerCount), ct));

    private static IResult GetCustomBookingStatuses() =>
        Results.Ok(new[]
        {
            new CustomBookingStatusApiResponse(
                CustomBookingRequestStatus.PendingReview,
                "Chờ Admin xử lý",
                "Khách có thể chỉnh sửa hoặc hủy. Admin chọn tàu phù hợp rồi mới báo giá.",
                [
                    "PUT /api/custom-booking-requests/{id}",
                    "GET /api/custom-booking-requests/{id}/vessel-candidates",
                    "PUT /api/custom-booking-requests/{id}/assigned-vessel",
                    "POST /api/custom-booking-requests/{id}/quote",
                    "POST /api/custom-booking-requests/{id}/cancel"
                ]),
            new CustomBookingStatusApiResponse(
                CustomBookingRequestStatus.Quoted,
                "Đã báo giá",
                "Admin đã gán tàu và báo giá. Khách có thể chấp nhận hoặc hủy/từ chối.",
                [
                    "POST /api/custom-booking-requests/{id}/accept-quote",
                    "POST /api/custom-booking-requests/{id}/cancel"
                ]),
            new CustomBookingStatusApiResponse(
                CustomBookingRequestStatus.Confirmed,
                "Đã xác nhận",
                "Khách đã đồng ý báo giá. Admin giao Manager bến khởi hành; Manager phân Staff và dịch vụ vận hành.",
                [
                    "GET /api/custom-booking-requests/{id}/manager-candidates",
                    "PUT /api/custom-booking-requests/{id}/assigned-manager",
                    "GET /api/custom-booking-requests/{id}/staff-candidates",
                    "PUT /api/custom-booking-requests/{id}/operation-plan"
                ]),
            new CustomBookingStatusApiResponse(
                CustomBookingRequestStatus.Cancelled,
                "Đã hủy",
                "Khách hoặc Admin đã hủy. Xem statusReason để biết lý do.",
                [])
        });

    private static async Task<IResult> GetCustomBookingRentalServices(
        ISender sender,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetCustomBookingRentalServicesQuery(), ct));

    private static async Task<IResult> CreateCustomBookingRequest(
        ISender sender,
        CreateCustomBookingRequestCommand command,
        CancellationToken ct)
    {
        var result = await sender.Send(command, ct);
        return Results.Created($"{RoutePrefix}/{result.Id}", result);
    }

    private static async Task<IResult> UpdateCustomBookingRequest(
        ISender sender,
        Guid id,
        UpdateCustomBookingRequestApiRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateCustomBookingRequestCommand(
            id,
            request.ServiceId,
            request.RequestedNumberOfDecks,
            request.RequestedSeatSetupType,
            request.RentalUnit,
            request.DepartureDate,
            request.PreferredStartTime,
            request.FromStationId,
            request.ToStationId,
            request.AdultCount,
            request.ChildCount,
            request.SpecialRequests,
            request.ItineraryStops), ct));

    private static async Task<IResult> GetCustomBookingVesselCandidates(
        ISender sender,
        Guid id,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetCustomBookingVesselCandidatesQuery(id), ct));

    private static async Task<IResult> AssignCustomBookingVessel(
        ISender sender,
        Guid id,
        AssignCustomBookingVesselApiRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new AssignCustomBookingVesselCommand(id, request.VesselId), ct));

    private static async Task<IResult> QuoteCustomBookingRequest(
        ISender sender,
        Guid id,
        QuoteCustomBookingRequestApiRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new QuoteCustomBookingRequestCommand(
            id,
            request.DepositPercent,
            request.PriceNote), ct));

    private static async Task<IResult> AcceptCustomBookingQuote(
        ISender sender,
        Guid id,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new AcceptCustomBookingQuoteCommand(id), ct));

    private static async Task<IResult> GetCustomBookingTicket(
        ISender sender,
        Guid id,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetCustomBookingTicketQuery(id), ct));

    private static async Task<IResult> ScanCustomBookingTicket(
        ISender sender,
        ScanCustomBookingTicketRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(request, ct));

    private static async Task<IResult> CancelCustomBookingRequest(
        ISender sender,
        Guid id,
        CancelCustomBookingRequestApiRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new CancelCustomBookingRequestCommand(id, request.Reason), ct));

    private static async Task<IResult> GetCustomBookingManagerCandidates(
        ISender sender,
        Guid id,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetCustomBookingManagerCandidatesQuery(id), ct));

    private static async Task<IResult> AssignCustomBookingManager(
        ISender sender,
        Guid id,
        AssignCustomBookingManagerApiRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new AssignCustomBookingManagerCommand(id, request.ManagerUserId), ct));

    private static async Task<IResult> GetCustomBookingStaffCandidates(
        ISender sender,
        Guid id,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetCustomBookingStaffCandidatesQuery(id), ct));

    private static async Task<IResult> UpdateCustomBookingOperationPlan(
        ISender sender,
        Guid id,
        UpdateCustomBookingOperationPlanApiRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateCustomBookingOperationPlanCommand(
            id,
            request.StaffAssignments,
            request.Services), ct));

    public sealed record UpdateCustomBookingRequestApiRequest(
        int RequestedNumberOfDecks,
        SeatSetupType RequestedSeatSetupType,
        VesselRentalUnit RentalUnit,
        DateOnly DepartureDate,
        TimeOnly? PreferredStartTime,
        Guid FromStationId,
        Guid ToStationId,
        int AdultCount,
        int ChildCount,
        string? SpecialRequests = null,
        IReadOnlyCollection<CreateCustomBookingItineraryStopRequest>? ItineraryStops = null,
        Guid? ServiceId = null);

    public sealed record AssignCustomBookingVesselApiRequest(Guid VesselId);

    public sealed record QuoteCustomBookingRequestApiRequest(
        decimal DepositPercent,
        string? PriceNote = null);

    public sealed record CancelCustomBookingRequestApiRequest(string Reason);

    public sealed record AssignCustomBookingManagerApiRequest(Guid ManagerUserId);

    public sealed record UpdateCustomBookingOperationPlanApiRequest(
        IReadOnlyCollection<CustomBookingStaffPlanItem> StaffAssignments,
        IReadOnlyCollection<CustomBookingOperationServicePlanItem> Services);

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
