using SaigonWaterbus.Application.Seats;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class SeatTypes : IEndpointGroup
{
    public static string RoutePrefix => "/api/seat-types";

    private const string UpdatePriceExample =
        """
        {
          "basePrice": 20000
        }
        """;

    private const string CreateExample =
        """
        {
          "code": "VIP",
          "name": "Ghe VIP",
          "basePrice": 50000,
          "displayOrder": 5
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetSeatTypes, string.Empty)
            .AllowAnonymous()
            .WithSummary("Danh sach loai ghe va gia goc")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Tra ve 4 loai ghe: STANDARD (bus thuong), CABIN / RIVER / SKY (bus sightseeing) kem gia goc.",
                "Gia goc doc tu bang seat_types (duoc seed tu dong khi khoi dong); admin chinh bang PUT /api/seat-types/{code}.",
                "Gia ve thuc te cua tung chuyen nam trong trip_seats.price (chot khi tao trip)."));

        group.MapPost(CreateSeatType, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao loai ghe moi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                CreateExample,
                "Tao loai ghe tuy chinh (vd VIP) kem gia goc, dung cho tau sightseeing (StandardAndVip).",
                "Sau khi tao, chon loai ghe nay o buoc setup ghe: POST /api/boats/{boatId}/seats/configure voi cells[].seatTypeCode.",
                "Tau FullStandard van chi dung ghe STANDARD.",
                "code: chu + so + gach duoi, tu dong uppercase. Khong duoc trung code da co."));

        group.MapPut(UpdateSeatTypePrice, "{code}")
            .RequireAuthorization()
            .WithSummary("Chinh gia goc loai ghe")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                UpdatePriceExample,
                "code: STANDARD | CABIN | RIVER | SKY.",
                "Gia goc moi chi ap dung cho cac trip TAO SAU thoi diem chinh; trip da tao giu nguyen gia da chot trong trip_seats.",
                "basePrice: so nguyen VND, > 0."));
    }

    private static async Task<IResult> GetSeatTypes(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetSeatTypeListQuery(), ct));

    private static async Task<IResult> CreateSeatType(
        ISender sender, CreateSeatTypeCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateSeatTypePrice(
        ISender sender, string code, UpdateSeatTypePriceRequest request, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateSeatTypePriceCommand(code, request.BasePrice), ct));

    public sealed record UpdateSeatTypePriceRequest(decimal BasePrice);
}
