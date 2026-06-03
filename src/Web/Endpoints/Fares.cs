using SaigonWaterbus.Application.Fares;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Fares : IEndpointGroup
{
    public static string RoutePrefix => "/api/fares";

    private const string CreateFareExample =
        """
        {
          "routeId": "8a5a73e6-729d-410d-83b3-0ce2fe2eaa52",
          "fromStationId": "86015ba6-adac-4fd3-9e5f-30eeb9b2cefe",
          "toStationId": "95916ab1-a4b8-445b-b6fb-e2c8e609dc1c",
          "basePrice": 20000
        }
        """;

    private const string UpdateFareExample =
        """
        {
          "basePrice": 22000,
          "isActive": true
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetFare, string.Empty)
            .AllowAnonymous()
            .WithSummary("Tra gia ve theo cap tram + tuyen")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Query params bat buoc: routeId, fromStationId, toStationId.",
                "Vi du: GET /api/fares?routeId=8a5a73e6-...&fromStationId=86015ba6-...&toStationId=95916ab1-...",
                "Tra ve list gia theo tung loai ve (Adult, Student, Child, Senior).",
                "finalPrice = basePrice x priceModifier.",
                "Tra ve 404 neu chua co fare cho cap tram nay."));

        group.MapGet(GetFareMatrix, "matrix")
            .RequireAuthorization()
            .WithSummary("Danh sach bang gia (admin)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Query param routeId (optional) de loc theo tuyen.",
                "Vi du: GET /api/fares/matrix?routeId=8a5a73e6-...",
                "Tra ve tat ca FareMatrix entries kem ten tram."));

        group.MapPost(CreateFare, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao muc gia cho cap tram")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                CreateFareExample,
                "Moi cap (routeId + fromStationId + toStationId) chi co 1 fare active.",
                "fromStationId phai khac toStationId.",
                "basePrice > 0 (don vi: VND).",
                "Gia cho chieu nguoc lai phai tao rieng."));

        group.MapPut(UpdateFare, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat muc gia")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                UpdateFareExample,
                "isActive = false de vo hieu hoa muc gia (khong xoa).",
                "Khi isActive = false, GetFare se khong tim thay cap tram nay."));
    }

    private static async Task<IResult> GetFare(
        ISender sender,
        Guid routeId, Guid fromStationId, Guid toStationId,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetFareQuery(routeId, fromStationId, toStationId), ct));

    private static async Task<IResult> GetFareMatrix(ISender sender, Guid? routeId, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetFareMatrixListQuery(routeId), ct));

    private static async Task<IResult> CreateFare(ISender sender, CreateFareCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateFare(ISender sender, Guid id, UpdateFareRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateFareCommand(id, req.BasePrice, req.IsActive), ct));

    public sealed record UpdateFareRequest(decimal BasePrice, bool IsActive);
}
