using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using SaigonWaterbus.Application.Vessels;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Vessels : IEndpointGroup
{
    private const string CreateVesselExample =
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
            "https://cdn.example.com/vessels/wb01-main.jpg",
            "https://cdn.example.com/vessels/wb01-deck.jpg"
          ],
          "description": "Tau cong cong tuyen so 1."
        }
        """;

    private const string UpdateVesselExample =
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
          "status": "Maintenance"
        }
        """;

    public static string RoutePrefix => "/api/vessels";

    public static string OpenApiTag => "Vessels";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetVessels, "")
            .RequireAuthorization()
            .WithSummary("Lấy danh sách tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                null,
                "Admin thấy tất cả tàu (mọi trạng thái).",
                "Manager và Staff chỉ thấy tàu đang Active.",
                "Có thể lọc theo status và từ khóa tìm kiếm."));

        groupBuilder.MapGet(GetVesselById, "{vesselId:guid}")
            .RequireAuthorization()
            .WithSummary("Lấy chi tiết tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                null,
                "Admin xem được tàu ở mọi trạng thái.",
                "Manager và Staff chỉ xem được tàu đang Active."));

        groupBuilder.MapPost(CreateVessel, "")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<CreateVesselJsonRequest>("application/json", "multipart/form-data")
            .WithSummary("Tạo tàu mới")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                CreateVesselExample,
                "Có thể gửi application/json nếu không có ảnh.",
                "Nếu gửi URL ảnh bằng JSON, dùng imageUrls là danh sách ảnh; imageUrl cũ vẫn được hỗ trợ cho 1 ảnh.",
                "Nếu upload ảnh, gửi multipart/form-data với các field tương ứng và field 'images' nhiều file; field cũ 'image' vẫn được hỗ trợ cho 1 ảnh.",
                "Ảnh chỉ hỗ trợ JPEG, PNG hoặc WebP, tối đa 5 MB.",
                "Mỗi tàu tối đa 10 ảnh.",
                "Code tàu được chuẩn hóa thành chữ in hoa.",
                "seatSetupType: FullStandard hoặc StandardAndVip, là đặc tính của tàu.",
                "Tàu chưa thuộc dịch vụ nào; dịch vụ sẽ được chọn khi phân lịch chạy.",
                "Không cần gửi status khi tạo tàu. Backend tự tạo Inactive, setup đủ ghế rồi mới chuyển Active.",
                "Số đăng ký tàu phải là duy nhất nếu cung cấp."));

        groupBuilder.MapPut(UpdateVessel, "{vesselId:guid}")
            .RequireAuthorization()
            .DisableAntiforgery()
            .Accepts<UpdateVesselJsonRequest>("application/json", "multipart/form-data")
            .WithSummary("Cập nhật thông tin tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateVesselExample,
                "Chỉ field nào gửi lên mới được cập nhật.",
                "Nếu gửi rentalPrices thì backend cập nhật/thêm các giá theo rentalUnit được gửi, không xóa giá cũ không có trong payload.",
                "Có thể gửi application/json nếu không đổi ảnh.",
                "Nếu gửi ảnh mới bằng imageUrl/imageUrls/images thì backend thay bộ ảnh hiện tại bằng bộ ảnh mới.",
                "Nếu upload ảnh, gửi multipart/form-data với field 'images' nhiều file; field cũ 'image' vẫn được hỗ trợ cho 1 ảnh.",
                "Với multipart/form-data, rentalPrices dùng dạng rentalPrices[0].rentalUnit, rentalPrices[0].unitPrice, rentalPrices[0].currency, rentalPrices[0].note."));

        groupBuilder.MapPatch(UpdateVesselStatus, "status/{vesselId:guid}")
            .RequireAuthorization()
            .WithSummary("Cập nhật trạng thái tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateStatusExample,
                "Các trạng thái hợp lệ: Active, Maintenance, Inactive, Retired.",
                "Muốn chuyển Active thì tàu phải setup đủ ghế.",
                "Tàu ở trạng thái không phải Active hoặc chưa setup ghế sẽ không hiện với Manager và Staff."));

        groupBuilder.MapDelete(DeleteVessel, "{vesselId:guid}")
            .RequireAuthorization()
            .WithSummary("Xóa tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Chỉ xóa khi tàu chưa được tham chiếu bởi lịch chạy (Schedule).",
                "Ưu tiên đổi trạng thái Retired thay vì xóa nếu tàu đã có lịch sử vận hành."));
    }

    private static async Task<IResult> GetVessels(
        IVesselManagementService vesselManagementService,
        [FromQuery] VesselStatus? status,
        [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        Results.Ok(await vesselManagementService.GetVesselsAsync(status, search, cancellationToken));

    private static async Task<IResult> GetVesselById(
        IVesselManagementService vesselManagementService,
        Guid vesselId,
        CancellationToken cancellationToken) =>
        Results.Ok(await vesselManagementService.GetVesselByIdAsync(vesselId, cancellationToken));

    private static async Task<IResult> CreateVessel(
        IVesselManagementService vesselManagementService,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var command = request.HasFormContentType
            ? await CreateVesselRequestFromFormAsync(request, cancellationToken)
            : await CreateVesselRequestFromJsonAsync(request, cancellationToken);

        try
        {
            return Results.Ok(await vesselManagementService.CreateVesselAsync(command, cancellationToken));
        }
        finally
        {
            DisposeImageStreams(command);
        }
    }

    private static async Task<IResult> UpdateVessel(
        IVesselManagementService vesselManagementService,
        Guid vesselId,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var command = request.HasFormContentType
            ? await UpdateVesselRequestFromFormAsync(vesselId, request, cancellationToken)
            : await UpdateVesselRequestFromJsonAsync(vesselId, request, cancellationToken);

        try
        {
            return Results.Ok(await vesselManagementService.UpdateVesselAsync(command, cancellationToken));
        }
        finally
        {
            DisposeImageStreams(command);
        }
    }

    private static async Task<IResult> UpdateVesselStatus(
        IVesselManagementService vesselManagementService,
        Guid vesselId,
        UpdateVesselStatusApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await vesselManagementService.UpdateVesselStatusAsync(
            new UpdateVesselStatusRequest(vesselId, request.Status),
            cancellationToken));

    private static async Task<IResult> DeleteVessel(
        IVesselManagementService vesselManagementService,
        Guid vesselId,
        CancellationToken cancellationToken) =>
        Results.Ok(await vesselManagementService.DeleteVesselAsync(vesselId, cancellationToken));

    private static async Task<CreateVesselRequest> CreateVesselRequestFromJsonAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var body = await request.ReadFromJsonAsync<CreateVesselJsonRequest>(cancellationToken: cancellationToken);
        return new CreateVesselRequest(
            body?.Code ?? string.Empty,
            body?.Name ?? string.Empty,
            VesselStatus.Inactive,
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

    private static async Task<CreateVesselRequest> CreateVesselRequestFromFormAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);

        return new CreateVesselRequest(
            GetFormValue(form, "code") ?? string.Empty,
            GetFormValue(form, "name") ?? string.Empty,
            VesselStatus.Inactive,
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

    private static async Task<UpdateVesselRequest> UpdateVesselRequestFromJsonAsync(
        Guid vesselId,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var body = await request.ReadFromJsonAsync<UpdateVesselJsonRequest>(cancellationToken: cancellationToken);
        return new UpdateVesselRequest(
            vesselId,
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

    private static async Task<UpdateVesselRequest> UpdateVesselRequestFromFormAsync(
        Guid vesselId,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);

        return new UpdateVesselRequest(
            vesselId,
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

    private static async Task<IReadOnlyCollection<VesselImageFileRequest>?> CreateImageFilesFromFormAsync(
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

    private static async Task<IReadOnlyCollection<VesselImageFileRequest>> CopyImageFilesAsync(
        IReadOnlyCollection<IFormFile> files,
        CancellationToken cancellationToken)
    {
        var copiedFiles = new List<VesselImageFileRequest>(files.Count);
        foreach (var file in files)
        {
            var content = new MemoryStream();
            await file.CopyToAsync(content, cancellationToken);
            content.Position = 0;
            copiedFiles.Add(new VesselImageFileRequest(
                file.FileName,
                file.ContentType,
                file.Length,
                content));
        }

        return copiedFiles;
    }

    private static void DisposeImageStreams(CreateVesselRequest command)
    {
        command.ImageContent?.Dispose();
        foreach (var imageFile in command.ImageFiles ?? [])
        {
            imageFile.Content.Dispose();
        }
    }

    private static void DisposeImageStreams(UpdateVesselRequest command)
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

    private static VesselRentalPriceRequest ToApplicationRentalPrice(VesselRentalPriceApiRequest request) =>
        new(request.RentalUnit, request.UnitPrice, request.Currency, request.Note);

    private static IReadOnlyCollection<VesselRentalPriceRequest>? CreateRentalPricesFromForm(IFormCollection form)
    {
        // FE gửi multipart theo dạng mảng có chỉ số: rentalPrices[i].rentalUnit/unitPrice/currency/note
        // (giống shape JSON). Ưu tiên đọc dạng này; nếu không có thì fallback key phẳng hourly*/daily*.
        var indexedRentalPrices = CreateIndexedRentalPricesFromForm(form);
        if (indexedRentalPrices is not null)
        {
            return indexedRentalPrices;
        }

        var rentalPrices = new List<VesselRentalPriceRequest>();
        var hourlyPrice = ParseOptionalDecimal(GetFormValue(form, "hourlyUnitPrice")
            ?? GetFormValue(form, "hourlyRentalPrice"));
        var dailyPrice = ParseOptionalDecimal(GetFormValue(form, "dailyUnitPrice")
            ?? GetFormValue(form, "dailyRentalPrice"));

        if (hourlyPrice.HasValue)
        {
            rentalPrices.Add(new VesselRentalPriceRequest(
                VesselRentalUnit.Hour,
                hourlyPrice.Value,
                GetFormValue(form, "hourlyCurrency"),
                GetFormValue(form, "hourlyNote")));
        }

        if (dailyPrice.HasValue)
        {
            rentalPrices.Add(new VesselRentalPriceRequest(
                VesselRentalUnit.Day,
                dailyPrice.Value,
                GetFormValue(form, "dailyCurrency"),
                GetFormValue(form, "dailyNote")));
        }

        return rentalPrices.Count == 0 ? null : rentalPrices;
    }

    // Đọc mảng giá thuê dạng có chỉ số trong multipart: rentalPrices[i].rentalUnit / unitPrice / currency / note.
    // Đây là convention FE dùng (khớp với shape JSON). Trả về null nếu form không chứa dạng này.
    private static IReadOnlyCollection<VesselRentalPriceRequest>? CreateIndexedRentalPricesFromForm(IFormCollection form)
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

        var rentalPrices = new List<VesselRentalPriceRequest>();
        foreach (var index in indices)
        {
            var rentalUnit = ParseOptionalEnum<VesselRentalUnit>(
                GetFormValue(form, $"rentalPrices[{index}].rentalUnit"));
            var unitPrice = ParseOptionalDecimal(
                GetFormValue(form, $"rentalPrices[{index}].unitPrice"));

            // Bỏ qua phần tử thiếu dữ liệu bắt buộc thay vì lưu giá rác.
            if (rentalUnit is null || unitPrice is null)
            {
                continue;
            }

            rentalPrices.Add(new VesselRentalPriceRequest(
                rentalUnit.Value,
                unitPrice.Value,
                GetFormValue(form, $"rentalPrices[{index}].currency"),
                GetFormValue(form, $"rentalPrices[{index}].note")));
        }

        return rentalPrices.Count == 0 ? null : rentalPrices;
    }

    private sealed record CreateVesselJsonRequest(
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
        IReadOnlyCollection<VesselRentalPriceApiRequest>? RentalPrices = null);

    private sealed record UpdateVesselJsonRequest(
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
        IReadOnlyCollection<VesselRentalPriceApiRequest>? RentalPrices = null);

    private sealed record UpdateVesselStatusApiRequest(VesselStatus Status);

    private sealed record VesselRentalPriceApiRequest(
        VesselRentalUnit RentalUnit,
        decimal UnitPrice,
        string? Currency = null,
        string? Note = null);
}
