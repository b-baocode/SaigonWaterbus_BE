using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using SaigonWaterbus.Application.Vessels;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Vessels : IEndpointGroup
{
    private const string CreateVesselExample =
        """
        {
          "waterbusServiceId": "00000000-0000-0000-0000-000000000000",
          "code": "WB01",
          "name": "Tau Waterbus 01",
          "seatCount": 80,
          "passengerCapacity": 80,
          "numberOfDecks": 1,
          "registrationNumber": "SG-1234",
          "maxSpeedKmh": 30,
          "yearBuilt": 2020,
          "description": "Tau cong cong tuyen so 1."
        }
        """;

    private const string UpdateVesselExample =
        """
        {
          "name": "Tau Waterbus 01 (moi)",
          "seatCount": 90
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
                "Có thể lọc theo serviceId, status và từ khóa tìm kiếm."));

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
                "Nếu có ảnh, gửi multipart/form-data với các field tương ứng và field 'image'.",
                "Ảnh chỉ hỗ trợ JPEG, PNG hoặc WebP, tối đa 5 MB.",
                "Code tàu được chuẩn hóa thành chữ in hoa.",
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
                "Có thể gửi application/json nếu không đổi ảnh.",
                "Nếu đổi ảnh, gửi multipart/form-data với field 'image'."));

        groupBuilder.MapPatch(UpdateVesselStatus, "status/{vesselId:guid}")
            .RequireAuthorization()
            .WithSummary("Cập nhật trạng thái tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateStatusExample,
                "Các trạng thái hợp lệ: Active, Maintenance, Inactive, Retired.",
                "Muốn chuyển Active thì tàu phải setup đủ ghế.",
                "Tàu ở trạng thái không phải Active hoặc chưa setup ghế sẽ không hiện với Manager và Staff."))
            .WithOpenApi(op => SetBodyExample(op, UpdateStatusExample));

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
        [FromQuery] Guid? serviceId,
        [FromQuery] VesselStatus? status,
        [FromQuery] string? search,
        CancellationToken cancellationToken) =>
        Results.Ok(await vesselManagementService.GetVesselsAsync(serviceId, status, search, cancellationToken));

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
            command.ImageContent?.Dispose();
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
            command.ImageContent?.Dispose();
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
            body?.WaterbusServiceId ?? Guid.Empty,
            body?.Code ?? string.Empty,
            body?.Name ?? string.Empty,
            VesselStatus.Inactive,
            body?.SeatCount ?? 0,
            body?.PassengerCapacity ?? 0,
            body?.NumberOfDecks ?? 0,
            body?.RegistrationNumber,
            body?.MaxSpeedKmh,
            body?.YearBuilt,
            body?.Description,
            body?.ImageUrl);
    }

    private static async Task<CreateVesselRequest> CreateVesselRequestFromFormAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files["image"] ?? form.Files["Image"] ?? form.Files.FirstOrDefault();

        return new CreateVesselRequest(
            ParseOptionalGuid(GetFormValue(form, "waterbusServiceId")) ?? Guid.Empty,
            GetFormValue(form, "code") ?? string.Empty,
            GetFormValue(form, "name") ?? string.Empty,
            VesselStatus.Inactive,
            ParseOptionalInt(GetFormValue(form, "seatCount")) ?? 0,
            ParseOptionalInt(GetFormValue(form, "passengerCapacity")) ?? 0,
            ParseOptionalInt(GetFormValue(form, "numberOfDecks")) ?? 0,
            GetFormValue(form, "registrationNumber"),
            ParseOptionalInt(GetFormValue(form, "maxSpeedKmh")),
            ParseOptionalInt(GetFormValue(form, "yearBuilt")),
            GetFormValue(form, "description"),
            GetFormValue(form, "imageUrl"),
            file?.FileName,
            file?.ContentType,
            file?.Length,
            file?.OpenReadStream());
    }

    private static async Task<UpdateVesselRequest> UpdateVesselRequestFromJsonAsync(
        Guid vesselId,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var body = await request.ReadFromJsonAsync<UpdateVesselJsonRequest>(cancellationToken: cancellationToken);
        return new UpdateVesselRequest(
            vesselId,
            body?.WaterbusServiceId,
            body?.Code,
            body?.Name,
            body?.SeatCount,
            body?.PassengerCapacity,
            body?.NumberOfDecks,
            body?.RegistrationNumber,
            body?.MaxSpeedKmh,
            body?.YearBuilt,
            body?.Description,
            body?.ImageUrl);
    }

    private static async Task<UpdateVesselRequest> UpdateVesselRequestFromFormAsync(
        Guid vesselId,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files["image"] ?? form.Files["Image"] ?? form.Files.FirstOrDefault();

        return new UpdateVesselRequest(
            vesselId,
            ParseOptionalGuid(GetFormValue(form, "waterbusServiceId")),
            GetFormValue(form, "code"),
            GetFormValue(form, "name"),
            ParseOptionalInt(GetFormValue(form, "seatCount")),
            ParseOptionalInt(GetFormValue(form, "passengerCapacity")),
            ParseOptionalInt(GetFormValue(form, "numberOfDecks")),
            GetFormValue(form, "registrationNumber"),
            ParseOptionalInt(GetFormValue(form, "maxSpeedKmh")),
            ParseOptionalInt(GetFormValue(form, "yearBuilt")),
            GetFormValue(form, "description"),
            GetFormValue(form, "imageUrl"),
            file?.FileName,
            file?.ContentType,
            file?.Length,
            file?.OpenReadStream());
    }

    private static string? GetFormValue(IFormCollection form, string name)
    {
        var value = form[name].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int? ParseOptionalInt(string? value) =>
        int.TryParse(value, out var result) ? result : null;

    private static Guid? ParseOptionalGuid(string? value) =>
        Guid.TryParse(value, out var result) ? result : null;

    private static T? ParseOptionalEnum<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var result) ? result : null;

    private sealed record CreateVesselJsonRequest(
        Guid WaterbusServiceId,
        string Code,
        string Name,
        int SeatCount,
        int PassengerCapacity,
        int NumberOfDecks,
        string? RegistrationNumber = null,
        int? MaxSpeedKmh = null,
        int? YearBuilt = null,
        string? Description = null,
        string? ImageUrl = null);

    private sealed record UpdateVesselJsonRequest(
        Guid? WaterbusServiceId = null,
        string? Code = null,
        string? Name = null,
        int? SeatCount = null,
        int? PassengerCapacity = null,
        int? NumberOfDecks = null,
        string? RegistrationNumber = null,
        int? MaxSpeedKmh = null,
        int? YearBuilt = null,
        string? Description = null,
        string? ImageUrl = null);

    private sealed record UpdateVesselStatusApiRequest(VesselStatus Status);

    private static OpenApiOperation SetBodyExample(OpenApiOperation op, string exampleJson)
    {
        var content = op.RequestBody?.Content;
        if (content is null) return op;
        foreach (var ct in content.Values)
            ct.Example = new OpenApiString(exampleJson.Trim());
        return op;
    }
}
