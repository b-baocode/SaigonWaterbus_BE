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
                "Tra ve loai ghe kem pricingMode de FE biet cach hien thi gia.",
                "STANDARD co pricingMode=DistanceFareForRegular: Waterbus thuong tinh theo /api/fare-policy + km tren route, khong lay basePrice STANDARD.",
                "CABIN/RIVER/SKY/custom co pricingMode=SeatTypeBasePrice: sightseeing tinh theo basePrice loai ghe + fareAdjustment neu ngay chay la cuoi tuan/le/dac biet."));

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
                "Dung cho CABIN | RIVER | SKY | loai ghe custom cua sightseeing.",
                "Khong dung cho STANDARD; gia Waterbus thuong chinh bang PUT /api/fare-policy va km tren route stops.",
                "Gia goc moi ap dung cho tat ca booking sightseeing tao sau thoi diem chinh, ke ca trip da tao nhung chua booking.",
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
