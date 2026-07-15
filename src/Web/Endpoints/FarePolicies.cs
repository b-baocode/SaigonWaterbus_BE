using SaigonWaterbus.Application.Fares;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class FarePolicies : IEndpointGroup
{
    public static string RoutePrefix => "/api/fare-policy";

    private const string UpdateExample =
        """
        {
          "baseFare": 5000,
          "pricePerKm": 1500,
          "roundingStep": 1000,
          "minFare": null
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetFarePolicy, string.Empty)
            .AllowAnonymous()
            .WithSummary("Cong thuc gia ve theo quang duong")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Gia ve trip Regular (ghe STANDARD) = RoundUp(baseFare + pricePerKm x km, roundingStep), toi thieu minFare neu co.",
                "Km cua chang = tong distance_from_previous_km cua cac route stops giua tram len va tram xuong.",
                "Ve mien phi (INFANT/SENIOR/DISABLED) van ap dung he so 0 nhu cu."));

        group.MapPut(UpdateFarePolicy, string.Empty)
            .RequireAuthorization()
            .WithSummary("Chinh cong thuc gia ve theo quang duong")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                UpdateExample,
                "Ap dung ngay cho cac booking tao sau khi chinh (gia da chot trong booking cu khong doi).",
                "baseFare/pricePerKm/minFare: so nguyen VND. roundingStep: 1 | 100 | 500 | 1000 (lam tron LEN).",
                "Chi ap dung trip Regular voi ghe STANDARD; sightseeing (CABIN/RIVER/SKY) van tinh theo loai ghe."));
    }

    private static async Task<IResult> GetFarePolicy(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetFarePolicyQuery(), ct));

    private static async Task<IResult> UpdateFarePolicy(
        ISender sender, UpdateFarePolicyCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));
}
