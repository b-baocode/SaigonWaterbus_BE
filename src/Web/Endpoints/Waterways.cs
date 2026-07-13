using SaigonWaterbus.Application.Routes;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Waterways : IEndpointGroup
{
    public static string RoutePrefix => "/api/waterways";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetWaterways, string.Empty)
            .AllowAnonymous()
            .WithSummary("Danh sach duong song/kenh da import")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Tra ve danh sach waterway (group theo OsmId/Ten), tong chieu dai.",
                "?name=song+sai+gon — tim theo ten (contains, khong phan biet hoa thuong).",
                "?type=river|canal|custom — loc theo loai.",
                "Dung OsmId tra ve de truyen vao viaWaterway khi tao route."));

        group.MapGet(GetWaterwayById, "{id:guid}")
            .AllowAnonymous()
            .WithSummary("Chi tiet waterway (kem toa do tung doan)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Tra ve thong tin day du cua 1 waterway: OsmId, ten, loai, tong chieu dai, tung segment voi coordinates.",
                "Id lay tu ket qua GET /api/waterways (truong id cua moi phan tu).",
                "Tra ve 404 neu khong tim thay."));
    }

    private static async Task<IResult> GetWaterways(
        ISender sender,
        string? name,
        string? type,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetWaterwayListQuery(name, type), ct));

    private static async Task<IResult> GetWaterwayById(ISender sender, Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new GetWaterwayDetailQuery(id), ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
}
