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
          "depositPercent": 50
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

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost(CreatePayment, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao link thanh toan")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                CreatePaymentExample,
                "Dung chung cho booking thuong va custom booking.",
                "Booking thuong chi ho tro paymentOption=Full.",
                "Custom booking ho tro Deposit, Full hoac Remaining.",
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

        group.MapPost(RefundPayment, "{paymentId:guid}/refund")
            .RequireAuthorization()
            .WithSummary("Hoan tien payment")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                RefundPaymentExample,
                "Tao lenh payout PayOS de hoan tien payment da thanh toan.",
                "Khong nhan amount tu client; backend tu tinh so tien hoan tu payment.Amount, payment.RefundAmount va chinh sach hoan tien."));

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
            request.DepositPercent), ct));

    private static async Task<IResult> SyncPayment(ISender sender, Guid paymentId, CancellationToken ct) =>
        Results.Ok(await sender.Send(new SyncPaymentCommand(paymentId), ct));

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

    private static async Task<IResult> HandlePaymentWebhook(
        ISender sender,
        CustomBookingDepositPaymentWebhook webhook,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new HandlePaymentWebhookCommand(webhook), ct));
}
