using SaigonWaterbus.Application.TicketTypes;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class TicketTypes : IEndpointGroup
{
    public static string RoutePrefix => "/api/ticket-types";

    private const string UpdateFareRuleExample =
        """
        {
          "ticketTypeCode": "CHILD",
          "routeType": "SightseeingLoop",
          "priceModifier": 0.5,
          "isActive": true
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetTicketTypes, string.Empty)
            .AllowAnonymous()
            .WithSummary("Danh sach loai ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Tra ve danh sach loai ve kem he so gia dang ap dung. Default nam trong code, admin co the override bang fare-rules.",
                "priceModifier: he so gia (1.0 = gia goc, 0.5 = giam 50%).",
                "priceModifier ap dung Regular; sightseeingPriceModifier ap dung routeType=SightseeingLoop.",
                "CHILD giam 50%; DISABLED mien phi waterbus thuong va giam 50% sightseeing.",
                "Ticket type thuc te cua ve da phat hanh duoc luu truc tiep trong bang tickets."));

        group.MapGet(GetTicketFareRules, "fare-rules")
            .RequireAuthorization()
            .WithSummary("Admin xem cau hinh he so gia theo loai ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                null,
                "Tra ve ma tran ticketTypeCode x routeType. Dong ticketFareRuleId=null la gia default chua override.",
                "routeType: Regular hoac SightseeingLoop. priceModifier: 1 = nguyen gia, 0.5 = giam 50%, 0 = mien phi."));

        group.MapPut(UpdateTicketFareRule, "fare-rules")
            .RequireAuthorization()
            .WithSummary("Admin chinh he so gia theo loai ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                UpdateFareRuleExample,
                "Ap dung cho booking tao sau khi chinh; booking da tao/da thanh toan khong tinh lai.",
                "ticketTypeCode: ADULT | CHILD | INFANT | SENIOR | DISABLED.",
                "routeType: Regular | SightseeingLoop.",
                "priceModifier: 1 = nguyen gia, 0.5 = giam 50%, 0 = mien phi. Neu can phu thu ngay le/cuoi tuan thi dung /api/fare-policy/adjustments."));
    }

    private static async Task<IResult> GetTicketTypes(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTicketTypeListQuery(), ct));

    private static async Task<IResult> GetTicketFareRules(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTicketFareRuleListQuery(), ct));

    private static async Task<IResult> UpdateTicketFareRule(
        ISender sender,
        UpdateTicketFareRuleCommand command,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));
}
