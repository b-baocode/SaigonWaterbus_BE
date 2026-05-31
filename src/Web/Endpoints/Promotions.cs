using SaigonWaterbus.Application.Promotions;
using SaigonWaterbus.Web.Infrastructure;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Promotions : IEndpointGroup
{
    public static string RoutePrefix => "/api/promotions";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(ValidatePromotion, "validate").AllowAnonymous();
        group.MapPost(CreatePromotion, string.Empty).RequireAuthorization();
        group.MapPut(UpdatePromotion, "{id:guid}").RequireAuthorization();
    }

    private static async Task<IResult> ValidatePromotion(
        ISender sender, string code, decimal subtotalAmount, CancellationToken ct) =>
        Results.Ok(await sender.Send(new ValidatePromotionQuery(code, subtotalAmount), ct));

    private static async Task<IResult> CreatePromotion(ISender sender, CreatePromotionCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdatePromotion(ISender sender, Guid id, UpdatePromotionRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdatePromotionCommand(
            id, req.PromotionName, req.DiscountValue, req.MinOrderValue,
            req.ValidFrom, req.ValidTo, req.UsageLimit, req.Status), ct));

    public sealed record UpdatePromotionRequest(
        string PromotionName,
        decimal DiscountValue,
        decimal? MinOrderValue,
        DateTimeOffset ValidFrom,
        DateTimeOffset ValidTo,
        int? UsageLimit,
        string Status);
}
