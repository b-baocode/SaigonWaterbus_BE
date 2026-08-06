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
                "1 point = 1 VND. Chi tai khoan Customer duoc tich/dung diem; Staff/Manager/Admin khong duoc dung diem cho booking cua chinh ho.",
                "Tich 1% so tien thuc tra sau khi dich vu hoan tat (trip Completed / charter Completed).",
                "So tien thuc tra = payment Paid - refundAmount; diem da tich luu trong booking.pointsEarned va khong cong lap.",
                "Query: page (mac dinh 1), pageSize (mac dinh 20, toi da 100).",
                "transactionType: Earn (tich diem), Redeem (dung diem), RedeemCancelled (doi muc dung tai checkout), RedeemReturned (hoan diem do booking het han/huy/hoan tien), EarnRevoked (thu hoi diem do hoan tien).",
                "Diem duoc dung toi da 50% gia tri don tai buoc tao thanh toan (pointsToUse), hoac tai quay khi staff da lookup va OK dung customer."));

        group.MapPost(BackfillCompletedBookingPoints, "admin/backfill-completed-bookings")
            .RequireAuthorization()
            .WithSummary("Admin cong diem bu cho booking da hoan tat")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Chay mot lan sau khi bat tinh diem de cong bu cho booking cu.",
                "Chi xu ly booking co userId, pointsEarned=0 va dich vu da hoan tat.",
                "Booking thuong: bookingStatus=Completed hoac tat ca trip lien quan da Completed.",
                "Charter: bookingStatus=Completed.",
                "Ghi point_transactions va tang users.point_balance; goi lai khong cong lap."));
    }

    private static async Task<IResult> GetMyPoints(
        ISender sender,
        int? page,
        int? pageSize,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetMyPointsQuery(page ?? 1, pageSize ?? 20), ct));

    private static async Task<IResult> BackfillCompletedBookingPoints(
        ISender sender,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new BackfillCompletedBookingPointsCommand(), ct));
}
