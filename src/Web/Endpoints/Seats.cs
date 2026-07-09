using SaigonWaterbus.Application.Seats;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Seats : IEndpointGroup
{
    private const string GenerateMatrixExample =
        """
        {
          "decks": [
            {
              "deckNumber": 1,
              "rowCount": 2,
              "columnCount": 4
            }
          ]
        }
        """;

    private const string ConfigureExample =
        """
        {
          "decks": [
            {
              "deckNumber": 1,
              "rowCount": 5,
              "columnCount": 6,
                "cells": [
                  { "row": 1, "column": 3, "type": "Aisle" },
                  { "row": 2, "column": 3, "type": "Aisle" },
                  { "row": 3, "column": 1, "type": "Empty" },
                  { "row": 3, "column": 3, "type": "Aisle" },
                  { "row": 4, "column": 3, "type": "Aisle" },
                  { "row": 5, "column": 3, "type": "Aisle" },
                  { "row": 5, "column": 4, "type": "Seat", "seatTypeCode": "RIVER" }
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

    public static string RoutePrefix => "/api/boats";

    public static string OpenApiTag => "Seats";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetSeats, "{boatId:guid}/seats")
            .RequireAuthorization()
            .WithSummary("Lấy sơ đồ ghế của tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin, Manager hoặc Staff",
                null,
                "Trả về danh sách ghế nhóm theo tầng và hàng.",
                "Manager và Staff chỉ xem được ghế của tàu đang Active và đã setup đủ ghế.",
                "TotalSeats là số ghế đăng ký của tàu.",
                "ConfiguredSeats là tổng số ghế thật đã setup trong database.",
                "SeatsConfigured=true khi tàu đã được setup đủ ghế."));

        groupBuilder.MapPost(GenerateSeats, "{boatId:guid}/seats/generate")
            .RequireAuthorization()
            .WithSummary("Sinh ma trận layout ghế")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                GenerateMatrixExample,
                "API này chỉ sinh ma trận vật lý theo số tầng, số hàng và số cột.",
                "Response trả cells mặc định type=Empty để frontend hiển thị ô trống.",
                "Loại ghế seed mặc định: STANDARD, CABIN, RIVER, SKY.",
                "Sau bước này frontend chọn ô nào là ghế bằng cách đổi type=Seat, hoặc chọn Aisle/Empty cho lối đi và ô trống.",
                "Tàu vẫn SeatsConfigured=false và Status=Inactive.",
                "Nếu tàu đã có ma trận hoặc sơ đồ ghế, phải xóa toàn bộ trước khi generate lại."));

        groupBuilder.MapPost(ConfigureSeats, "{boatId:guid}/seats/configure")
            .RequireAuthorization()
            .WithSummary("Setup ghế từ ma trận đã sinh")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                ConfigureExample,
                "Dùng sau khi đã sinh ma trận bằng /seats/generate.",
                "Kiểu FullStandard: toàn bộ ghế là STANDARD.",
                "Kiểu StandardAndVip: mặc định là CABIN; FE có thể đánh dấu ô Seat bằng seatTypeCode đã seed trong database: CABIN, RIVER hoặc SKY.",
                "cells là danh sách override; ô không gửi sẽ mặc định là Seat.",
                "Mỗi override cell có type=Seat/Aisle/Empty.",
                "Chỉ cần gửi Aisle/Empty cho vị trí không phải ghế, hoặc gửi Seat kèm seatTypeCode để đổi loại ghế.",
                "Kiểu ghế được lưu trực tiếp trên ghế của tàu; giá vé lấy theo seatTypeCode, không theo số trạm.",
                "Aisle/Empty không lưu thành ghế; frontend dựng lại từ ma trận rowCount × columnCount và danh sách ghế.",
                "Backend tự cập nhật SeatCount bằng tổng số ô Seat được tạo.",
                "Cần có ít nhất 1 ô Seat.",
                "Nếu tàu đã có ghế, phải xóa toàn bộ trước khi configure lại.",
                "Khi setup hợp lệ, backend lưu ghế vào database và đặt SeatsConfigured=true.",
                "Nếu tàu đã đủ 4 hồ sơ thì backend tự chuyển tàu sang Active; nếu chưa đủ hồ sơ thì tàu vẫn Inactive/UnderMaintenance.",
                "Mã ghế tự sinh theo format: {tầng}-{hàng}{cột}, ví dụ 1-A1, 2-B3."));

        groupBuilder.MapDelete(DeleteAllSeats, "{boatId:guid}/seats")
            .RequireAuthorization()
            .WithSummary("Xóa toàn bộ ghế của tàu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Dùng khi muốn setup lại sơ đồ ghế từ đầu.",
                "Backend reset SeatCount về 0; SeatCount mới sẽ được tính lại ở lần configure tiếp theo.",
                "Sau khi xóa có thể gọi lại API generate với cấu hình mới."));

        groupBuilder.MapPut(UpdateSeat, "{boatId:guid}/seats/{seatId:guid}")
            .RequireAuthorization()
            .WithSummary("Cập nhật thông tin ghế")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateSeatExample,
                "Chỉ cho phép đổi mã ghế (Code).",
                "Mã ghế phải là duy nhất trong cùng một tàu."));

        groupBuilder.MapPatch(UpdateSeatStatus, "{boatId:guid}/seats/{seatId:guid}/status")
            .RequireAuthorization()
            .WithSummary("Bật/tắt ghế")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                UpdateStatusExample,
                "isActive=false để vô hiệu hóa ghế (ghế hỏng, bảo trì...).",
                "Ghế bị tắt sẽ không thể đặt vé."));

        groupBuilder.MapDelete(DeleteSeat, "{boatId:guid}/seats/{seatId:guid}")
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
        Guid boatId,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.GetSeatsAsync(boatId, cancellationToken));

    private static async Task<IResult> GenerateSeats(
        ISeatManagementService seatManagementService,
        Guid boatId,
        GenerateSeatMatrixApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.GenerateSeatMatrixAsync(
            new GenerateSeatMatrixRequest(boatId, request.Decks),
            cancellationToken));

    private static async Task<IResult> ConfigureSeats(
        ISeatManagementService seatManagementService,
        Guid boatId,
        ConfigureSeatsApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.ConfigureSeatsAsync(
            new GenerateSeatsRequest(boatId, request.Decks),
            cancellationToken));

    private static async Task<IResult> DeleteAllSeats(
        ISeatManagementService seatManagementService,
        Guid boatId,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.DeleteAllSeatsAsync(boatId, cancellationToken));

    private static async Task<IResult> UpdateSeat(
        ISeatManagementService seatManagementService,
        Guid boatId,
        Guid seatId,
        UpdateSeatApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.UpdateSeatAsync(
            new UpdateSeatRequest(boatId, seatId, request.Code),
            cancellationToken));

    private static async Task<IResult> UpdateSeatStatus(
        ISeatManagementService seatManagementService,
        Guid boatId,
        Guid seatId,
        UpdateSeatStatusApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.UpdateSeatStatusAsync(
            new UpdateSeatStatusRequest(boatId, seatId, request.IsActive),
            cancellationToken));

    private static async Task<IResult> DeleteSeat(
        ISeatManagementService seatManagementService,
        Guid boatId,
        Guid seatId,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.DeleteSeatAsync(boatId, seatId, cancellationToken));

    private sealed record GenerateSeatMatrixApiRequest(IReadOnlyCollection<DeckMatrixConfigDto> Decks);

    private sealed record ConfigureSeatsApiRequest(IReadOnlyCollection<DeckConfigDto> Decks);

    private sealed record UpdateSeatApiRequest(string? Code = null);

    private sealed record UpdateSeatStatusApiRequest(bool? IsActive);
}
