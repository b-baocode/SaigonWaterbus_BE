using System.Globalization;
using SaigonWaterbus.Application.Stations;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Stations : IEndpointGroup
{
    public static string RoutePrefix => "/api/stations";

    private const string CreateExample =
        """
        {
          "stationCode": "NVL",
          "stationName": "Ben Nguyen Van Linh",
          "address": "Q7, TP.HCM",
          "latitude": 10.7285,
          "longitude": 106.7006,
          "openingTime": "06:00",
          "closingTime": "22:00",
          "isWaterbusStation": true,
          "imageUrls": [
            "https://cdn.example.com/stations/nvl-main.jpg",
            "https://cdn.example.com/stations/nvl-pier.jpg"
          ]
        }
        """;

    private const string UpdateExample =
        """
        {
          "stationName": "Ben Nguyen Van Linh (cap nhat)",
          "address": "Q7, TP.HCM",
          "latitude": 10.7285,
          "longitude": 106.7006,
          "openingTime": "06:30",
          "closingTime": "21:30",
          "isWaterbusStation": false,
          "status": "Active",
          "imageUrls": [
            "https://cdn.example.com/stations/nvl-main.jpg",
            "https://cdn.example.com/stations/nvl-pier.jpg"
          ]
        }
        """;

    private const string UpdateStatusExample =
        """
        {
          "status": "Inactive"
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetStations, string.Empty)
            .AllowAnonymous()
            .WithSummary("Danh sach tram")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Tra ve tat ca tram, bao gom Active va Inactive.",
                "isWaterbusStation=true la ben thuoc he thong/tuyen waterbus; false la ben ngoai dung cho charter booking.",
                "Sap xep theo StationName.",
                "Managers va Staff la danh sach user active dang duoc gan voi tram."));

        group.MapGet(GetStationById, "{id:guid}")
            .AllowAnonymous()
            .WithSummary("Chi tiet tram")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Tra ve 404 neu khong tim thay tram.",
                "Managers va Staff la danh sach user active dang duoc gan voi tram."));

        group.MapPost(CreateStation, string.Empty)
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<CreateStationJsonRequest>("application/json", "multipart/form-data")
            .WithSummary("Tao tram moi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                CreateExample,
                "StationCode phai unique, chi gom chu cai, chu so va dau - (tu dong uppercase).",
                "StationName phai unique, chi gom chu cai (ho tro tieng Viet), chu so va khoang trang.",
                "openingTime va closingTime la gio mo/dong cua ben, dinh dang HH:mm.",
                "isWaterbusStation mac dinh true; gui false cho ben ngoai dung charter booking.",
                "Co the gui application/json neu khong upload anh.",
                "Dung imageUrls de gui danh sach link anh co san; imageUrl cu van duoc ho tro cho 1 anh.",
                "Neu upload anh, gui multipart/form-data voi field 'images' nhieu file; field cu 'image' van duoc ho tro cho 1 anh.",
                "Anh chi ho tro JPEG, PNG hoac WebP, toi da 5 MB; anh upload duoc resize ve 2000x1400.",
                "Moi tram toi da 6 anh, moi URL toi da 2048 ky tu.",
                "Status mac dinh la Active khi tao moi."));

        group.MapPut(UpdateStation, "{id:guid}")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<UpdateStationJsonRequest>("application/json", "multipart/form-data")
            .WithSummary("Cap nhat tram")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateExample,
                "Status hop le: Active | Inactive.",
                "StationName phai unique, chi gom chu cai (ho tro tieng Viet), chu so va khoang trang.",
                "openingTime va closingTime la gio mo/dong cua ben, dinh dang HH:mm.",
                "isWaterbusStation=true la ben thuoc he thong/tuyen waterbus; false la ben ngoai dung cho charter booking.",
                "Co the gui application/json neu khong doi anh.",
                "Neu gui imageUrl/imageUrls/images thi backend thay bo anh hien tai bang bo anh moi.",
                "Neu upload anh, gui multipart/form-data voi field 'images' nhieu file; field cu 'image' van duoc ho tro cho 1 anh.",
                "Anh chi ho tro JPEG, PNG hoac WebP, toi da 5 MB; anh upload duoc resize ve 2000x1400.",
                "Moi tram toi da 6 anh, moi URL toi da 2048 ky tu.",
                "StationCode khong doi duoc sau khi tao."));

        group.MapPatch(UpdateStationStatus, "{id:guid}/status")
            .RequireAuthorization()
            .WithSummary("Cap nhat trang thai tram")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateStatusExample,
                "Status hop le: Active | Inactive.",
                "Dung de bat/tat tram ma khong can gui lai toan bo thong tin tram."));

        group.MapDelete(DeleteStation, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Xoa tram")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Tra ve 204 khi xoa thanh cong.",
                "Tra ve 404 neu khong tim thay tram.",
                "Tra ve 400 neu tram dang duoc dung trong route, booking, trip, GPS, phan cong hoac du lieu lien quan.",
                "Neu muon an tram da phat sinh du lieu, dung PATCH /api/stations/{id}/status voi status=Inactive."));
    }

    private static async Task<IResult> GetStations(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetStationListQuery(), ct));

    private static async Task<IResult> GetStationById(ISender sender, Guid id, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetStationDetailQuery(id), ct));

    private static async Task<IResult> CreateStation(ISender sender, HttpRequest request, CancellationToken ct)
    {
        var command = request.HasFormContentType
            ? await CreateStationCommandFromFormAsync(request, ct)
            : await CreateStationCommandFromJsonAsync(request, ct);

        try
        {
            return Results.Ok(await sender.Send(command, ct));
        }
        finally
        {
            DisposeImageStreams(command.ImageFiles);
        }
    }

    private static async Task<IResult> UpdateStation(ISender sender, Guid id, HttpRequest request, CancellationToken ct)
    {
        var command = request.HasFormContentType
            ? await UpdateStationCommandFromFormAsync(id, request, ct)
            : await UpdateStationCommandFromJsonAsync(id, request, ct);

        try
        {
            return Results.Ok(await sender.Send(command, ct));
        }
        finally
        {
            DisposeImageStreams(command.ImageFiles);
        }
    }

    private static async Task<IResult> DeleteStation(ISender sender, Guid id, CancellationToken ct)
    {
        await sender.Send(new DeleteStationCommand(id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateStationStatus(
        ISender sender,
        Guid id,
        UpdateStationStatusRequest request,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateStationStatusCommand(id, request.Status), ct));

    private static async Task<CreateStationCommand> CreateStationCommandFromJsonAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var body = await request.ReadFromJsonAsync<CreateStationJsonRequest>(cancellationToken: cancellationToken);
        return new CreateStationCommand(
            body?.StationCode ?? string.Empty,
            body?.StationName ?? string.Empty,
            body?.Address,
            body?.Latitude,
            body?.Longitude,
            body?.ImageUrl,
            body?.ImageUrls,
            OpeningTime: body?.OpeningTime,
            ClosingTime: body?.ClosingTime,
            IsWaterbusStation: body?.IsWaterbusStation);
    }

    private static async Task<CreateStationCommand> CreateStationCommandFromFormAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        return new CreateStationCommand(
            GetFormValue(form, "stationCode") ?? string.Empty,
            GetFormValue(form, "stationName") ?? string.Empty,
            GetFormValue(form, "address"),
            ParseOptionalDecimal(GetFormValue(form, "latitude")),
            ParseOptionalDecimal(GetFormValue(form, "longitude")),
            GetFormValue(form, "imageUrl"),
            GetFormValues(form, "imageUrls"),
            await CreateImageFilesFromFormAsync(form, cancellationToken),
            ParseOptionalTimeOnly(GetFormValue(form, "openingTime")),
            ParseOptionalTimeOnly(GetFormValue(form, "closingTime")),
            ParseOptionalBool(GetFormValue(form, "isWaterbusStation")));
    }

    private static async Task<UpdateStationCommand> UpdateStationCommandFromJsonAsync(
        Guid stationId,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var body = await request.ReadFromJsonAsync<UpdateStationJsonRequest>(cancellationToken: cancellationToken);
        return new UpdateStationCommand(
            stationId,
            body?.StationName ?? string.Empty,
            body?.Address,
            body?.Description,
            body?.Latitude,
            body?.Longitude,
            body?.Status ?? StationStatus.Active,
            body?.ImageUrl,
            body?.ImageUrls,
            body?.HasWaitingArea,
            body?.HasParking,
            body?.HasTicketCounter,
            OpeningTime: body?.OpeningTime,
            ClosingTime: body?.ClosingTime,
            IsWaterbusStation: body?.IsWaterbusStation);
    }

    private static async Task<UpdateStationCommand> UpdateStationCommandFromFormAsync(
        Guid stationId,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        return new UpdateStationCommand(
            stationId,
            GetFormValue(form, "stationName") ?? string.Empty,
            GetFormValue(form, "address"),
            GetFormValue(form, "description"),
            ParseOptionalDecimal(GetFormValue(form, "latitude")),
            ParseOptionalDecimal(GetFormValue(form, "longitude")),
            ParseOptionalEnum<StationStatus>(GetFormValue(form, "status")) ?? StationStatus.Active,
            GetFormValue(form, "imageUrl"),
            GetFormValues(form, "imageUrls"),
            ParseOptionalBool(GetFormValue(form, "hasWaitingArea")),
            ParseOptionalBool(GetFormValue(form, "hasParking")),
            ParseOptionalBool(GetFormValue(form, "hasTicketCounter")),
            await CreateImageFilesFromFormAsync(form, cancellationToken),
            ParseOptionalTimeOnly(GetFormValue(form, "openingTime")),
            ParseOptionalTimeOnly(GetFormValue(form, "closingTime")),
            ParseOptionalBool(GetFormValue(form, "isWaterbusStation")));
    }

    private static string? GetFormValue(IFormCollection form, string name)
    {
        var value = form[name].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static IReadOnlyCollection<string>? GetFormValues(IFormCollection form, string name)
    {
        var values = new List<string>();
        AddFormValues(values, form, name);

        foreach (var key in form.Keys.Where(key =>
                     key.StartsWith($"{name}[", StringComparison.OrdinalIgnoreCase)))
        {
            AddFormValues(values, form, key);
        }

        return values.Count == 0 ? null : values.ToArray();
    }

    private static void AddFormValues(List<string> values, IFormCollection form, string name)
    {
        foreach (var value in form[name])
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value!.Trim());
            }
        }
    }

    private static async Task<IReadOnlyCollection<StationImageFileRequest>?> CreateImageFilesFromFormAsync(
        IFormCollection form,
        CancellationToken cancellationToken)
    {
        var files = form.Files
            .Where(file =>
                string.Equals(file.Name, "images", StringComparison.OrdinalIgnoreCase)
                || string.Equals(file.Name, "images[]", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (files.Count == 0)
        {
            var singleFile = form.Files.FirstOrDefault(file =>
                string.Equals(file.Name, "image", StringComparison.OrdinalIgnoreCase));
            if (singleFile is not null)
            {
                files.Add(singleFile);
            }
        }

        return files.Count == 0
            ? null
            : await CopyImageFilesAsync(files, cancellationToken);
    }

    private static async Task<IReadOnlyCollection<StationImageFileRequest>> CopyImageFilesAsync(
        IReadOnlyCollection<IFormFile> files,
        CancellationToken cancellationToken)
    {
        var copiedFiles = new List<StationImageFileRequest>(files.Count);
        foreach (var file in files)
        {
            var content = new MemoryStream();
            await file.CopyToAsync(content, cancellationToken);
            content.Position = 0;
            copiedFiles.Add(new StationImageFileRequest(
                file.FileName,
                file.ContentType,
                file.Length,
                content));
        }

        return copiedFiles;
    }

    private static void DisposeImageStreams(IReadOnlyCollection<StationImageFileRequest>? imageFiles)
    {
        foreach (var imageFile in imageFiles ?? [])
        {
            imageFile.Content.Dispose();
        }
    }

    private static decimal? ParseOptionalDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static bool? ParseOptionalBool(string? value) =>
        bool.TryParse(value, out var result) ? result : null;

    private static T? ParseOptionalEnum<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var result) ? result : null;

    private static TimeOnly? ParseOptionalTimeOnly(string? value) =>
        TimeOnly.TryParseExact(value, ["HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
            ? result
            : null;

    private sealed record CreateStationJsonRequest(
        string StationCode,
        string StationName,
        string? Address = null,
        decimal? Latitude = null,
        decimal? Longitude = null,
        TimeOnly? OpeningTime = null,
        TimeOnly? ClosingTime = null,
        bool? IsWaterbusStation = null,
        string? ImageUrl = null,
        IReadOnlyCollection<string>? ImageUrls = null);

    private sealed record UpdateStationJsonRequest(
        string StationName,
        string? Address = null,
        string? Description = null,
        decimal? Latitude = null,
        decimal? Longitude = null,
        TimeOnly? OpeningTime = null,
        TimeOnly? ClosingTime = null,
        bool? IsWaterbusStation = null,
        StationStatus? Status = null,
        string? ImageUrl = null,
        IReadOnlyCollection<string>? ImageUrls = null,
        bool? HasWaitingArea = null,
        bool? HasParking = null,
        bool? HasTicketCounter = null);

    private sealed record UpdateStationStatusRequest(StationStatus Status);
}
