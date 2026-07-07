using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Payments;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Payments : IEndpointGroup
{
    public static string RoutePrefix => "/api/payments";

    private const string CreatePaymentExample =
        """
        {
          "bookingId": "00000000-0000-0000-0000-000000000000",
          "paymentOption": "Deposit",
          "depositPercent": 50,
          "promotionCode": "WELCOME10"
        }
        """;

    private const string RefundPaymentExample =
        """
        {
          "reason": "Customer refund",
          "bankBin": "970422",
          "accountNumber": "123456789",
          "accountName": "NGUYEN VAN A"
        }
        """;

    private const string ManualRefundPaymentExample =
        """
        {
          "reason": "Admin refunded by bank transfer after PayOS payout failed",
          "referenceId": "BANK-TX-123456",
          "payoutId": null,
          "refundedAt": "2026-07-07T10:00:00Z"
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost(CreatePayment, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao link thanh toan")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                CreatePaymentExample,
                "Dung chung cho booking thuong va charter booking.",
                "Booking thuong chi ho tro paymentOption=Full.",
                "Charter booking ho tro Deposit, Full hoac Remaining.",
                "promotionCode: tuy chon; cho phep nhap/ap ma giam gia ngay tai man thanh toan truoc khi tao link PayOS.",
                "Khong the doi promotionCode khi booking da co payment Pending/Paid.",
                "Sau khi da dat coc, gui paymentOption=Remaining de tao payment phan con lai.",
                "De tuong thich nguoc, Full sau khi da dat coc cung se thanh toan phan con lai."));

        group.MapPost(SyncPayment, "{paymentId:guid}/sync")
            .RequireAuthorization()
            .WithSummary("Dong bo trang thai thanh toan")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Goi PayOS de lay trang thai payment moi nhat.",
                "Neu PayOS tra ve Paid, backend cap nhat booking/payment."));

        group.MapPost(SyncPaymentByOrderCode, "order-code/{orderCode:long}/sync")
            .RequireAuthorization()
            .WithSummary("Dong bo trang thai thanh toan bang PayOS orderCode")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Dung sau khi PayOS redirect ve FE voi query orderCode.",
                "Vi du: POST /api/payments/order-code/123456/sync.",
                "Endpoint cu POST /api/payments/{paymentId}/sync van dung paymentId noi bo trong database."));

        group.MapPost(RefundPayment, "{paymentId:guid}/refund")
            .RequireAuthorization()
            .WithSummary("Hoan tien payment")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                RefundPaymentExample,
                "Tao lenh payout PayOS de hoan tien payment da thanh toan.",
                "Khong nhan amount tu client; backend tu tinh so tien hoan tu payment.Amount, payment.RefundAmount va chinh sach hoan tien."));

        group.MapPost(ManualRefundPayment, "{paymentId:guid}/manual-refund")
            .RequireAuthorization()
            .WithSummary("Admin ghi nhan hoan tien thu cong")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                ManualRefundPaymentExample,
                "Dung khi PayOS payout bi loi va admin da hoan tien ngoai he thong.",
                "Chi cho phep neu payment da co refundStatus=Failed, refundFailureReason, refundReferenceId va refundRequestedAmount tu lan refund PayOS truoc do.",
                "API khong goi PayOS; backend tu tinh so tien can hoan va ghi nhan reason/reference vao payment history.",
                "Backend dung lai refundRequestedAmount da tinh luc PayOS refund loi.",
                "Response tra ve PaymentDto voi refundRequestedAmount/refundAmount/refundMethod/refundReason/refundStatus."));

        group.MapPost(HandlePaymentWebhook, "webhook/payos")
            .WithSummary("Webhook PayOS")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Public PayOS webhook",
                null,
                "Endpoint public de PayOS callback sau khi thanh toan.",
                "Backend validate signature truoc khi cap nhat payment."));
    }

    private static async Task<IResult> CreatePayment(
        ISender sender,
        CreatePaymentRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new CreatePaymentCommand(
            request.BookingId,
            request.PaymentOption,
            request.DepositPercent,
            request.PromotionCode), ct));

    private static async Task<IResult> SyncPayment(ISender sender, Guid paymentId, CancellationToken ct) =>
        Results.Ok(await sender.Send(new SyncPaymentCommand(paymentId), ct));

    private static async Task<IResult> SyncPaymentByOrderCode(ISender sender, long orderCode, CancellationToken ct) =>
        Results.Ok(await sender.Send(new SyncPaymentByOrderCodeCommand(orderCode), ct));

    private static async Task<IResult> RefundPayment(
        ISender sender,
        Guid paymentId,
        RefundPaymentRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new RefundPaymentCommand(
            paymentId,
            request.Reason,
            request.BankBin,
            request.AccountNumber,
            request.AccountName), ct));

    private static async Task<IResult> ManualRefundPayment(
        ISender sender,
        Guid paymentId,
        ManualRefundPaymentRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new ManualRefundPaymentCommand(
            paymentId,
            request.Reason,
            request.ReferenceId,
            request.PayoutId,
            request.RefundedAt), ct));

    private static async Task<IResult> HandlePaymentWebhook(
        ISender sender,
        CharterBookingDepositPaymentWebhook webhook,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new HandlePaymentWebhookCommand(webhook), ct));
}
