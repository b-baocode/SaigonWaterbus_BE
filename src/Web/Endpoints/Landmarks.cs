using SaigonWaterbus.Application.Landmarks;

namespace SaigonWaterbus.Web.Endpoints;

/// <summary>CRUD landmark + ghi nhận audio pre-bake (admin).</summary>
public sealed class Landmarks : IEndpointGroup
{
    public static string RoutePrefix => "/api/landmarks";

    private const string CreateExample =
        """
        {
          "landmarkName": "Cau Ba Son",
          "latitude": 10.7876,
          "longitude": 106.7060,
          "description": "Cay cau day vang dau tien bac qua song Sai Gon...",
          "displayOrder": 1,
          "triggerRadiusMeters": 300,
          "isActive": true
        }
        """;

    private const string SetAudioExample =
        """
        {
          "voiceId": "00000000-0000-0000-0000-000000000000",
          "audioUrl": "https://res.cloudinary.com/.../ba_son_nam.mp3",
          "durationSeconds": 42.5
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetLandmarks, string.Empty)
            .AllowAnonymous()
            .WithSummary("Diem thuyet minh (khach)")
            .WithDescription(OpenApiDescriptionBuilder.Build("Anonymous", null,
                "Tra ve TAT CA landmark active kem audio cua giong duoc chon; app tai 1 lan roi phat theo vi tri thuc.",
                "voiceId (optional): chon giong. Bo trong thi dung giong mac dinh.",
                "Khong gan tuyen — landmark kich hoat theo khoang cach GPS, dung chung moi tuyen."));

        group.MapGet(GetLandmarksAdmin, "admin")
            .RequireAuthorization()
            .WithSummary("Danh sach landmark (admin)")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", null,
                "Tra ve tat ca landmark (ke ca inactive) kem moi audio da bake theo tung giong."));

        group.MapPost(CreateLandmark, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao landmark")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", CreateExample,
                "Landmark khong gan tuyen — dinh danh bang latitude/longitude, dung chung moi tuyen di ngang qua.",
                "displayOrder chi la thu tu hien thi o man admin; triggerRadiusMeters (mac dinh 300) la ban kinh kich hoat phat.",
                "Chi tao phan text + toa do. Muon co tieng phai bake TTS roi goi PUT {id}/audios cho tung giong."));

        group.MapPut(UpdateLandmark, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat landmark")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", CreateExample,
                "Full replace — gui lai toan bo thong tin landmark, field bo trong se bi ghi de.",
                "Sua description KHONG tu re-bake audio; phai chay lai bake va PUT {id}/audios."));

        group.MapDelete(DeleteLandmark, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Xoa landmark")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", null,
                "Xoa landmark keo theo xoa moi audio cua no (cascade). Tra ve 204."));

        group.MapPut(SetLandmarkAudio, "{id:guid}/audios")
            .RequireAuthorization()
            .WithSummary("Ghi nhan audio pre-bake cho landmark")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", SetAudioExample,
                "Upsert audio cho (landmark x giong): co roi thi cap nhat URL, chua co thi tao.",
                "audioUrl la link mp3 da bake bang VieNeu-TTS va up Cloudinary.",
                "Tra ve nguyen landmark kem audios[] (giong GET admin) de FE ve lai danh sach ngay."));

        group.MapDelete(DeleteLandmarkAudio, "audios/{audioId:guid}")
            .RequireAuthorization()
            .WithSummary("Xoa audio cua landmark")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", null, "Tra ve 204."));
    }

    private static async Task<IResult> GetLandmarks(ISender sender, Guid? voiceId, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetLandmarksQuery(voiceId), ct));

    private static async Task<IResult> GetLandmarksAdmin(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetLandmarksAdminQuery(), ct));

    private static async Task<IResult> CreateLandmark(
        ISender sender, CreateLandmarkCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateLandmark(
        ISender sender, Guid id, UpdateLandmarkRequest body, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateLandmarkCommand(
            id, body.LandmarkName, body.Latitude, body.Longitude, body.Description,
            body.DisplayOrder, body.TriggerRadiusMeters, body.IsActive), ct));

    private static async Task<IResult> DeleteLandmark(ISender sender, Guid id, CancellationToken ct)
    {
        await sender.Send(new DeleteLandmarkCommand(id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetLandmarkAudio(
        ISender sender, Guid id, SetLandmarkAudioRequest body, CancellationToken ct) =>
        Results.Ok(await sender.Send(new SetLandmarkAudioCommand(
            id, body.VoiceId, body.AudioUrl, body.DurationSeconds), ct));

    private static async Task<IResult> DeleteLandmarkAudio(ISender sender, Guid audioId, CancellationToken ct)
    {
        await sender.Send(new DeleteLandmarkAudioCommand(audioId), ct);
        return Results.NoContent();
    }

    public sealed record UpdateLandmarkRequest(
        string LandmarkName,
        decimal Latitude,
        decimal Longitude,
        string? Description,
        int DisplayOrder,
        int TriggerRadiusMeters,
        bool IsActive);

    public sealed record SetLandmarkAudioRequest(
        Guid VoiceId,
        string AudioUrl,
        decimal? DurationSeconds);
}
