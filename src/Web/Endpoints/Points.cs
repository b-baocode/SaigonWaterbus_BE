using SaigonWaterbus.Application.Common.Interfaces;
using SaigonWaterbus.Application.Points;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Points : IEndpointGroup
{
    public static string RoutePrefix => "/api/points";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetMyPoints, "me")
            .RequireAuthorization()
            .WithSummary("So du va lich su diem cua toi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "1 point = 1 VND. Tich 1% gia tri moi payment thanh toan thanh cong.",
                "Query: page (mac dinh 1), pageSize (mac dinh 20, toi da 100).",
                "transactionType: Earn (tich diem), Redeem (dung diem), RedeemCancelled (doi muc dung tai checkout), RedeemReturned (hoan diem do booking het han/huy/hoan tien), EarnRevoked (thu hoi diem do hoan tien).",
                "Diem duoc dung toi da 50% gia tri don tai buoc tao thanh toan (pointsToUse)."));
    }

    private static async Task<IResult> GetMyPoints(
        ISender sender,
        int? page,
        int? pageSize,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetMyPointsQuery(page ?? 1, pageSize ?? 20), ct));
}
