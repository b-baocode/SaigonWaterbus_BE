using SaigonWaterbus.Application.Maintenance;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class DataCleanup : IEndpointGroup
{
    public static string RoutePrefix => "/api/data-cleanup";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost(CleanupImportedData, string.Empty)
            .RequireAuthorization()
            .WithSummary("Don du lieu rac tu import (station/song khong ten)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Xoa station khong ten (StationName rong hoac 'Unnamed ferry terminal') va KHONG bi tham chieu boi route/booking/landmark/phan cong/GPS session.",
                "Xoa TAT CA waterway segment khong co ten (WaterwayName rong).",
                "Yeu cau query param confirm=true de tranh xoa nham: POST /api/data-cleanup?confirm=true.",
                "Tra ve { deletedStations, skippedStations, deletedWaterwaySegments }.",
                "skippedStations = station khong ten nhung dang duoc dung -> giu lai de tranh loi rang buoc."));

        group.MapPost(DeleteStationsFarFromRiver, "stations-far-from-river")
            .RequireAuthorization()
            .WithSummary("Xoa station khong nam gan duong song chi dinh")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Do khoang cach tu station (latitude/longitude) toi hinh hoc duong song, xoa station xa hon maxDistanceMeters.",
                "waterwayName: ten duong song, mac dinh 'Sông Sài Gòn' (lay tu GET /api/waterways).",
                "maxDistanceMeters: nguong gan, mac dinh 500.",
                "preview=true (MAC DINH): chi liet ke station se bi xoa kem khoang cach thuc, KHONG xoa. Dung de do nguong truoc.",
                "Muon xoa that: preview=false&confirm=true.",
                "Station dang duoc dung (route/booking/landmark/phan cong/GPS) se duoc GIU LAI -> skippedReferencedStations.",
                "Station khong co toa do khong do duoc nen cung duoc giu lai -> stationsWithoutCoordinates.",
                "Tra ve { nearStations, farStations, deletedStations, skippedReferencedStations, stationsWithoutCoordinates, farStationDetails[] }."));
    }

    private static async Task<IResult> CleanupImportedData(
        ISender sender, bool confirm = false, CancellationToken ct = default)
    {
        if (!confirm)
        {
            return Results.BadRequest(new
            {
                message = "Them ?confirm=true de xac nhan don du lieu rac."
            });
        }

        return Results.Ok(await sender.Send(new CleanupImportedDataCommand(), ct));
    }

    private static async Task<IResult> DeleteStationsFarFromRiver(
        ISender sender,
        string? waterwayName,
        double? maxDistanceMeters,
        bool preview = true,
        bool confirm = false,
        CancellationToken ct = default)
    {
        if (!preview && !confirm)
        {
            return Results.BadRequest(new
            {
                message = "Xoa that can preview=false&confirm=true. Bo trong de chay preview (khong xoa)."
            });
        }

        return Results.Ok(await sender.Send(
            new DeleteStationsFarFromWaterwayCommand(
                string.IsNullOrWhiteSpace(waterwayName) ? "Sông Sài Gòn" : waterwayName,
                maxDistanceMeters ?? 500,
                preview),
            ct));
    }
}
