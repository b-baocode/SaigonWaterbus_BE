using System.Globalization;
using System.Text.Json;
using SaigonWaterbus.Application.Common.Interfaces;
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
          "serviceFeeAmount": 400000,
          "discountCode": "TESTPAY",
          "priceNote": "Giá được hệ thống tính theo tàu và đơn vị thuê khách đã chọn."
        }
        """;

    private const string UpdateExample =
        """
        {
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

    private const string PreferredVesselExample =
        """
        {
          "vesselId": "00000000-0000-0000-0000-000000000000"
        }
        """;

    private const string CancelExample =
        """
        {
          "reason": "Khách thay đổi kế hoạch.",
          "refundBankBin": "970415",
          "refundAccountNumber": "123456789",
          "refundAccountName": "NGUYEN VAN A"
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
          "qrToken": "swb:custom-booking:QR_TOKEN_AFTER_FULL_PAYMENT"
        }
        """;

    private const string ReissueTicketExample =
        """
        {
          "reason": "QR không quét được tại cổng check-in."
        }
        """;

    private const string PassengerManifestExample =
        """
        {
          "passengers": [
            {
              "fullName": "Nguyen Van A",
              "dateOfBirth": "20/06/1995"
            },
            {
              "fullName": "Nguyen Van C",
              "dateOfBirth": "21/06/2018"
            }
          ]
        }
        """;

    private const string PayOsWebhookExample =
        """
        {
          "code": "00",
          "desc": "success",
          "success": true,
          "data": {
            "orderCode": 123,
            "amount": 2500000,
            "description": "SWB000123",
            "accountNumber": "12345678",
            "reference": "TF230204212323",
            "transactionDateTime": "2026-06-19 18:25:00",
            "currency": "VND",
            "paymentLinkId": "124c33293c43417ab7879e14c8d9eb18",
            "code": "00",
            "desc": "Thanh cong",
            "counterAccountBankId": "",
            "counterAccountBankName": "",
            "counterAccountName": "",
            "counterAccountNumber": "",
            "virtualAccountName": "",
            "virtualAccountNumber": ""
          },
          "signature": "signature"
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

        group.MapGet(GetCustomBookingRentalServices, "rental-services")
            .RequireAuthorization()
            .WithSummary("Danh sách dịch vụ thuê tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer/Admin/Manager/Staff",
                null,
                "Trả về các service đang active có BookingMode=VesselRental.",
                "Customer không cần gọi endpoint này khi tạo yêu cầu; backend tự dùng service thuê tàu mặc định."));

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
                "Customer không gửi serviceId; backend tự chọn dịch vụ thuê tàu mặc định WT.",
                "Khách không chọn tàu cụ thể, chỉ chọn số tầng và kiểu ghế.",
                "rentalUnit là Hour hoặc Day; backend dùng đơn vị này để lọc tàu và tự tính báo giá sau khi Admin gán tàu.",
                "requestedSeatSetupType nhận FullStandard hoặc StandardAndVip.",
                "Booking luôn phải có email nhận thông tin vé và email phải thuộc @gmail.com hoặc @fpt.edu.vn.",
                "useAccountContact=true ưu tiên email trong profile; nếu profile chưa có email thì phải gửi contactEmail.",
                "Email nhập trong booking chỉ lưu cho yêu cầu này, không cập nhật profile.",
                "useAccountContact=false phải gửi contactName, contactPhone và contactEmail.",
                "Ngày và giờ khởi hành phải ở tương lai.",
                "Tổng khách tối đa 500; backend kiểm tra có ít nhất một tàu phù hợp còn trống.",
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
                "Customer không gửi serviceId; backend tự dùng dịch vụ thuê tàu mặc định.",
                "API cập nhật toàn bộ lịch trình, số khách, tiêu chí tàu và yêu cầu đặc biệt.",
                "Thông tin liên hệ không bị thay đổi."));

        group.MapGet(GetCustomBookingVesselCandidates, "{id:guid}/vessel-candidates")
            .RequireAuthorization()
            .WithSummary("Xem tàu phù hợp còn trống")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer chủ yêu cầu hoặc Admin",
                null,
                "Chỉ dùng khi status=PendingReview.",
                "Backend lọc đúng số tầng, kiểu ghế, sức chứa, trạng thái setup và giá thuê theo rentalUnit khách chọn.",
                "Customer chỉ xem được danh sách tàu của yêu cầu do chính mình tạo.",
                "estimatedBasePrice là unitPrice nhân số giờ/ngày thuê làm tròn lên theo thời lượng chuyến."));

        group.MapPut(SelectPreferredCustomBookingVessel, "{id:guid}/preferred-vessel")
            .RequireAuthorization()
            .WithSummary("Khách chọn tàu mong muốn")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer là chủ yêu cầu",
                PreferredVesselExample,
                "Chỉ chọn được khi status=PendingReview và Admin chưa gán tàu.",
                "Backend kiểm tra lại tàu còn trống, Active, đã setup ghế, đúng số tầng, đúng kiểu ghế, đủ sức chứa và có giá theo rentalUnit.",
                "Đây là tàu khách mong muốn; Admin vẫn phải gán tàu chính thức trước khi báo giá."));

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
                "Chỉ cập nhật lại báo giá khi chưa có payment PayOS pending hoặc paid.",
                "Sau khi báo giá, status=Quoted."));

        group.MapPost(AcceptCustomBookingQuote, "{id:guid}/accept-quote")
            .RequireAuthorization()
            .WithSummary("Khách chấp nhận báo giá")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer là chủ yêu cầu",
                null,
                "Chỉ chấp nhận khi status=Quoted, báo giá chưa hết hạn và tàu vẫn hợp lệ.",
                "Sau khi chấp nhận, backend tạo link thanh toán PayOS cho tiền cọc và trả quote.depositPaymentCheckoutUrl.",
                "Sau khi tạo link đặt cọc, booking vẫn status=Quoted và quote.depositPaymentStatus=Pending.",
                "Chỉ webhook PayOS hợp lệ, success và đúng amount mới chuyển booking sang Confirmed.",
                "Bước này chưa tạo/gửi QR. QR chỉ phát sau khi khách đã hoàn tất danh sách hành khách.",
                "Danh sách hành khách và QR bị khóa cho đến khi booking thanh toán đủ."));

        group.MapPost(CreateCustomBookingRemainingPayment, "{id:guid}/remaining-payment")
            .RequireAuthorization()
            .WithSummary("Khách tạo link thanh toán phần còn lại")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer là chủ yêu cầu",
                null,
                "Chỉ tạo link sau khi booking Confirmed và tiền cọc đã Paid.",
                "Nếu quote.remainingAmount > 0, khách phải thanh toán phần còn lại trước 24 giờ so với giờ khởi hành.",
                "Backend trả quote.remainingPaymentCheckoutUrl và giữ quote.remainingPaymentStatus=Pending cho đến khi webhook PayOS xác nhận.",
                "QR, cấp lại QR và scan/check-in bị khóa cho đến khi booking đã thanh toán đủ."));

        group.MapPost(HandleCustomBookingDepositPaymentWebhook, "payments/payos/webhook")
            .AllowAnonymous()
            .WithSummary("Webhook PayOS xác nhận thanh toán custom booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "PayOS gọi public endpoint, không dùng JWT.",
                PayOsWebhookExample,
                "Backend verify signature bằng PayOs:ChecksumKey.",
                "Nếu orderCode khớp tiền cọc và amount khớp depositAmount thì quote.depositPaymentStatus=Paid và booking chuyển sang Confirmed.",
                "Nếu orderCode khớp phần còn lại và amount khớp remainingAmount thì quote.remainingPaymentStatus=Paid.",
                "Webhook idempotent; PayOS gửi lại nhiều lần không tạo QR hoặc gửi mail lặp."));

        group.MapGet(GetCustomBookingTicket, "{id:guid}/ticket")
            .RequireAuthorization()
            .WithSummary("Xem thông tin vé QR custom booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer chủ yêu cầu, Admin, Manager/Staff được phân công",
                null,
                "Trả metadata của vé QR active.",
                "qrPayload chỉ trả khi booking đã thanh toán đủ.",
                "Chỉ Customer chủ yêu cầu, Admin và Manager được giao booking nhận qrPayload để frontend render lại QR; Staff chỉ nhận metadata để phục vụ scan/check-in.",
                "Vé tạo trước khi có cột qr_token có thể không có qrPayload; dữ liệu cũ cần được backfill bằng token gốc nếu còn giữ."));

        group.MapGet(GetCustomBookingPassengers, "{id:guid}/passengers")
            .RequireAuthorization()
            .WithSummary("Xem danh sách hành khách custom booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer chủ yêu cầu, Admin, Manager/Staff được phân công",
                null,
                "Trả trạng thái manifest và danh sách hành khách hiện tại.",
                "Staff được xem để đối soát vận hành nhưng không được cập nhật."));

        group.MapPut(UpdateCustomBookingPassengers, "{id:guid}/passengers")
            .RequireAuthorization()
            .WithSummary("Cập nhật danh sách hành khách custom booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer chủ yêu cầu, Admin hoặc Manager được giao booking",
                PassengerManifestExample,
                "PUT thay thế toàn bộ danh sách hành khách.",
                "Chỉ cập nhật sau khi booking Confirmed, đã thanh toán đủ và trước khi check-in.",
                "Backend tự tính Adult/Child từ ngày sinh rồi kiểm tra đúng số đã đăng ký.",
                "Trẻ em là người dưới 11 tuổi tại ngày khởi hành; người từ đủ 11 tuổi được tính là Adult.",
                "Khi manifest chuyển hoàn tất, backend tạo vé QR nếu chưa có active ticket và gửi confirmation email không kèm QR."));

        group.MapPost(PreviewImportCustomBookingPassengers, "{id:guid}/passengers/import/preview")
            .RequireAuthorization()
            .DisableAntiforgery()
            .WithSummary("Preview CSV/XLSX danh sách hành khách custom booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer chủ yêu cầu, Admin hoặc Manager được giao booking",
                null,
                "Multipart/form-data field file, hỗ trợ .csv hoặc .xlsx.",
                "Header bắt buộc: FullName/Tên/Họ và tên và DateOfBirth/Ngày sinh.",
                "Các cột khác trong file như giới tính, số điện thoại, email, địa chỉ, ghi chú sẽ bị bỏ qua.",
                "Endpoint chỉ parse, tính Adult/Child và trả preview/error/warning; không lưu DB.",
                "Frontend cho khách kiểm tra/sửa rồi gọi PUT /passengers để xác nhận."));

        group.MapPost(ReissueCustomBookingTicket, "{id:guid}/ticket/reissue")
            .RequireAuthorization()
            .WithSummary("Admin/Manager cấp lại QR custom booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoặc Manager được giao booking",
                ReissueTicketExample,
                "Chỉ dùng khi QR bị lỗi tại bước check-in.",
                "Chỉ cấp lại khi booking Confirmed, đã thanh toán đủ, vé Active, chưa dùng và chưa hết hạn.",
                "Backend tạo token QR mới, QR cũ mất hiệu lực, và ghi audit log kèm lý do."));

        group.MapPost(ScanCustomBookingTicket, "tickets/scan")
            .RequireAuthorization()
            .Accepts<ScanCustomBookingTicketRequest>("application/json", "text/plain")
            .WithSummary("Scan/check-in vé QR custom booking")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                ScanTicketExample,
                "Body nhận được dạng JSON object { \"qrToken\": \"...\" }, JSON string hoặc text/plain.",
                "qrToken nhận raw token hoặc payload dạng swb:custom-booking:{token}.",
                "Backend kiểm tra vé active, booking Confirmed, đã thanh toán đủ, chưa hết hạn và chưa dùng.",
                "Chỉ cho check-in từ 30 phút trước giờ khởi hành; quét sớm hơn trả lỗi và vé vẫn Active.",
                "Scan thành công sẽ set status=Used, qrUsedAt và qrUsedByUserId.",
                "Scan lại cùng mã sẽ báo vé đã được sử dụng."));

        group.MapPost(CancelCustomBookingRequest, "{id:guid}/cancel")
            .RequireAuthorization()
            .WithSummary("Khách hoặc Admin hủy yêu cầu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Customer là chủ yêu cầu hoặc Admin",
                CancelExample,
                "Hủy được khi booking chưa Cancelled.",
                "Nếu payment đang Pending, backend hủy link PayOS rồi chuyển trạng thái payment sang Cancelled.",
                "Nếu đã thanh toán, backend tính hoàn tiền trên tổng số tiền đã Paid: trước giờ khởi hành ít nhất 3 ngày hoàn 100%, ít nhất 24 giờ hoàn 30%, dưới 24 giờ không hoàn.",
                "Booking đủ điều kiện hoàn tiền phải gửi refundBankBin, refundAccountNumber và refundAccountName để tạo lệnh chi PayOS.",
                "Backend lưu người hủy, thời điểm hủy, lý do và trạng thái hoàn tiền."));

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

    private static async Task<IResult> GetCustomBookingRentalServices(
        ISender sender,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetCustomBookingRentalServicesQuery(), ct));

    private static async Task<IResult> CreateCustomBookingRequest(
        ISender sender,
        CreateCustomBookingRequestApiRequest request,
        CancellationToken ct)
    {
        var command = new CreateCustomBookingRequestCommand(
            request.UseAccountContact,
            request.ContactName,
            request.ContactPhone,
            request.ContactEmail,
            null,
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
            request.ItineraryStops);
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
            null,
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

    public sealed record CreateCustomBookingRequestApiRequest(
        bool UseAccountContact = true,
        string? ContactName = null,
        string? ContactPhone = null,
        string? ContactEmail = null,
        int RequestedNumberOfDecks = 0,
        SeatSetupType RequestedSeatSetupType = default,
        VesselRentalUnit RentalUnit = VesselRentalUnit.Day,
        DateOnly DepartureDate = default,
        TimeOnly? PreferredStartTime = null,
        Guid FromStationId = default,
        Guid ToStationId = default,
        int AdultCount = 0,
        int ChildCount = 0,
        string? SpecialRequests = null,
        IReadOnlyCollection<CreateCustomBookingItineraryStopRequest>? ItineraryStops = null);

    private static async Task<IResult> GetCustomBookingVesselCandidates(
        ISender sender,
        Guid id,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetCustomBookingVesselCandidatesQuery(id), ct));

    private static async Task<IResult> SelectPreferredCustomBookingVessel(
        ISender sender,
        Guid id,
        PreferredCustomBookingVesselApiRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new SelectPreferredCustomBookingVesselCommand(id, request.VesselId), ct));

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
            request.PriceNote,
            request.ServiceFeeAmount,
            request.DiscountCode), ct));

    private static async Task<IResult> AcceptCustomBookingQuote(
        ISender sender,
        Guid id,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new AcceptCustomBookingQuoteCommand(id), ct));

    private static async Task<IResult> CreateCustomBookingRemainingPayment(
        ISender sender,
        Guid id,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new CreateCustomBookingRemainingPaymentCommand(id), ct));

    private static async Task<IResult> HandleCustomBookingDepositPaymentWebhook(
        ISender sender,
        CustomBookingDepositPaymentWebhook request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new HandleCustomBookingDepositPaymentWebhookCommand(request), ct));

    private static async Task<IResult> GetCustomBookingTicket(
        ISender sender,
        Guid id,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetCustomBookingTicketQuery(id), ct));

    private static async Task<IResult> GetCustomBookingPassengers(
        ISender sender,
        Guid id,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetCustomBookingPassengerManifestQuery(id), ct));

    private static async Task<IResult> UpdateCustomBookingPassengers(
        ISender sender,
        Guid id,
        UpdateCustomBookingPassengersApiRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateCustomBookingPassengerManifestCommand(id, request.Passengers), ct));

    private static async Task<IResult> PreviewImportCustomBookingPassengers(
        ISender sender,
        Guid id,
        IFormFile file,
        CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var rows = CustomBookingPassengerManifestFileParser.Parse(file.FileName, stream);
        return Results.Ok(await sender.Send(new PreviewCustomBookingPassengerManifestImportCommand(id, rows), ct));
    }

    private static async Task<IResult> ReissueCustomBookingTicket(
        ISender sender,
        Guid id,
        ReissueCustomBookingTicketApiRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new ReissueCustomBookingTicketCommand(id, request.Reason), ct));

    private static async Task<IResult> ScanCustomBookingTicket(
        ISender sender,
        HttpRequest httpRequest,
        CancellationToken ct)
    {
        var request = await ReadScanTicketRequestAsync(httpRequest, ct);
        return Results.Ok(await sender.Send(request, ct));
    }

    private static async Task<ScanCustomBookingTicketRequest> ReadScanTicketRequestAsync(
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(httpRequest.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return new ScanCustomBookingTicketRequest(null);
        }

        var trimmed = body.Trim();
        if (LooksLikeJson(trimmed))
        {
            var request = TryReadScanTicketJson(trimmed);
            if (request is not null)
            {
                return request;
            }
        }

        return new ScanCustomBookingTicketRequest(trimmed);
    }

    private static ScanCustomBookingTicketRequest? TryReadScanTicketJson(string body)
    {
        var request = TryDeserializeScanTicketJson(body);
        if (request is not null || body[^1] != '"')
        {
            return request;
        }

        var withoutTrailingQuote = body[..^1].TrimEnd();
        return LooksLikeJson(withoutTrailingQuote)
            ? TryDeserializeScanTicketJson(withoutTrailingQuote)
            : null;
    }

    private static ScanCustomBookingTicketRequest? TryDeserializeScanTicketJson(string body)
    {
        try
        {
            return body[0] == '"'
                ? new ScanCustomBookingTicketRequest(JsonSerializer.Deserialize<string>(body, JsonSerializerOptions.Web))
                : JsonSerializer.Deserialize<ScanCustomBookingTicketRequest>(body, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool LooksLikeJson(string body) =>
        body.Length > 0 && (body[0] == '{' || body[0] == '"');

    private static async Task<IResult> CancelCustomBookingRequest(
        ISender sender,
        Guid id,
        CancelCustomBookingRequestApiRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new CancelCustomBookingRequestCommand(
            id,
            request.Reason,
            request.RefundBankBin,
            request.RefundAccountNumber,
            request.RefundAccountName), ct));

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
        IReadOnlyCollection<CreateCustomBookingItineraryStopRequest>? ItineraryStops = null);

    public sealed record AssignCustomBookingVesselApiRequest(Guid VesselId);

    public sealed record PreferredCustomBookingVesselApiRequest(Guid VesselId);

    public sealed record QuoteCustomBookingRequestApiRequest(
        decimal DepositPercent,
        decimal ServiceFeeAmount = 0m,
        string? DiscountCode = null,
        string? PriceNote = null);

    public sealed record ReissueCustomBookingTicketApiRequest(string? Reason);

    public sealed record UpdateCustomBookingPassengersApiRequest(
        IReadOnlyCollection<CustomBookingPassengerInput> Passengers);

    public sealed record CancelCustomBookingRequestApiRequest(
        string Reason,
        string? RefundBankBin = null,
        string? RefundAccountNumber = null,
        string? RefundAccountName = null);

    public sealed record AssignCustomBookingManagerApiRequest(Guid ManagerUserId);

    public sealed record UpdateCustomBookingOperationPlanApiRequest(
        IReadOnlyCollection<CustomBookingStaffPlanItem> StaffAssignments,
        IReadOnlyCollection<CustomBookingOperationServicePlanItem> Services);

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
