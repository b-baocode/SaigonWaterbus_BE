using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using SaigonWaterbus.Application.Seats;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Seats : IEndpointGroup
{
    private const string GenerateExample =
        """
        {
          "decks": [
            {
              "deckNumber": 1,
              "rowCount": 20,
              "columnCount": 8,
              "seatBlocks": [
                { "startRow": 1, "startColumn": 1, "rowCount": 10, "columnCount": 4 },
                { "startRow": 1, "startColumn": 5, "rowCount": 10, "columnCount": 4 }
              ],
              "facilities": [
                { "type": "Toilet", "startRow": 15, "startColumn": 1, "rowSpan": 1, "columnSpan": 2 }
              ]
            }
          ]
        }
        """;

    private const string UpdateSeatExample =
        """
        {
          "code": "1-A1"
        }
        """;

    private const string UpdateStatusExample =
        """
        {
          "isActive": false
        }
        """;

    public static string RoutePrefix => "/api/vessels";

    public static string OpenApiTag => "Seats";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetSeats, "{vesselId:guid}/seats")
            .RequireAuthorization()
            .WithSummary("Lấy sơ đồ ghế của tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                null,
                "Trả về danh sách ghế nhóm theo tầng và hàng.",
                "Manager và Staff chỉ xem được ghế của tàu đang Active.",
                "TotalSeats là số ghế đăng ký của tàu.",
                "ConfiguredSeats là tổng số ghế thật đã setup trong database.",
                "Facilities là tiện ích thật đã setup trong database, ví dụ Toilet, không tính vào số ghế.",
                "SeatsConfigured=true khi tàu đã được setup đủ ghế."));

        groupBuilder.MapPost(GenerateSeats, "{vesselId:guid}/seats/generate")
            .RequireAuthorization()
            .WithSummary("Tự động sinh ghế theo cấu hình")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                GenerateExample,
                "Nếu không gửi seatBlocks, toàn bộ ma trận rowCount × columnCount sẽ được sinh thành ghế như cách cũ.",
                "Nếu gửi seatBlocks, rowCount × columnCount là ma trận layout vật lý; chỉ các ô trong seatBlocks được sinh thành ghế.",
                "Tổng ghế sinh ra từ seatBlocks phải bằng SeatCount của tàu.",
                "Facilities dùng để setup tiện ích như Toilet, không tính vào số ghế.",
                "Toilet phải chiếm đúng 2 ô: rowSpan=1,columnSpan=2 hoặc rowSpan=2,columnSpan=1.",
                "Nếu tàu đã có ghế, phải xóa toàn bộ trước khi generate lại.",
                "Mã ghế tự sinh theo format: {tầng}-{hàng}{cột}, ví dụ 1-A1, 2-B3."))
            .WithOpenApi(op => SetBodyExample(op, GenerateExample));

        groupBuilder.MapDelete(DeleteAllSeats, "{vesselId:guid}/seats")
            .RequireAuthorization()
            .WithSummary("Xóa toàn bộ ghế của tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Dùng khi muốn setup lại sơ đồ ghế từ đầu.",
                "Sau khi xóa có thể gọi lại API generate với cấu hình mới."));

        groupBuilder.MapPut(UpdateSeat, "{vesselId:guid}/seats/{seatId:guid}")
            .RequireAuthorization()
            .WithSummary("Cập nhật thông tin ghế")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateSeatExample,
                "Chỉ cho phép đổi mã ghế (Code).",
                "Mã ghế phải là duy nhất trong cùng một tàu."))
            .WithOpenApi(op => SetBodyExample(op, UpdateSeatExample));

        groupBuilder.MapPatch(UpdateSeatStatus, "{vesselId:guid}/seats/{seatId:guid}/status")
            .RequireAuthorization()
            .WithSummary("Bật/tắt ghế")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateStatusExample,
                "isActive=false để vô hiệu hóa ghế (ghế hỏng, bảo trì...).",
                "Ghế bị tắt sẽ không thể đặt vé."))
            .WithOpenApi(op => SetBodyExample(op, UpdateStatusExample));

        groupBuilder.MapDelete(DeleteSeat, "{vesselId:guid}/seats/{seatId:guid}")
            .RequireAuthorization()
            .WithSummary("Xóa một ghế")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Xóa vĩnh viễn một ghế khỏi tàu.",
                "Ưu tiên dùng API tắt ghế thay vì xóa nếu ghế chỉ tạm thời không dùng."));
    }

    private static async Task<IResult> GetSeats(
        ISeatManagementService seatManagementService,
        Guid vesselId,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.GetSeatsAsync(vesselId, cancellationToken));

    private static async Task<IResult> GenerateSeats(
        ISeatManagementService seatManagementService,
        Guid vesselId,
        GenerateSeatsApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.GenerateSeatsAsync(
            new GenerateSeatsRequest(vesselId, request.Decks),
            cancellationToken));

    private static async Task<IResult> DeleteAllSeats(
        ISeatManagementService seatManagementService,
        Guid vesselId,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.DeleteAllSeatsAsync(vesselId, cancellationToken));

    private static async Task<IResult> UpdateSeat(
        ISeatManagementService seatManagementService,
        Guid vesselId,
        Guid seatId,
        UpdateSeatApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.UpdateSeatAsync(
            new UpdateSeatRequest(vesselId, seatId, request.Code),
            cancellationToken));

    private static async Task<IResult> UpdateSeatStatus(
        ISeatManagementService seatManagementService,
        Guid vesselId,
        Guid seatId,
        UpdateSeatStatusApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.UpdateSeatStatusAsync(
            new UpdateSeatStatusRequest(vesselId, seatId, request.IsActive),
            cancellationToken));

    private static async Task<IResult> DeleteSeat(
        ISeatManagementService seatManagementService,
        Guid vesselId,
        Guid seatId,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.DeleteSeatAsync(vesselId, seatId, cancellationToken));

    private sealed record GenerateSeatsApiRequest(IReadOnlyCollection<DeckConfigDto> Decks);

    private sealed record UpdateSeatApiRequest(string? Code = null);

    private sealed record UpdateSeatStatusApiRequest(bool? IsActive);

    private static OpenApiOperation SetBodyExample(OpenApiOperation op, string exampleJson)
    {
        var content = op.RequestBody?.Content;
        if (content is null) return op;
        foreach (var ct in content.Values)
            ct.Example = new OpenApiString(exampleJson.Trim());
        return op;
    }
}
