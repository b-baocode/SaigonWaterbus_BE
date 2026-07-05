using SaigonWaterbus.Application.Promotions;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Promotions : IEndpointGroup
{
    public static string RoutePrefix => "/api/promotions";

    private const string CreateExample =
        """
        {
          "promotionCode": "WELCOME10",
          "promotionName": "Chao mung khach moi",
          "promotionType": "Percent",
          "discountValue": 10,
          "minOrderValue": 20000,
          "validFrom": "2026-01-01T00:00:00+07:00",
          "validTo": "2026-12-31T23:59:59+07:00",
          "usageLimit": 1000,
          "accountUsagePolicy": "OncePerAccount"
        }
        """;

    private const string UpdateExample =
        """
        {
          "promotionName": "Chao mung khach moi (mo rong)",
          "discountValue": 15,
          "minOrderValue": 30000,
          "validFrom": "2026-01-01T00:00:00+07:00",
          "validTo": "2026-12-31T23:59:59+07:00",
          "usageLimit": 2000,
          "accountUsagePolicy": "MultiplePerAccount",
          "status": "Active"
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetPromotions, string.Empty)
            .AllowAnonymous()
            .WithSummary("Danh sach khuyen mai")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Query param status (optional): Active | Inactive. Bo trong de lay tat ca.",
                "Sap xep moi nhat truoc."));

        group.MapGet(ValidatePromotion, "validate")
            .AllowAnonymous()
            .WithSummary("Kiem tra ma khuyen mai")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Query params: code (string), subtotalAmount (decimal).",
                "Vi du: GET /api/promotions/validate?code=WELCOME10&subtotalAmount=40000",
                "isValid=false neu: het han, het luot, don hang chua du gia tri toi thieu.",
                "discountAmount = gia tri giam thuc te tinh theo subtotalAmount."));

        group.MapPost(CreatePromotion, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao khuyen mai moi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                CreateExample,
                "promotionCode phai unique (tu dong uppercase).",
                "promotionType (enum): 0 = Percent (giam theo %), 1 = Fixed (giam so tien co dinh).",
                "  Percent: discountValue=10 → giam 10% tren subtotal.",
                "  Fixed: discountValue=5000 → giam 5.000d, toi da bang subtotal.",
                "discountValue: neu Percent thi % giam (10 = giam 10%), neu Fixed thi so tien giam.",
                "usageLimit: null = khong gioi han luot su dung.",
                "accountUsagePolicy: MultiplePerAccount | OncePerAccount.",
                "minOrderValue: null = khong yeu cau gia tri don toi thieu."));

        group.MapPut(UpdatePromotion, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat khuyen mai")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateExample,
                "status hop le: Active | Inactive.",
                "accountUsagePolicy: MultiplePerAccount | OncePerAccount.",
                "PromotionCode va PromotionType khong doi duoc sau khi tao."));

        group.MapDelete(DeletePromotion, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Vo hieu hoa khuyen mai")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Soft delete: dat Status = Inactive.",
                "Tra ve 204 khi thanh cong."));
    }

    private static async Task<IResult> GetPromotions(ISender sender, string? status, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetPromotionListQuery(status), ct));

    private static async Task<IResult> ValidatePromotion(
        ISender sender, string code, decimal subtotalAmount, CancellationToken ct) =>
        Results.Ok(await sender.Send(new ValidatePromotionQuery(code, subtotalAmount), ct));

    private static async Task<IResult> CreatePromotion(ISender sender, CreatePromotionCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdatePromotion(ISender sender, Guid id, UpdatePromotionRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdatePromotionCommand(
            id, req.PromotionName, req.DiscountValue, req.MinOrderValue,
            req.ValidFrom, req.ValidTo, req.UsageLimit, req.AccountUsagePolicy, req.Status), ct));

    private static async Task<IResult> DeletePromotion(ISender sender, Guid id, CancellationToken ct)
    {
        await sender.Send(new DeletePromotionCommand(id), ct);
        return Results.NoContent();
    }

    public sealed record UpdatePromotionRequest(
        string PromotionName,
        decimal DiscountValue,
        decimal? MinOrderValue,
        DateTimeOffset ValidFrom,
        DateTimeOffset ValidTo,
        int? UsageLimit,
        SaigonWaterbus.Domain.Enums.PromotionAccountUsagePolicy AccountUsagePolicy,
        string Status);
}
