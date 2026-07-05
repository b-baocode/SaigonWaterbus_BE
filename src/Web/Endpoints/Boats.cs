using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using SaigonWaterbus.Application.BoatStaffAssignments;
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
          "seatCount": 80,
          "numberOfDecks": 1,
          "seatSetupType": "FullStandard",
          "rentalPrices": [
            {
              "rentalUnit": "Hour",
              "unitPrice": 2000000,
              "currency": "VND",
              "note": "Gia tham khao theo gio."
            },
            {
              "rentalUnit": "Day",
              "unitPrice": 15000000,
              "currency": "VND",
              "note": "Gia tham khao theo ngay."
            }
          ],
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
          "name": "Tau Waterbus 01 (moi)",
          "seatCount": 90,
          "rentalPrices": [
            {
              "rentalUnit": "Hour",
              "unitPrice": 2500000,
              "currency": "VND",
              "note": "Gia cap nhat theo gio."
            }
          ]
        }
        """;

    private const string UpdateStatusExample =
        """
        {
          "status": "UnderMaintenance"
        }
        """;

    private const string AssignStaffExample =
        """
        {
          "staffUserId": "00000000-0000-0000-0000-000000000000",
          "workingDate": "2026-06-29",
          "shiftCode": "Day",
          "dutyRole": "Crew"
        }
        """;

    private const string ReplaceStaffExample =
        """
        {
          "replacementStaffUserId": "00000000-0000-0000-0000-000000000000",
          "reason": "Staff nghi dot xuat"
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
                "Manager và Staff chỉ xem được tàu đang Active."));

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
                "Mỗi tàu hiện lưu 1 ảnh chính.",
                "Code tàu được chuẩn hóa thành chữ in hoa.",
                "seatSetupType: FullStandard hoặc StandardAndVip, là đặc tính của tàu.",
                "Tàu chưa thuộc dịch vụ nào; dịch vụ sẽ được chọn khi phân lịch chạy.",
                "Không cần gửi status khi tạo tàu. Backend tự tạo Inactive, setup đủ ghế rồi mới chuyển Active.",
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
                "Nếu gửi rentalPrices thì backend cập nhật/thêm các giá theo rentalUnit được gửi, không xóa giá cũ không có trong payload.",
                "Có thể gửi application/json nếu không đổi ảnh.",
                "Nếu gửi ảnh mới bằng imageUrl/imageUrls/images thì backend thay bộ ảnh hiện tại bằng bộ ảnh mới.",
                "Nếu upload ảnh, gửi multipart/form-data với field 'images' nhiều file; field cũ 'image' vẫn được hỗ trợ cho 1 ảnh.",
                "Ảnh chỉ hỗ trợ JPEG, PNG hoặc WebP, tối đa 5 MB.",
                "Mỗi tàu hiện lưu 1 ảnh chính.",
                "Với multipart/form-data, rentalPrices dùng dạng rentalPrices[0].rentalUnit, rentalPrices[0].unitPrice, rentalPrices[0].currency, rentalPrices[0].note."));

        groupBuilder.MapPatch(UpdateBoatStatus, "{boatId:guid}/status")
            .RequireAuthorization()
            .WithSummary("Cập nhật trạng thái tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateStatusExample,
                "Các trạng thái hợp lệ: Active, UnderMaintenance, Inactive, Retired.",
                "Muốn chuyển Active thì tàu phải setup đủ ghế.",
                "Tàu ở trạng thái không phải Active hoặc chưa setup ghế sẽ không hiện với Manager và Staff."));

        groupBuilder.MapGet(GetBoatStaffAssignments, "{boatId:guid}/staff-assignments")
            .RequireAuthorization()
            .WithSummary("Xem lịch staff của tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                null,
                "Query params optional: workingDate=yyyy-MM-dd, shiftCode, activeOnly=true/false.",
                "Dùng để lấy staff đang được gắn với tàu theo ngày/ca."));

        groupBuilder.MapPost(AssignBoatStaff, "{boatId:guid}/staff-assignments")
            .RequireAuthorization()
            .WithSummary("Phân staff cho tàu theo ngày/ca")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoặc Manager",
                AssignStaffExample,
                "workingDate là ngày làm việc. shiftCode chỉ được Day hoặc Evening; mặc định Day nếu không gửi.",
                "Không cho phân cùng một staff active ở hai tàu trong cùng ngày/ca."));

        groupBuilder.MapPost(ReplaceBoatStaff, "{boatId:guid}/staff-assignments/{assignmentId:guid}/replace")
            .RequireAuthorization()
            .WithSummary("Thay staff được phân cho tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoặc Manager",
                ReplaceStaffExample,
                "Phân công cũ chuyển inactive, tạo phân công mới active cho staff thay thế.",
                "Bắt buộc có reason để lưu lịch sử thay thế."));

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
        [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        Results.Ok(await boatManagementService.GetBoatsAsync(status, search, cancellationToken));

    private static async Task<IResult> GetBoatById(
        IBoatManagementService boatManagementService,
        Guid boatId,
        CancellationToken cancellationToken) =>
        Results.Ok(await boatManagementService.GetBoatByIdAsync(boatId, cancellationToken));

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
            new UpdateBoatStatusRequest(boatId, request.Status),
            cancellationToken));

    private static async Task<IResult> GetBoatStaffAssignments(
        ISender sender,
        Guid boatId,
        [FromQuery] DateOnly? workingDate,
        [FromQuery] string? shiftCode,
        [FromQuery] bool? activeOnly,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new GetBoatStaffAssignmentsQuery(boatId, workingDate, shiftCode, activeOnly ?? true),
            cancellationToken));

    private static async Task<IResult> AssignBoatStaff(
        ISender sender,
        Guid boatId,
        AssignBoatStaffApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new AssignBoatStaffCommand(
                boatId,
                request.StaffUserId,
                request.WorkingDate,
                request.ShiftCode,
                request.DutyRole),
            cancellationToken));

    private static async Task<IResult> ReplaceBoatStaff(
        ISender sender,
        Guid boatId,
        Guid assignmentId,
        ReplaceBoatStaffApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new ReplaceBoatStaffCommand(
                boatId,
                assignmentId,
                request.ReplacementStaffUserId,
                request.Reason),
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
            body?.SeatCount ?? 0,
            body?.NumberOfDecks ?? 0,
            body?.RegistrationNumber,
            body?.MaxSpeedKmh,
            body?.YearBuilt,
            body?.Description,
            body?.ImageUrl,
            SeatSetupType: body?.SeatSetupType ?? SeatSetupType.FullStandard,
            RentalPrices: body?.RentalPrices?.Select(ToApplicationRentalPrice).ToArray(),
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
            ParseOptionalInt(GetFormValue(form, "seatCount")) ?? 0,
            ParseOptionalInt(GetFormValue(form, "numberOfDecks")) ?? 0,
            GetFormValue(form, "registrationNumber"),
            ParseOptionalInt(GetFormValue(form, "maxSpeedKmh")),
            ParseOptionalInt(GetFormValue(form, "yearBuilt")),
            GetFormValue(form, "description"),
            GetFormValue(form, "imageUrl"),
            SeatSetupType: ParseOptionalEnum<SeatSetupType>(GetFormValue(form, "seatSetupType"))
                ?? SeatSetupType.FullStandard,
            RentalPrices: CreateRentalPricesFromForm(form),
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
            body?.SeatCount,
            body?.NumberOfDecks,
            body?.RegistrationNumber,
            body?.MaxSpeedKmh,
            body?.YearBuilt,
            body?.Description,
            body?.ImageUrl,
            SeatSetupType: body?.SeatSetupType,
            ImageUrls: body?.ImageUrls,
            RentalPrices: body?.RentalPrices?.Select(ToApplicationRentalPrice).ToArray());
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
            ParseOptionalInt(GetFormValue(form, "seatCount")),
            ParseOptionalInt(GetFormValue(form, "numberOfDecks")),
            GetFormValue(form, "registrationNumber"),
            ParseOptionalInt(GetFormValue(form, "maxSpeedKmh")),
            ParseOptionalInt(GetFormValue(form, "yearBuilt")),
            GetFormValue(form, "description"),
            GetFormValue(form, "imageUrl"),
            SeatSetupType: ParseOptionalEnum<SeatSetupType>(GetFormValue(form, "seatSetupType")),
            ImageUrls: GetFormValues(form, "imageUrls"),
            ImageFiles: await CreateImageFilesFromFormAsync(form, cancellationToken),
            RentalPrices: CreateRentalPricesFromForm(form));
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

    private static decimal? ParseOptionalDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static T? ParseOptionalEnum<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var result) ? result : null;

    private static BoatRentalPriceRequest ToApplicationRentalPrice(BoatRentalPriceApiRequest request) =>
        new(request.RentalUnit, request.UnitPrice, request.Currency, request.Note);

    private static IReadOnlyCollection<BoatRentalPriceRequest>? CreateRentalPricesFromForm(IFormCollection form)
    {
        var indexedRentalPrices = CreateIndexedRentalPricesFromForm(form);
        if (indexedRentalPrices is not null)
        {
            return indexedRentalPrices;
        }

        var rentalPrices = new List<BoatRentalPriceRequest>();
        var hourlyPrice = ParseOptionalDecimal(GetFormValue(form, "hourlyUnitPrice")
            ?? GetFormValue(form, "hourlyRentalPrice"));
        var dailyPrice = ParseOptionalDecimal(GetFormValue(form, "dailyUnitPrice")
            ?? GetFormValue(form, "dailyRentalPrice"));

        if (hourlyPrice.HasValue)
        {
            rentalPrices.Add(new BoatRentalPriceRequest(
                BoatRentalUnit.Hour,
                hourlyPrice.Value,
                GetFormValue(form, "hourlyCurrency"),
                GetFormValue(form, "hourlyNote")));
        }

        if (dailyPrice.HasValue)
        {
            rentalPrices.Add(new BoatRentalPriceRequest(
                BoatRentalUnit.Day,
                dailyPrice.Value,
                GetFormValue(form, "dailyCurrency"),
                GetFormValue(form, "dailyNote")));
        }

        return rentalPrices.Count == 0 ? null : rentalPrices;
    }

    private static IReadOnlyCollection<BoatRentalPriceRequest>? CreateIndexedRentalPricesFromForm(IFormCollection form)
    {
        const string prefix = "rentalPrices[";

        var indices = new SortedSet<int>();
        foreach (var key in form.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var closeIndex = key.IndexOf(']');
            if (closeIndex <= prefix.Length)
            {
                continue;
            }

            if (int.TryParse(key.AsSpan(prefix.Length, closeIndex - prefix.Length), out var index))
            {
                indices.Add(index);
            }
        }

        if (indices.Count == 0)
        {
            return null;
        }

        var rentalPrices = new List<BoatRentalPriceRequest>();
        foreach (var index in indices)
        {
            var rentalUnit = ParseOptionalEnum<BoatRentalUnit>(
                GetFormValue(form, $"rentalPrices[{index}].rentalUnit"));
            var unitPrice = ParseOptionalDecimal(
                GetFormValue(form, $"rentalPrices[{index}].unitPrice"));

            if (rentalUnit is null || unitPrice is null)
            {
                continue;
            }

            rentalPrices.Add(new BoatRentalPriceRequest(
                rentalUnit.Value,
                unitPrice.Value,
                GetFormValue(form, $"rentalPrices[{index}].currency"),
                GetFormValue(form, $"rentalPrices[{index}].note")));
        }

        return rentalPrices.Count == 0 ? null : rentalPrices;
    }

    private sealed record CreateBoatJsonRequest(
        string Code,
        string Name,
        int SeatCount,
        int NumberOfDecks,
        SeatSetupType SeatSetupType = SeatSetupType.FullStandard,
        string? RegistrationNumber = null,
        int? MaxSpeedKmh = null,
        int? YearBuilt = null,
        string? Description = null,
        string? ImageUrl = null,
        IReadOnlyCollection<string>? ImageUrls = null,
        IReadOnlyCollection<BoatRentalPriceApiRequest>? RentalPrices = null);

    private sealed record UpdateBoatJsonRequest(
        string? Code = null,
        string? Name = null,
        int? SeatCount = null,
        int? NumberOfDecks = null,
        SeatSetupType? SeatSetupType = null,
        string? RegistrationNumber = null,
        int? MaxSpeedKmh = null,
        int? YearBuilt = null,
        string? Description = null,
        string? ImageUrl = null,
        IReadOnlyCollection<string>? ImageUrls = null,
        IReadOnlyCollection<BoatRentalPriceApiRequest>? RentalPrices = null);

    private sealed record UpdateBoatStatusApiRequest(BoatStatus Status);

    private sealed record AssignBoatStaffApiRequest(
        Guid StaffUserId,
        DateOnly WorkingDate,
        string? ShiftCode,
        string? DutyRole);

    private sealed record ReplaceBoatStaffApiRequest(
        Guid ReplacementStaffUserId,
        string Reason);

    private sealed record BoatRentalPriceApiRequest(
        BoatRentalUnit RentalUnit,
        decimal UnitPrice,
        string? Currency = null,
        string? Note = null);
}
