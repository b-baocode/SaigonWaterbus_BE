using SaigonWaterbus.Application.PublicBoard;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class PublicBoard : IEndpointGroup
{
    public static string RoutePrefix => "/api/public";

    public static string OpenApiTag => "PublicBoard";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetDepartureBoard, "departure-board")
            .AllowAnonymous()
            .WithSummary("Bảng điện tử công cộng")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Chỉ hiển thị chuyến thường (RegularTrip). Custom booking thuê tàu không xuất hiện ở bảng công cộng.",
                "Query optional: stationId hoặc stationCode để lọc theo bến.",
                "lookAheadMinutes mặc định 180, tối đa 1440.",
                "includeDepartedMinutes mặc định 20, dùng để giữ chuyến vừa rời bến một thời gian ngắn.",
                "displayStatus: Upcoming, Boarding, ArrivingSoon, Departed hoặc Arrived."));
    }

    private static async Task<IResult> GetDepartureBoard(
        ISender sender,
        Guid? stationId,
        string? stationCode,
        int? lookAheadMinutes,
        int? includeDepartedMinutes,
        CancellationToken cancellationToken) =>
        Results.Ok(await sender.Send(
            new GetPublicDepartureBoardQuery(
                stationId,
                stationCode,
                lookAheadMinutes ?? 180,
                includeDepartedMinutes ?? 20),
            cancellationToken));
}
