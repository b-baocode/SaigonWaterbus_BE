using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using SaigonWaterbus.Application.Boats;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Boats : IEndpointGroup
{
    private const string CreateBoatExample =
        """
        {
          "code": "WB01",
          "name": "Tau Waterbus 01",
          "numberOfDecks": 1,
          "seatSetupType": "FullStandard",
          "registrationNumber": "SG-1234",
          "maxSpeedKmh": 30,
          "yearBuilt": 2020,
          "imageUrls": [
            "https://cdn.example.com/boats/wb01-main.jpg",
            "https://cdn.example.com/boats/wb01-deck.jpg"
          ],
          "description": "Tau cong cong tuyen so 1."
        }
        """;

    private const string UpdateBoatExample =
        """
        {
          "name": "Tau Waterbus 01 (moi)"
        }
        """;

    private const string UpdateStatusExample =
        """
        {
          "status": "UnderMaintenance",
          "estimatedMaintenanceEndAt": "2026-08-10T17:00:00+07:00",
          "maintenanceNote": "Kiem tra dong co sau su co."
        }
        """;

    public static string RoutePrefix => "/api/boats";

    public static string OpenApiTag => "Boats";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetBoats, "")
            .RequireAuthorization()
            .WithSummary("Lấy danh sách tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                null,
                "Admin thấy tất cả tàu (mọi trạng thái).",
                "Manager và Staff chỉ thấy tàu đang Active.",
                "Có thể lọc theo status và từ khóa tìm kiếm."));

        groupBuilder.MapGet(GetBoatById, "{boatId:guid}")
            .RequireAuthorization()
            .WithSummary("Lấy chi tiết tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                null,
                "Admin xem được tàu ở mọi trạng thái.",
                "Manager và Staff chỉ xem được tàu đang Active.",
                "Response có maintenanceStartedAt và documentsRequireRefresh để FE hiển thị banner hồ sơ sau bảo trì."));

        groupBuilder.MapGet(GetBoatDocuments, "{boatId:guid}/documents")
            .RequireAuthorization()
            .WithSummary("Lấy 4 slot hồ sơ tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                null,
                "Trả đủ 4 loại hồ sơ: Inspection, Registration, Insurance, OperationLicense.",
                "Slot chưa upload có isUploaded=false và fileUrl=null.",
                "Mỗi slot có requiresRefresh; hiện chỉ Inspection cần refresh sau bảo trì.",
                "Admin xem được tàu ở mọi trạng thái; Manager và Staff chỉ xem được tàu đang Active."));

        groupBuilder.MapPost(CreateBoat, "")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<CreateBoatJsonRequest>("application/json", "multipart/form-data")
            .WithSummary("Tạo tàu mới")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                CreateBoatExample,
                "Có thể gửi application/json nếu không có ảnh.",
                "Nếu gửi URL ảnh bằng JSON, dùng imageUrls là danh sách ảnh; imageUrl cũ vẫn được hỗ trợ cho 1 ảnh.",
                "Nếu upload ảnh, gửi multipart/form-data với các field tương ứng và field 'images' nhiều file; field cũ 'image' vẫn được hỗ trợ cho 1 ảnh.",
                "Ảnh chỉ hỗ trợ JPEG, PNG hoặc WebP, tối đa 5 MB.",
                "Mỗi tàu lưu tối đa 3 ảnh.",
                "Code tàu được chuẩn hóa thành chữ in hoa.",
                "seatSetupType: FullStandard hoặc StandardAndVip, là đặc tính của tàu.",
                "Không cần nhập seatCount khi tạo tàu; backend tự tính sau khi setup sơ đồ ghế.",
                "Không nhập giá thuê khi tạo tàu. Giá charter dùng policy chung theo số tầng tại /api/charter-bookings/admin/rental-price-policies.",
                "Tàu chưa thuộc dịch vụ nào; dịch vụ sẽ được chọn khi phân lịch chạy.",
                "Không cần gửi status khi tạo tàu. Backend tự tạo Inactive; khi đủ ghế và đủ 4 hồ sơ thì tự chuyển Active.",
                "Số đăng ký tàu phải là duy nhất nếu cung cấp."));

        groupBuilder.MapPut(UpdateBoat, "{boatId:guid}")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<UpdateBoatJsonRequest>("application/json", "multipart/form-data")
            .WithSummary("Cập nhật thông tin tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateBoatExample,
                "Chỉ field nào gửi lên mới được cập nhật.",
                "Không cập nhật giá thuê trong API tàu. Giá charter dùng policy chung theo số tầng tại /api/charter-bookings/admin/rental-price-policies.",
                "Có thể gửi application/json nếu không đổi ảnh.",
                "Nếu gửi ảnh mới bằng imageUrl/imageUrls/images thì backend thay bộ ảnh hiện tại bằng bộ ảnh mới.",
                "Nếu upload ảnh, gửi multipart/form-data với field 'images' nhiều file; field cũ 'image' vẫn được hỗ trợ cho 1 ảnh.",
                "Ảnh chỉ hỗ trợ JPEG, PNG hoặc WebP, tối đa 5 MB.",
                "Mỗi tàu lưu tối đa 3 ảnh."));

        groupBuilder.MapPatch(UpdateBoatStatus, "{boatId:guid}/status")
            .RequireAuthorization()
            .WithSummary("Cập nhật trạng thái tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateStatusExample,
                "Các trạng thái hợp lệ: Active, UnderMaintenance, Inactive, Retired.",
                "Muốn chuyển Active thì tàu phải setup đủ ghế.",
                "Muốn chuyển Active thì tàu phải có đủ 4 hồ sơ: Inspection, Registration, Insurance, OperationLicense.",
                "Flow thường không cần gọi API này để Active: setup đủ ghế và upload đủ hồ sơ thì backend tự Active.",
                "Khi status=UnderMaintenance, có thể gửi estimatedMaintenanceEndAt và maintenanceNote để FE hiển thị dự kiến bảo trì.",
                "Khi chuyển sang trạng thái khác UnderMaintenance, backend tự xóa estimatedMaintenanceEndAt và maintenanceNote.",
                "Nếu tàu đã vào UnderMaintenance, upload lại hồ sơ Inspection sau thời điểm vào bảo trì thì backend tự Active khi các điều kiện khác đã đủ.",
                "Tàu ở trạng thái không phải Active hoặc chưa setup ghế sẽ không hiện với Manager và Staff."));

        groupBuilder.MapPut(UpdateBoatDocument, "{boatId:guid}/documents/{type}")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<BoatDocumentFormRequest>("multipart/form-data")
            .WithSummary("Upload hoặc cập nhật một hồ sơ tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "type trên route: Inspection, Registration, Insurance hoặc OperationLicense.",
                "Gửi multipart/form-data với field file.",
                "Các field optional: issuedDate=yyyy-MM-dd, expiryDate=yyyy-MM-dd.",
                "Upload lại cùng type sẽ thay thế slot hiện tại. Tối đa 4 file, mỗi loại 1 file.",
                "Nếu sau upload tàu đủ ghế và đủ hồ sơ thì backend tự chuyển Active.",
                "Hỗ trợ PDF, JPEG, PNG hoặc WebP, tối đa 10 MB."));

        groupBuilder.MapDelete(DeleteBoatDocument, "{boatId:guid}/documents/{type}")
            .RequireAuthorization()
            .WithSummary("Xóa một slot hồ sơ tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "type trên route: Inspection, Registration, Insurance hoặc OperationLicense.",
                "Xóa metadata hồ sơ khỏi tàu. Nếu tàu đang Active thì backend tự chuyển Inactive."));

        groupBuilder.MapDelete(DeleteBoat, "{boatId:guid}")
            .RequireAuthorization()
            .WithSummary("Xóa tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Chỉ xóa khi tàu chưa được tham chiếu bởi lịch chạy (Schedule).",
                "Ưu tiên đổi trạng thái Retired thay vì xóa nếu tàu đã có lịch sử vận hành."));
    }

    private static async Task<IResult> GetBoats(
        IBoatManagementService boatManagementService,
        [FromQuery] BoatStatus? status,
        [FromQuery] BoatServiceType? serviceType,
        [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        Results.Ok(await boatManagementService.GetBoatsAsync(status, serviceType, search, cancellationToken));

    private static async Task<IResult> GetBoatById(
        IBoatManagementService boatManagementService,
        Guid boatId,
        CancellationToken cancellationToken) =>
        Results.Ok(await boatManagementService.GetBoatByIdAsync(boatId, cancellationToken));

    private static async Task<IResult> GetBoatDocuments(
        IBoatManagementService boatManagementService,
        Guid boatId,
        CancellationToken cancellationToken) =>
        Results.Ok(await boatManagementService.GetBoatDocumentsAsync(boatId, cancellationToken));

    private static async Task<IResult> CreateBoat(
        IBoatManagementService boatManagementService,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var command = request.HasFormContentType
            ? await CreateBoatRequestFromFormAsync(request, cancellationToken)
            : await CreateBoatRequestFromJsonAsync(request, cancellationToken);

        try
        {
            return Results.Ok(await boatManagementService.CreateBoatAsync(command, cancellationToken));
        }
        finally
        {
            DisposeImageStreams(command);
        }
    }

    private static async Task<IResult> UpdateBoat(
        IBoatManagementService boatManagementService,
        Guid boatId,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var command = request.HasFormContentType
            ? await UpdateBoatRequestFromFormAsync(boatId, request, cancellationToken)
            : await UpdateBoatRequestFromJsonAsync(boatId, request, cancellationToken);

        try
        {
            return Results.Ok(await boatManagementService.UpdateBoatAsync(command, cancellationToken));
        }
        finally
        {
            DisposeImageStreams(command);
        }
    }

    private static async Task<IResult> UpdateBoatStatus(
        IBoatManagementService boatManagementService,
        Guid boatId,
        UpdateBoatStatusApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await boatManagementService.UpdateBoatStatusAsync(
            new UpdateBoatStatusRequest(
                boatId,
                request.Status,
                request.EstimatedMaintenanceEndAt,
                request.MaintenanceNote),
            cancellationToken));

    private static async Task<IResult> UpdateBoatDocument(
        IBoatManagementService boatManagementService,
        Guid boatId,
        BoatDocumentType type,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { message = "Gửi multipart/form-data với field file." });
        }

        var command = await UpdateBoatDocumentRequestFromFormAsync(boatId, type, request, cancellationToken);
        if (command is null)
        {
            return Results.BadRequest(new { message = "Gửi multipart/form-data với field file." });
        }

        try
        {
            return Results.Ok(await boatManagementService.UpdateBoatDocumentAsync(command, cancellationToken));
        }
        finally
        {
            command.File.Content.Dispose();
        }
    }

    private static async Task<IResult> DeleteBoatDocument(
        IBoatManagementService boatManagementService,
        Guid boatId,
        BoatDocumentType type,
        CancellationToken cancellationToken) =>
        Results.Ok(await boatManagementService.DeleteBoatDocumentAsync(
            new DeleteBoatDocumentRequest(boatId, type),
            cancellationToken));

    private static async Task<IResult> DeleteBoat(
        IBoatManagementService boatManagementService,
        Guid boatId,
        CancellationToken cancellationToken) =>
        Results.Ok(await boatManagementService.DeleteBoatAsync(boatId, cancellationToken));

    private static async Task<CreateBoatRequest> CreateBoatRequestFromJsonAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var body = await request.ReadFromJsonAsync<CreateBoatJsonRequest>(cancellationToken: cancellationToken);
        return new CreateBoatRequest(
            body?.Code ?? string.Empty,
            body?.Name ?? string.Empty,
            BoatStatus.Inactive,
            0,
            body?.NumberOfDecks ?? 0,
            body?.RegistrationNumber,
            body?.MaxSpeedKmh,
            body?.YearBuilt,
            body?.Description,
            body?.ImageUrl,
            ServiceType: body?.ServiceType ?? BoatServiceType.Passenger,
            SeatSetupType: body?.SeatSetupType ?? SeatSetupType.FullStandard,
            ImageUrls: body?.ImageUrls);
    }

    private static async Task<CreateBoatRequest> CreateBoatRequestFromFormAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);

        return new CreateBoatRequest(
            GetFormValue(form, "code") ?? string.Empty,
            GetFormValue(form, "name") ?? string.Empty,
            BoatStatus.Inactive,
            0,
            ParseOptionalInt(GetFormValue(form, "numberOfDecks")) ?? 0,
            GetFormValue(form, "registrationNumber"),
            ParseOptionalInt(GetFormValue(form, "maxSpeedKmh")),
            ParseOptionalInt(GetFormValue(form, "yearBuilt")),
            GetFormValue(form, "description"),
            GetFormValue(form, "imageUrl"),
            ServiceType: ParseOptionalEnum<BoatServiceType>(GetFormValue(form, "serviceType"))
                ?? BoatServiceType.Passenger,
            SeatSetupType: ParseOptionalEnum<SeatSetupType>(GetFormValue(form, "seatSetupType"))
                ?? SeatSetupType.FullStandard,
            ImageUrls: GetFormValues(form, "imageUrls"),
            ImageFiles: await CreateImageFilesFromFormAsync(form, cancellationToken));
    }

    private static async Task<UpdateBoatRequest> UpdateBoatRequestFromJsonAsync(
        Guid boatId,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var body = await request.ReadFromJsonAsync<UpdateBoatJsonRequest>(cancellationToken: cancellationToken);
        return new UpdateBoatRequest(
            boatId,
            body?.Code,
            body?.Name,
            body?.NumberOfDecks,
            body?.RegistrationNumber,
            body?.MaxSpeedKmh,
            body?.YearBuilt,
            body?.Description,
            body?.EstimatedMaintenanceEndAt,
            body?.MaintenanceNote,
            body?.ImageUrl,
            ServiceType: body?.ServiceType,
            SeatSetupType: body?.SeatSetupType,
            ImageUrls: body?.ImageUrls);
    }

    private static async Task<UpdateBoatRequest> UpdateBoatRequestFromFormAsync(
        Guid boatId,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);

        return new UpdateBoatRequest(
            boatId,
            GetFormValue(form, "code"),
            GetFormValue(form, "name"),
            ParseOptionalInt(GetFormValue(form, "numberOfDecks")),
            GetFormValue(form, "registrationNumber"),
            ParseOptionalInt(GetFormValue(form, "maxSpeedKmh")),
            ParseOptionalInt(GetFormValue(form, "yearBuilt")),
            GetFormValue(form, "description"),
            ParseOptionalDateTimeOffset(GetFormValue(form, "estimatedMaintenanceEndAt")),
            GetFormValue(form, "maintenanceNote"),
            GetFormValue(form, "imageUrl"),
            ServiceType: ParseOptionalEnum<BoatServiceType>(GetFormValue(form, "serviceType")),
            SeatSetupType: ParseOptionalEnum<SeatSetupType>(GetFormValue(form, "seatSetupType")),
            ImageUrls: GetFormValues(form, "imageUrls"),
            ImageFiles: await CreateImageFilesFromFormAsync(form, cancellationToken));
    }

    private static async Task<UpdateBoatDocumentRequest?> UpdateBoatDocumentRequestFromFormAsync(
        Guid boatId,
        BoatDocumentType type,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.FirstOrDefault(file =>
            string.Equals(file.Name, "file", StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            return null;
        }

        var content = new MemoryStream();
        await file.CopyToAsync(content, cancellationToken);
        content.Position = 0;

        return new UpdateBoatDocumentRequest(
            boatId,
            type,
            new BoatDocumentFileRequest(
                file.FileName,
                file.ContentType,
                file.Length,
                content),
            ParseOptionalDateOnly(GetFormValue(form, "issuedDate")),
            ParseOptionalDateOnly(GetFormValue(form, "expiryDate")));
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

    private static async Task<IReadOnlyCollection<BoatImageFileRequest>?> CreateImageFilesFromFormAsync(
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

    private static async Task<IReadOnlyCollection<BoatImageFileRequest>> CopyImageFilesAsync(
        IReadOnlyCollection<IFormFile> files,
        CancellationToken cancellationToken)
    {
        var copiedFiles = new List<BoatImageFileRequest>(files.Count);
        foreach (var file in files)
        {
            var content = new MemoryStream();
            await file.CopyToAsync(content, cancellationToken);
            content.Position = 0;
            copiedFiles.Add(new BoatImageFileRequest(
                file.FileName,
                file.ContentType,
                file.Length,
                content));
        }

        return copiedFiles;
    }

    private static void DisposeImageStreams(CreateBoatRequest command)
    {
        command.ImageContent?.Dispose();
        foreach (var imageFile in command.ImageFiles ?? [])
        {
            imageFile.Content.Dispose();
        }
    }

    private static void DisposeImageStreams(UpdateBoatRequest command)
    {
        command.ImageContent?.Dispose();
        foreach (var imageFile in command.ImageFiles ?? [])
        {
            imageFile.Content.Dispose();
        }
    }

    private static int? ParseOptionalInt(string? value) =>
        int.TryParse(value, out var result) ? result : null;

    private static DateOnly? ParseOptionalDateOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateOnly.TryParseExact(
            value,
            ["yyyy-MM-dd", "dd/MM/yyyy"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var result)
            ? result
            : null;
    }

    private static DateTimeOffset? ParseOptionalDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var result)
            ? result
            : null;
    }

    private static T? ParseOptionalEnum<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var result) ? result : null;

    private sealed record CreateBoatJsonRequest(
        string Code,
        string Name,
        int NumberOfDecks = 0,
        BoatServiceType ServiceType = BoatServiceType.Passenger,
        SeatSetupType SeatSetupType = SeatSetupType.FullStandard,
        string? RegistrationNumber = null,
        int? MaxSpeedKmh = null,
        int? YearBuilt = null,
        string? Description = null,
        string? ImageUrl = null,
        IReadOnlyCollection<string>? ImageUrls = null);

    private sealed record UpdateBoatJsonRequest(
        string? Code = null,
        string? Name = null,
        int? NumberOfDecks = null,
        BoatServiceType? ServiceType = null,
        SeatSetupType? SeatSetupType = null,
        string? RegistrationNumber = null,
        int? MaxSpeedKmh = null,
        int? YearBuilt = null,
        string? Description = null,
        DateTimeOffset? EstimatedMaintenanceEndAt = null,
        string? MaintenanceNote = null,
        string? ImageUrl = null,
        IReadOnlyCollection<string>? ImageUrls = null);

    private sealed record UpdateBoatStatusApiRequest(
        BoatStatus Status,
        DateTimeOffset? EstimatedMaintenanceEndAt = null,
        string? MaintenanceNote = null);

    private sealed record BoatDocumentFormRequest(
        IFormFile File,
        DateOnly? IssuedDate = null,
        DateOnly? ExpiryDate = null);

}
