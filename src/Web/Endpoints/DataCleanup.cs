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
}
