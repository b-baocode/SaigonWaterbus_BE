using SaigonWaterbus.Application.Landmarks;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Voices : IEndpointGroup
{
    public static string RoutePrefix => "/api/voices";

    private const string CreateExample =
        """
        {
          "name": "Giong Nam - ke chuyen",
          "vieneuVoiceId": "Pham Tuyen",
          "refAudioUrl": null,
          "region": "Nam",
          "gender": "Nam",
          "style": "ke chuyen",
          "sampleUrl": "https://res.cloudinary.com/.../sample_nam.mp3",
          "isActive": true,
          "displayOrder": 1
        }
        """;

    private const string CloneExample =
        """
        {
          "name": "Giong HDV rieng",
          "refAudioUrl": "https://res.cloudinary.com/.../voice_samples/hdv.wav",
          "region": "Nam",
          "gender": "Nam",
          "isActive": true,
          "displayOrder": 2
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetVoices, string.Empty)
            .AllowAnonymous()
            .WithSummary("Danh sach giong doc (khach)")
            .WithDescription(OpenApiDescriptionBuilder.Build("Anonymous", null,
                "Tra ve cac giong active de khach chon nghe thuyet minh, sap xep theo displayOrder.",
                "voiceId dung lam tham so cho GET /api/landmarks?voiceId=..."));

        group.MapGet(GetVoicesAdmin, "admin")
            .RequireAuthorization()
            .WithSummary("Danh sach giong doc (admin)")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", null,
                "Tra ve tat ca giong (ke ca inactive) kem vieneuVoiceId."));

        group.MapPost(CreateVoice, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao giong doc")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", CreateExample,
                "Giong PRESET: dat vieneuVoiceId = ten 1 trong 14 giong VieNeu (co dau, vd 'Pham Tuyen').",
                "Giong CLONE: dat refAudioUrl = clip audio 3-8s (dung upload_voice_sample.py de lay URL). " + CloneExample,
                "Phai co it nhat mot trong hai (vieneuVoiceId hoac refAudioUrl)."));

        group.MapPut(UpdateVoice, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat giong doc")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", CreateExample,
                "Gui lai toan bo thong tin giong."));

        group.MapDelete(DeleteVoice, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Xoa giong doc")
            .WithDescription(OpenApiDescriptionBuilder.Build("Admin", null,
                "Chan xoa neu giong con audio landmark. Dat isActive=false de an thay vi xoa. Tra ve 204."));
    }

    private static async Task<IResult> GetVoices(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetVoicesQuery(), ct));

    private static async Task<IResult> GetVoicesAdmin(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetVoicesAdminQuery(), ct));

    private static async Task<IResult> CreateVoice(
        ISender sender, CreateVoiceCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateVoice(
        ISender sender, Guid id, UpdateVoiceRequest body, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateVoiceCommand(
            id, body.Name, body.VieneuVoiceId, body.RefAudioUrl, body.Region, body.Gender, body.Style,
            body.SampleUrl, body.IsActive, body.DisplayOrder), ct));

    private static async Task<IResult> DeleteVoice(ISender sender, Guid id, CancellationToken ct)
    {
        await sender.Send(new DeleteVoiceCommand(id), ct);
        return Results.NoContent();
    }

    public sealed record UpdateVoiceRequest(
        string Name,
        string? VieneuVoiceId,
        string? RefAudioUrl,
        string? Region,
        string? Gender,
        string? Style,
        string? SampleUrl,
        bool IsActive,
        int DisplayOrder);
}
