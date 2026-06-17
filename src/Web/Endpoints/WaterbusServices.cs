using SaigonWaterbus.Application.WaterbusServices;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class WaterbusServices : IEndpointGroup
{
    private const string CreateServiceExample =
        """
        {
          "code": "PUBLIC",
          "name": "WaterBus cong cong",
          "description": "Dich vu WaterBus cong cong theo tuyen.",
          "isActive": true,
          "displayOrder": 1
        }
        """;

    private const string UpdateServiceExample =
        """
        {
          "name": "WaterBus du lich",
          "description": "Dich vu WaterBus du lich theo plan.",
          "displayOrder": 2
        }
        """;

    private const string UpdateStatusExample =
        """
        {
          "isActive": false
        }
        """;

    public static string RoutePrefix => "/api/waterbus/services";

    public static string OpenApiTag => "Services";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetAllWaterbusServices)
            .RequireAuthorization()
            .WithSummary("Lấy danh sách dịch vụ WaterBus")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                null,
                "Admin thấy tất cả dịch vụ active và inactive.",
                "Manager và Staff chỉ thấy dịch vụ active."));

        groupBuilder.MapGet(GetWaterbusServiceById, "{serviceId:guid}")
            .RequireAuthorization()
            .WithSummary("Lấy chi tiết dịch vụ WaterBus")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                null,
                "Manager và Staff chỉ xem được dịch vụ đang active.",
                "Admin xem được cả dịch vụ đã ẩn."));

        groupBuilder.MapGet(GetWaterbusServiceSeatTypes, "{serviceId:guid}/seat-types")
            .RequireAuthorization()
            .WithSummary("Lấy danh sách loại ghế")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                null,
                "Trả về danh sách loại ghế global từ bảng seat_types.",
                "Không còn cấu hình giá loại ghế theo dịch vụ."));

        groupBuilder.MapPost(CreateWaterbusService, "")
            .RequireAuthorization()
            .WithSummary("Tạo dịch vụ WaterBus")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                CreateServiceExample,
                "Code nên ngắn gọn, ví dụ PUBLIC hoặc TOURIST.",
                "Dữ liệu được lưu trong database, không seed cứng trong code."));

        groupBuilder.MapPut(UpdateWaterbusService, "{serviceId:guid}")
            .RequireAuthorization()
            .WithSummary("Cập nhật dịch vụ WaterBus")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateServiceExample,
                "Chỉ field nào gửi lên mới được cập nhật.",
                "Code được chuẩn hóa thành chữ in hoa."));

        groupBuilder.MapPatch(UpdateWaterbusServiceStatus, "status/{serviceId:guid}")
            .RequireAuthorization()
            .WithSummary("Cập nhật trạng thái hiển thị dịch vụ WaterBus")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateStatusExample,
                "isActive=true để hiện, false để ẩn.",
                "Dịch vụ bị ẩn vẫn hiện với Admin, nhưng không hiện với Manager và Staff."));

        groupBuilder.MapDelete(DeleteWaterbusService, "{serviceId:guid}")
            .RequireAuthorization()
            .WithSummary("Xóa dịch vụ WaterBus")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Chỉ xóa khi dịch vụ chưa được các bảng plan/fare tham chiếu.",
                "Sau khi có plan/fare, nên ưu tiên ẩn dịch vụ bằng API status thay vì xóa."));
    }

    private static async Task<IResult> GetAllWaterbusServices(
        IWaterbusServiceManagementService waterbusServiceManagementService,
        CancellationToken cancellationToken) =>
        Results.Ok(await waterbusServiceManagementService.GetServicesAsync(includeInactive: true, cancellationToken));

    private static async Task<IResult> GetWaterbusServiceById(
        IWaterbusServiceManagementService waterbusServiceManagementService,
        Guid serviceId,
        CancellationToken cancellationToken) =>
        Results.Ok(await waterbusServiceManagementService.GetServiceByIdAsync(serviceId, cancellationToken));

    private static async Task<IResult> GetWaterbusServiceSeatTypes(
        IWaterbusServiceManagementService waterbusServiceManagementService,
        Guid serviceId,
        CancellationToken cancellationToken) =>
        Results.Ok(await waterbusServiceManagementService.GetServiceSeatTypesAsync(serviceId, cancellationToken));

    private static async Task<IResult> CreateWaterbusService(
        IWaterbusServiceManagementService waterbusServiceManagementService,
        CreateWaterbusServiceApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await waterbusServiceManagementService.CreateServiceAsync(
            new CreateWaterbusServiceRequest(
                request.Code,
                request.Name,
                request.Description,
                request.IsActive,
                request.DisplayOrder,
                request.BookingMode),
            cancellationToken));

    private static async Task<IResult> UpdateWaterbusService(
        IWaterbusServiceManagementService waterbusServiceManagementService,
        Guid serviceId,
        UpdateWaterbusServiceApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await waterbusServiceManagementService.UpdateServiceAsync(
            new UpdateWaterbusServiceRequest(
                serviceId,
                request.Code,
                request.Name,
                request.Description,
                request.DisplayOrder,
                request.BookingMode),
            cancellationToken));

    private static async Task<IResult> UpdateWaterbusServiceStatus(
        IWaterbusServiceManagementService waterbusServiceManagementService,
        Guid serviceId,
        UpdateWaterbusServiceStatusApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await waterbusServiceManagementService.UpdateServiceStatusAsync(
            new UpdateWaterbusServiceStatusRequest(
                serviceId,
                request.IsActive),
            cancellationToken));

    private static async Task<IResult> DeleteWaterbusService(
        IWaterbusServiceManagementService waterbusServiceManagementService,
        Guid serviceId,
        CancellationToken cancellationToken) =>
        Results.Ok(await waterbusServiceManagementService.DeleteServiceAsync(serviceId, cancellationToken));

    public sealed record CreateWaterbusServiceApiRequest(
        string Code,
        string Name,
        string? Description = null,
        bool IsActive = true,
        int DisplayOrder = 0,
        SaigonWaterbus.Domain.Enums.BookingMode BookingMode = SaigonWaterbus.Domain.Enums.BookingMode.SeatBased);

    public sealed record UpdateWaterbusServiceApiRequest(
        string? Code = null,
        string? Name = null,
        string? Description = null,
        int? DisplayOrder = null,
        SaigonWaterbus.Domain.Enums.BookingMode? BookingMode = null);

    public sealed record UpdateWaterbusServiceStatusApiRequest(
        bool IsActive);

}
