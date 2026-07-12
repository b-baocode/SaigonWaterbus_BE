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

        group.MapDelete(DeleteWaterway, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Xoa mot duong song/kenh")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Xoa TAT CA segment cua duong song nay (cung OsmId + ten + loai).",
                "Id lay tu GET /api/waterways. Dung de don du lieu ve tay/du thua gay route geometry sai.",
                "Tra ve { osmId, waterwayName, waterwayType, deletedSegments }.",
                "Route da tao KHONG bi anh huong (geometry da luu rieng trong routes); chi anh huong route tao MOI.",
                "Tra ve 404 neu khong tim thay."));

        group.MapDelete(DeleteAllWaterways, string.Empty)
            .RequireAuthorization()
            .WithSummary("Xoa TOAN BO mang duong song")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Xoa sach bang waterway_segments - dung truoc khi re-import GeoJSON de tranh du lieu cu tron voi map moi.",
                "Yeu cau query param confirm=true de tranh xoa nham: DELETE /api/waterways?confirm=true.",
                "Tra ve { deletedSegments }.",
                "Route da tao khong bi anh huong; nho import lai GeoJSON truoc khi tao route moi."));

        group.MapDelete(DeleteWaterwaysByType, "by-type/{type}")
            .RequireAuthorization()
            .WithSummary("Xoa toan bo duong song theo loai (vd canal)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Xoa TAT CA segment co type = {type} (river | canal | custom). Vd xoa het kenh: DELETE /api/waterways/by-type/canal?confirm=true.",
                "Yeu cau query param confirm=true de tranh xoa nham.",
                "type khong hop le tra ve 400.",
                "Tra ve { waterwayType, deletedSegments }.",
                "Route da tao KHONG bi anh huong; chi anh huong route tao MOI."));
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

    private static async Task<IResult> DeleteWaterway(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new DeleteWaterwayCommand(id), ct));

    private static async Task<IResult> DeleteAllWaterways(ISender sender, bool confirm = false, CancellationToken ct = default)
    {
        if (!confirm)
        {
            return Results.BadRequest(new
            {
                message = "Them ?confirm=true de xac nhan xoa TOAN BO mang duong song."
            });
        }

        return Results.Ok(await sender.Send(new DeleteAllWaterwaysCommand(), ct));
    }

    private static async Task<IResult> DeleteWaterwaysByType(
        ISender sender, string type, bool confirm = false, CancellationToken ct = default)
    {
        if (!confirm)
        {
            return Results.BadRequest(new
            {
                message = $"Them ?confirm=true de xac nhan xoa TAT CA duong song loai '{type}'."
            });
        }

        return Results.Ok(await sender.Send(new DeleteWaterwaysByTypeCommand(type), ct));
    }
}
