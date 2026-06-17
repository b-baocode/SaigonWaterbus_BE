using SaigonWaterbus.Application.Fares;
using SaigonWaterbus.Application.Vessels;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Fares : IEndpointGroup
{
    public static string RoutePrefix => "/api/fares";

    private const string CreateFareExample =
        """
        {
          "routeCode": "R01-BD-LD",
          "fromStationCode": "BD",
          "toStationCode": "LD",
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

    private const string UpdateVesselRentalFareExample =
        """
        {
          "rentalUnit": "Day",
          "unitPrice": 15000000,
          "currency": "VND",
          "note": "Gia thue tau tham khao theo ngay."
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
                "Query params bat buoc: routeCode, fromStationCode, toStationCode.",
                "Vi du: GET /api/fares?routeCode=R01-BD-LD&fromStationCode=BD&toStationCode=LD",
                "Tra ve list gia theo tung loai ve (ADULT, STUDENT, ...).",
                "finalPrice = basePrice x priceModifier.",
                "Tra ve 404 neu chua co fare cho cap tram nay."));

        group.MapGet(GetFareMatrix, "matrix")
            .RequireAuthorization()
            .WithSummary("Danh sach bang gia (admin)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Query param routeCode (optional) de loc theo tuyen.",
                "Vi du: GET /api/fares/matrix?routeCode=R01-BD-LD",
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

        group.MapPut(UpdateVesselRentalFare, "vessel-rental-prices/{vesselId:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat gia thue tau theo gio hoac theo ngay")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateVesselRentalFareExample,
                "Neu tau chua co gia thi tao moi, neu da co thi cap nhat.",
                "rentalUnit: Hour hoac Day. Neu khong gui thi mac dinh Day.",
                "API nay cap nhat bang vessel_rental_prices, khong dung fare_matrices."));

        group.MapDelete(DeleteFare, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Vo hieu hoa muc gia")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Soft delete: dat isActive = false.",
                "Tra ve 204 khi thanh cong."));
    }

    private static async Task<IResult> GetFare(
        ISender sender,
        string routeCode, string fromStationCode, string toStationCode,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetFareQuery(routeCode, fromStationCode, toStationCode), ct));

    private static async Task<IResult> GetFareMatrix(ISender sender, string? routeCode, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetFareMatrixListQuery(routeCode), ct));

    private static async Task<IResult> CreateFare(ISender sender, CreateFareCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateFare(ISender sender, Guid id, UpdateFareRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateFareCommand(id, req.BasePrice, req.IsActive), ct));

    private static async Task<IResult> UpdateVesselRentalFare(
        IVesselManagementService vesselManagementService,
        Guid vesselId,
        UpdateVesselRentalFareRequest req,
        CancellationToken ct) =>
        Results.Ok(await vesselManagementService.UpdateVesselRentalPriceAsync(
            new UpdateVesselRentalPriceRequest(vesselId, req.UnitPrice, req.Currency, req.Note, req.RentalUnit),
            ct));

    private static async Task<IResult> DeleteFare(ISender sender, Guid id, CancellationToken ct)
    {
        await sender.Send(new DeleteFareCommand(id), ct);
        return Results.NoContent();
    }

    public sealed record UpdateFareRequest(decimal BasePrice, bool IsActive);

    public sealed record UpdateVesselRentalFareRequest(
        decimal UnitPrice,
        string? Currency = null,
        string? Note = null,
        VesselRentalUnit RentalUnit = VesselRentalUnit.Day);
}
