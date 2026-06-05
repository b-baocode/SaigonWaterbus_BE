using SaigonWaterbus.Application.Seats;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Seats : IEndpointGroup
{
    private const string GenerateExample =
        """
        {
          "decks": [
            { "deckNumber": 1, "rowCount": 4, "columnCount": 8 },
            { "deckNumber": 2, "rowCount": 3, "columnCount": 6 }
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
        groupBuilder.MapGet(GetSeats, "{vesselId:int}/seats")
            .RequireAuthorization()
            .WithSummary("Lấy sơ đồ ghế của tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                null,
                "Trả về danh sách ghế nhóm theo tầng và hàng.",
                "Manager và Staff chỉ xem được ghế của tàu đang Active.",
                "TotalSeats là số ghế đăng ký của tàu.",
                "ConfiguredSeats là tổng số ghế thật đã setup trong database.",
                "SeatsConfigured=true khi tàu đã được setup đủ ghế."));

        groupBuilder.MapPost(GenerateSeats, "{vesselId:int}/seats/generate")
            .RequireAuthorization()
            .WithSummary("Tự động sinh ghế theo cấu hình")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                GenerateExample,
                "Tổng ghế (rowCount × columnCount × số tầng) phải bằng SeatCount của tàu.",
                "Nếu tàu đã có ghế, phải xóa toàn bộ trước khi generate lại.",
                "Mã ghế tự sinh theo format: {tầng}-{hàng}{cột}, ví dụ 1-A1, 2-B3."));

        groupBuilder.MapDelete(DeleteAllSeats, "{vesselId:int}/seats")
            .RequireAuthorization()
            .WithSummary("Xóa toàn bộ ghế của tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Dùng khi muốn setup lại sơ đồ ghế từ đầu.",
                "Sau khi xóa có thể gọi lại API generate với cấu hình mới."));

        groupBuilder.MapPut(UpdateSeat, "{vesselId:int}/seats/{seatId:int}")
            .RequireAuthorization()
            .WithSummary("Cập nhật thông tin ghế")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateSeatExample,
                "Chỉ cho phép đổi mã ghế (Code).",
                "Mã ghế phải là duy nhất trong cùng một tàu."));

        groupBuilder.MapPatch(UpdateSeatStatus, "{vesselId:int}/seats/{seatId:int}/status")
            .RequireAuthorization()
            .WithSummary("Bật/tắt ghế")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateStatusExample,
                "isActive=false để vô hiệu hóa ghế (ghế hỏng, bảo trì...).",
                "Ghế bị tắt sẽ không thể đặt vé."));

        groupBuilder.MapDelete(DeleteSeat, "{vesselId:int}/seats/{seatId:int}")
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
        int vesselId,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.GetSeatsAsync(vesselId, cancellationToken));

    private static async Task<IResult> GenerateSeats(
        ISeatManagementService seatManagementService,
        int vesselId,
        GenerateSeatsApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.GenerateSeatsAsync(
            new GenerateSeatsRequest(vesselId, request.Decks),
            cancellationToken));

    private static async Task<IResult> DeleteAllSeats(
        ISeatManagementService seatManagementService,
        int vesselId,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.DeleteAllSeatsAsync(vesselId, cancellationToken));

    private static async Task<IResult> UpdateSeat(
        ISeatManagementService seatManagementService,
        int vesselId,
        int seatId,
        UpdateSeatApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.UpdateSeatAsync(
            new UpdateSeatRequest(vesselId, seatId, request.Code),
            cancellationToken));

    private static async Task<IResult> UpdateSeatStatus(
        ISeatManagementService seatManagementService,
        int vesselId,
        int seatId,
        UpdateSeatStatusApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.UpdateSeatStatusAsync(
            new UpdateSeatStatusRequest(vesselId, seatId, request.IsActive),
            cancellationToken));

    private static async Task<IResult> DeleteSeat(
        ISeatManagementService seatManagementService,
        int vesselId,
        int seatId,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.DeleteSeatAsync(vesselId, seatId, cancellationToken));

    private sealed record GenerateSeatsApiRequest(IReadOnlyCollection<DeckConfigDto> Decks);

    private sealed record UpdateSeatApiRequest(string? Code = null);

    private sealed record UpdateSeatStatusApiRequest(bool? IsActive);
}
