using SaigonWaterbus.Application.Fares;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Fares : IEndpointGroup
{
    public static string RoutePrefix => "/api/fares";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetFare, string.Empty)
            .AllowAnonymous()
            .WithSummary("Tra gia ve theo cap tram + tuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Query params bat buoc: boatId, seatNumber.",
                "Vi du: GET /api/fares?boatId=...&seatNumber=1-A1",
                "Tra ve list gia theo tung loai ve (ADULT, STUDENT, ...).",
                "finalPrice = basePrice x priceModifier.",
                "basePrice lay tu seatTypeCode cua ghe.",
                "Tra ve 404 neu ghe khong ton tai, khong active hoac chua co gia hop le."));
    }

    private static async Task<IResult> GetFare(
        ISender sender,
        Guid boatId, string seatNumber,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetFareQuery(boatId, seatNumber), ct));
}
