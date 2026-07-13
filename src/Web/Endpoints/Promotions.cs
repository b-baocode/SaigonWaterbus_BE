using SaigonWaterbus.Application.Promotions;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Promotions : IEndpointGroup
{
    public static string RoutePrefix => "/api/promotions";

    private const string CreateExample =
        """
        {
          "promotionCode": "WELCOME10",
          "promotionName": "Chao mung khach moi",
          "description": "Giam 10% cho khach dat ve lan dau, toi da 30.000d.",
          "promotionType": "Percent",
          "discountValue": 10,
          "maxDiscountAmount": 30000,
          "minOrderValue": 20000,
          "validFrom": "2026-01-01T00:00:00+07:00",
          "validTo": "2026-12-31T23:59:59+07:00",
          "usageLimit": 1000,
          "maxUsesPerAccount": 1,
          "budgetCap": null,
          "firstBookingOnly": true,
          "scope": {
            "bookingTypes": ["SeatBooking"],
            "routeIds": null,
            "daysOfWeek": null,
            "departureFrom": null,
            "departureTo": null
          },
          "visibility": "Public",
          "status": "Active",
          "imageUrl": null
        }
        """;

    private const string UpdateExample =
        """
        {
          "promotionName": "Chao mung khach moi (mo rong)",
          "description": "Giam 15%, toi da 50.000d.",
          "discountValue": 15,
          "maxDiscountAmount": 50000,
          "minOrderValue": 30000,
          "validFrom": "2026-01-01T00:00:00+07:00",
          "validTo": "2026-12-31T23:59:59+07:00",
          "usageLimit": 2000,
          "maxUsesPerAccount": 2,
          "budgetCap": 5000000,
          "firstBookingOnly": false,
          "scope": null,
          "visibility": "Public",
          "status": "Active"
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetPublicPromotions, "public")
            .AllowAnonymous()
            .WithSummary("Danh sach khuyen mai cong khai (khach)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Chi tra ve ma Active + Public, con trong han va chua het luot/ngan sach.",
                "Khong lo luot da dung, ngan sach hay ma Private."));

        group.MapGet(GetPromotions, string.Empty)
            .RequireAuthorization()
            .WithSummary("Danh sach khuyen mai (admin)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Query param status (optional): Draft | Active | Paused | Archived. Bo trong de lay tat ca.",
                "Moi item co effectiveState (Draft/Scheduled/Running/Paused/Exhausted/Ended/Archived), totalUsed, budgetSpent.",
                "Sap xep moi nhat truoc."));

        group.MapGet(ValidatePromotion, "validate")
            .AllowAnonymous()
            .WithSummary("Kiem tra ma khuyen mai")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Query params: code (string), subtotalAmount (decimal).",
                "Vi du: GET /api/promotions/validate?code=WELCOME10&subtotalAmount=40000",
                "discountAmount da bao gom tran giam (maxDiscountAmount) va lam tron VND.",
                "isValid=false neu: chua/het han, het luot, het ngan sach, chua du don toi thieu, sai pham vi, khong con luot cho tai khoan."));

        group.MapPost(CreatePromotion, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao khuyen mai moi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                CreateExample,
                "promotionCode phai unique (tu dong uppercase).",
                "promotionType: Percent (giam %) | Fixed (giam so tien co dinh, gui 'FixedAmount' cung duoc).",
                "maxDiscountAmount: tran so tien giam, chi ap dung cho Percent.",
                "usageLimit: tong luot; maxUsesPerAccount: luot moi tai khoan; budgetCap: tran tong tien giam.",
                "firstBookingOnly: chi cho lan dat dau tien.",
                "scope (optional): gioi han theo bookingTypes | routeIds | daysOfWeek | khung gio khoi hanh. Null = ap moi noi.",
                "visibility: Public (hien trang KM) | Private (ma bi mat, van dung duoc neu biet ma).",
                "status: Draft | Active | Paused (khong tao voi Archived).",
                "Anh: gui imageUrl (absolute URL) o day, hoac upload file qua PUT {id}/image."))
            .Accepts<CreatePromotionRequest>("application/json");

        group.MapPut(UpdatePromotion, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat khuyen mai")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateExample,
                "PromotionCode va PromotionType khong doi duoc sau khi tao.",
                "Khong the dat usageLimit nho hon so luot da dung, hoac budgetCap nho hon ngan sach da tieu.",
                "status: Draft | Active | Paused | Archived. visibility: Public | Private."))
            .Accepts<UpdatePromotionRequest>("application/json");

        group.MapPut(UpdatePromotionImage, "{id:guid}/image")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<PromotionImageFormRequest>("multipart/form-data")
            .WithSummary("Cap nhat anh khuyen mai")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Gui multipart/form-data voi field image de upload anh (giong boat/station/blog).",
                "Hoac gui application/json voi imageUrl (absolute URL). imageUrl rong/khong gui = xoa anh."));

        group.MapDelete(DeletePromotion, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Vo hieu hoa khuyen mai")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Soft delete: dat Status = Archived (khong bat lai duoc).",
                "Tra ve 204 khi thanh cong."));
    }

    private static async Task<IResult> GetPublicPromotions(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetPublicPromotionListQuery(), ct));

    private static async Task<IResult> GetPromotions(ISender sender, PromotionStatus? status, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetPromotionListQuery(status), ct));

    private static async Task<IResult> ValidatePromotion(
        ISender sender, string code, decimal subtotalAmount, CancellationToken ct) =>
        Results.Ok(await sender.Send(new ValidatePromotionQuery(code, subtotalAmount), ct));

    private static async Task<IResult> CreatePromotion(ISender sender, CreatePromotionRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new CreatePromotionCommand(
            req.PromotionCode,
            req.PromotionName,
            req.PromotionType,
            req.DiscountValue,
            req.MaxDiscountAmount,
            req.MinOrderValue,
            req.ValidFrom,
            req.ValidTo,
            req.UsageLimit,
            req.MaxUsesPerAccount,
            req.BudgetCap,
            req.FirstBookingOnly,
            req.Scope,
            req.Visibility,
            req.Status,
            req.Description,
            req.ImageUrl), ct));

    private static async Task<IResult> UpdatePromotion(
        ISender sender, Guid id, UpdatePromotionRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdatePromotionCommand(
            id,
            req.PromotionName,
            req.DiscountValue,
            req.MaxDiscountAmount,
            req.MinOrderValue,
            req.ValidFrom,
            req.ValidTo,
            req.UsageLimit,
            req.MaxUsesPerAccount,
            req.BudgetCap,
            req.FirstBookingOnly,
            req.Scope,
            req.Visibility,
            req.Status,
            req.Description), ct));

    private static async Task<IResult> UpdatePromotionImage(
        ISender sender, Guid id, HttpRequest request, CancellationToken ct)
    {
        UpdatePromotionImageCommand command;
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault(f =>
                string.Equals(f.Name, "image", StringComparison.OrdinalIgnoreCase)
                || string.Equals(f.Name, "file", StringComparison.OrdinalIgnoreCase));

            PromotionImageFileRequest? imageFile = null;
            if (file is not null)
            {
                var content = new MemoryStream();
                await file.CopyToAsync(content, ct);
                content.Position = 0;
                imageFile = new PromotionImageFileRequest(file.FileName, file.ContentType, file.Length, content);
            }

            var imageUrl = form["imageUrl"].ToString();
            command = new UpdatePromotionImageCommand(
                id, string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl, imageFile);
        }
        else
        {
            var body = await request.ReadFromJsonAsync<UpdatePromotionImageRequest>(cancellationToken: ct);
            command = new UpdatePromotionImageCommand(id, body?.ImageUrl);
        }

        try
        {
            return Results.Ok(await sender.Send(command, ct));
        }
        finally
        {
            command.ImageFile?.Content.Dispose();
        }
    }

    private static async Task<IResult> DeletePromotion(ISender sender, Guid id, CancellationToken ct)
    {
        await sender.Send(new DeletePromotionCommand(id), ct);
        return Results.NoContent();
    }

    public sealed record CreatePromotionRequest(
        string PromotionCode,
        string PromotionName,
        PromotionType PromotionType,
        decimal DiscountValue,
        decimal? MaxDiscountAmount,
        decimal? MinOrderValue,
        DateTimeOffset ValidFrom,
        DateTimeOffset ValidTo,
        int? UsageLimit,
        int? MaxUsesPerAccount,
        decimal? BudgetCap,
        bool FirstBookingOnly = false,
        PromotionScopeDto? Scope = null,
        PromotionVisibility Visibility = PromotionVisibility.Public,
        PromotionStatus Status = PromotionStatus.Draft,
        string? Description = null,
        string? ImageUrl = null);

    public sealed record UpdatePromotionRequest(
        string PromotionName,
        decimal DiscountValue,
        decimal? MaxDiscountAmount,
        decimal? MinOrderValue,
        DateTimeOffset ValidFrom,
        DateTimeOffset ValidTo,
        int? UsageLimit,
        int? MaxUsesPerAccount,
        decimal? BudgetCap,
        bool FirstBookingOnly,
        PromotionScopeDto? Scope,
        PromotionVisibility Visibility,
        PromotionStatus Status,
        string? Description = null);

    public sealed record UpdatePromotionImageRequest(string? ImageUrl);

    public sealed record PromotionImageFormRequest(IFormFile Image);
}
