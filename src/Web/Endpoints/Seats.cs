using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
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
              "rowCount": 2,
              "columnCount": 4,
              "cells": [
                { "row": 1, "column": 1, "type": "Seat", "seatTypeCode": "STANDARD" },
                { "row": 1, "column": 2, "type": "Seat", "seatTypeCode": "STANDARD" },
                { "row": 1, "column": 3, "type": "Aisle" },
                { "row": 1, "column": 4, "type": "Seat", "seatTypeCode": "VIP" },
                { "row": 2, "column": 1, "type": "Seat", "seatTypeCode": "STANDARD" },
                { "row": 2, "column": 3, "type": "Toilet", "rowSpan": 1, "columnSpan": 2 }
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
                "Manager và Staff chỉ xem được ghế của tàu đang Active và đã setup đủ ghế.",
                "TotalSeats là số ghế đăng ký của tàu.",
                "ConfiguredSeats là tổng số ghế thật đã setup trong database.",
                "Facilities là tiện ích thật đã setup trong database, ví dụ Toilet, không tính vào số ghế.",
                "SeatsConfigured=true khi tàu đã được setup đủ ghế."));

        groupBuilder.MapPost(GenerateSeats, "{vesselId:guid}/seats/generate")
            .RequireAuthorization()
            .WithSummary("Sinh ma trận layout ghế")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                GenerateMatrixExample,
                "API này chỉ sinh ma trận vật lý theo số tầng, số hàng và số cột.",
                "Chưa tạo ghế thật, chưa tạo WC, chưa chọn VIP/Standard.",
                "Sau bước này frontend dùng rowCount × columnCount để hiển thị lưới cho admin click chọn từng ô.",
                "Tàu vẫn SeatsConfigured=false và Status=Inactive.",
                "Nếu tàu đã có ma trận hoặc sơ đồ ghế, phải xóa toàn bộ trước khi generate lại."))
            .WithOpenApi(op => SetBodyExample(op, GenerateMatrixExample));

        groupBuilder.MapPost(ConfigureSeats, "{vesselId:guid}/seats/configure")
            .RequireAuthorization()
            .WithSummary("Setup ghế từ ma trận đã sinh")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                ConfigureExample,
                "Dùng sau khi đã sinh ma trận bằng /seats/generate.",
                "Mỗi ô có type=Seat/Aisle/Empty/Toilet.",
                "Ô Seat có thể gửi seatTypeCode. Nếu bỏ trống thì mặc định STANDARD.",
                "Backend tự kiểm tra seatTypeCode theo dịch vụ: Waterbus chỉ STANDARD, WaterSightseeing có thể VIP nếu service có seat type VIP.",
                "Aisle/Empty không lưu thành ghế; frontend dựng lại từ ma trận rowCount × columnCount và danh sách ghế/WC.",
                "Tổng số ô Seat phải bằng SeatCount của tàu.",
                "Toilet phải chiếm đúng 2 ô: rowSpan=1,columnSpan=2 hoặc rowSpan=2,columnSpan=1.",
                "Nếu tàu đã có ghế/WC, phải xóa toàn bộ trước khi configure lại.",
                "Khi setup hợp lệ, backend lưu ghế/WC vào database, đặt SeatsConfigured=true và chuyển tàu sang Active.",
                "Mã ghế tự sinh theo format: {tầng}-{hàng}{cột}, ví dụ 1-A1, 2-B3."))
            .WithOpenApi(op => SetBodyExample(op, ConfigureExample));

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
        GenerateSeatMatrixApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.GenerateSeatMatrixAsync(
            new GenerateSeatMatrixRequest(vesselId, request.Decks),
            cancellationToken));

    private static async Task<IResult> ConfigureSeats(
        ISeatManagementService seatManagementService,
        Guid vesselId,
        ConfigureSeatsApiRequest request,
        CancellationToken cancellationToken) =>
        Results.Ok(await seatManagementService.ConfigureSeatsAsync(
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

    private sealed record GenerateSeatMatrixApiRequest(IReadOnlyCollection<DeckMatrixConfigDto> Decks);

    private sealed record ConfigureSeatsApiRequest(IReadOnlyCollection<DeckConfigDto> Decks);

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
