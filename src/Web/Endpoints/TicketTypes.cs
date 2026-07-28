using SaigonWaterbus.Application.TicketTypes;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class TicketTypes : IEndpointGroup
{
    public static string RoutePrefix => "/api/ticket-types";

    private const string UpdateFareRuleExample =
        """
        {
          "ticketTypeCode": "SENIOR",
          "routeType": "SightseeingLoop",
          "priceModifier": 0.5,
          "isActive": true
        }
        """;

    private const string UpdateSightseeingConcessionExample =
        """
        {
          "discountPercent": 50
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
                "Regular co dinh mien phi cho CHILD/SENIOR/DISABLED/INFANT.",
                "Sightseeing: CHILD va INFANT mien phi co dinh; SENIOR/DISABLED dung chung % giam tai /api/ticket-types/sightseeing-concession.",
                "Ticket type thuc te cua ve da phat hanh duoc luu truc tiep trong bang tickets."));

        group.MapGet(GetSightseeingConcessionFareRule, "sightseeing-concession")
            .RequireAuthorization()
            .WithSummary("Admin xem % giam chung cho nhom uu dai sightseeing")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                null,
                "Mot setting duy nhat cho SENIOR va DISABLED tren routeType=SightseeingLoop.",
                "discountPercent=50 nghia la gia ve = gia ghe x 0.5."));

        group.MapPut(UpdateSightseeingConcessionFareRule, "sightseeing-concession")
            .RequireAuthorization()
            .WithSummary("Admin chinh % giam chung cho nhom uu dai sightseeing")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                UpdateSightseeingConcessionExample,
                "Ap dung cho SENIOR va DISABLED tren routeType=SightseeingLoop.",
                "discountPercent=0 la khong giam; 50 la giam mot nua; 100 la mien phi.",
                "Ap dung cho booking tao sau khi chinh; booking da tao/da thanh toan khong tinh lai."));

        group.MapGet(GetTicketFareRules, "fare-rules")
            .RequireAuthorization()
            .WithSummary("Admin xem cau hinh he so gia theo loai ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                null,
                "Tra ve ma tran ticketTypeCode x routeType. Dong ticketFareRuleId=null la gia default chua override.",
                "routeType: Regular hoac SightseeingLoop. priceModifier: 1 = nguyen gia, 0.5 = giam 50%, 0 = mien phi.",
                "Regular cua CHILD/SENIOR/DISABLED/INFANT la mien phi co dinh, khong can tao setting rieng.",
                "Voi SENIOR/DISABLED tren SightseeingLoop, nen dung endpoint /api/ticket-types/sightseeing-concession de chinh chung mot lan.",
                "CHILD va INFANT la mien phi co dinh ca Regular lan SightseeingLoop."));

        group.MapPut(UpdateTicketFareRule, "fare-rules")
            .RequireAuthorization()
            .WithSummary("Admin chinh he so gia theo loai ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                UpdateFareRuleExample,
                "Ap dung cho booking tao sau khi chinh; booking da tao/da thanh toan khong tinh lai.",
                "ticketTypeCode: ADULT | CHILD | INFANT | SENIOR | DISABLED.",
                "routeType: Regular | SightseeingLoop.",
                "Neu gui CHILD/SENIOR/DISABLED/INFANT voi routeType=Regular, hoac CHILD/INFANT voi SightseeingLoop, BE se tra ve he so 0 va xoa override cu neu co.",
                "priceModifier: 1 = nguyen gia, 0.5 = giam 50%, 0 = mien phi. Neu can phu thu ngay le/cuoi tuan thi dung /api/fare-policy/adjustments."));
    }

    private static async Task<IResult> GetTicketTypes(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTicketTypeListQuery(), ct));

    private static async Task<IResult> GetSightseeingConcessionFareRule(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetSightseeingConcessionFareRuleQuery(), ct));

    private static async Task<IResult> UpdateSightseeingConcessionFareRule(
        ISender sender,
        UpdateSightseeingConcessionFareRuleCommand command,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> GetTicketFareRules(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTicketFareRuleListQuery(), ct));

    private static async Task<IResult> UpdateTicketFareRule(
        ISender sender,
        UpdateTicketFareRuleCommand command,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));
}
