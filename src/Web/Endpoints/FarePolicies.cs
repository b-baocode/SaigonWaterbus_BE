using SaigonWaterbus.Application.Fares;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class FarePolicies : IEndpointGroup
{
    public static string RoutePrefix => "/api/fare-policy";

    private const string UpdateExample =
        """
        {
          "baseFare": 5000,
          "pricePerKm": 1500,
          "roundingStep": 1000,
          "minFare": null
        }
        """;

    private const string WeekendAdjustmentExample =
        """
        {
          "surchargePercent": 20,
          "isActive": true
        }
        """;

    private const string CalendarDayAdjustmentExample =
        """
        {
          "date": "2026-09-02",
          "scope": "Holiday",
          "name": "Quoc khanh",
          "surchargePercent": 50,
          "isActive": true
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetFarePolicy, string.Empty)
            .AllowAnonymous()
            .WithSummary("Cong thuc gia ve theo quang duong")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Gia ve trip Regular (ghe STANDARD) = RoundUp(baseFare + pricePerKm x km, roundingStep), toi thieu minFare neu co.",
                "Km cua chang = tong distance_from_previous_km cua cac route stops giua tram len va tram xuong.",
                "Neu route Regular thieu km cho chang dang ban thi search tra isBookable=false, seat-map/booking tra validation; backend khong fallback gia STANDARD.",
                "Waterbus thuong: INFANT/SENIOR/DISABLED he so 0, CHILD he so 0.5.",
                "Sightseeing: CHILD/SENIOR/DISABLED dung chung % giam tai /api/ticket-types/sightseeing-concession, INFANT he so 0."));

        group.MapPut(UpdateFarePolicy, string.Empty)
            .RequireAuthorization()
            .WithSummary("Chinh cong thuc gia ve theo quang duong")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                UpdateExample,
                "Ap dung ngay cho cac booking tao sau khi chinh (gia da chot trong booking cu khong doi).",
                "baseFare/pricePerKm/minFare: so nguyen VND. roundingStep: 1 | 100 | 500 | 1000 (lam tron LEN).",
                "Chi ap dung trip Regular voi ghe STANDARD; sightseeing (CABIN/RIVER/SKY) van tinh theo loai ghe."));

        group.MapGet(GetFareAdjustments, "adjustments")
            .AllowAnonymous()
            .WithSummary("Danh sach phu thu gia ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Query optional: fromDate, toDate (yyyy-MM-dd).",
                "Weekend la rule chung cho thu Bay/Chu nhat. Holiday/Special la ngay cu the FE tick tren calendar.",
                "Gia ban = gia goc/chang x (1 + surchargePercent/100), backend tu lam tron den 2 chu so thap phan."));

        group.MapGet(GetEffectiveFareAdjustment, "adjustments/effective")
            .AllowAnonymous()
            .WithSummary("Phu thu dang ap dung cho mot ngay")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Query date=yyyy-MM-dd.",
                "Neu ngay co Holiday/Special thi uu tien ngay cu the; neu khong co thi thu Bay/Chu nhat lay rule Weekend.",
                "Tra null neu ngay khong co phu thu."));

        group.MapPut(UpsertWeekendFareAdjustment, "adjustments/weekend")
            .RequireAuthorization()
            .WithSummary("Cau hinh phu thu cuoi tuan")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                WeekendAdjustmentExample,
                "Ap dung tu dong cho tat ca trip co operatingDate la thu Bay/Chu nhat, tru khi ngay do co Holiday/Special rieng.",
                "surchargePercent=20 nghia la tang 20%; neu gui 12.345 backend luu 12.35. isActive=false de tat phu thu cuoi tuan."));

        group.MapPut(UpsertFareCalendarDay, "adjustments/calendar-day")
            .RequireAuthorization()
            .WithSummary("Danh dau ngay le/dac biet de phu thu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                CalendarDayAdjustmentExample,
                "scope chi nhan Holiday hoac Special.",
                "FE tick ngay nao thi goi API nay cho ngay do. Cung mot ngay co ca Holiday/Special thi Special duoc uu tien khi tinh gia.",
                "Gia booking cu da thanh toan/da tao passenger khong doi; booking tao sau se lay gia moi."));

        group.MapDelete(DeleteFareCalendarDay, "adjustments/calendar-day")
            .RequireAuthorization()
            .WithSummary("Xoa ngay le/dac biet khoi lich phu thu")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin hoac Manager",
                null,
                "Query date=yyyy-MM-dd&scope=Holiday|Special.",
                "Chi xoa rule ngay cu the, khong xoa rule Weekend."));
    }

    private static async Task<IResult> GetFarePolicy(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetFarePolicyQuery(), ct));

    private static async Task<IResult> UpdateFarePolicy(
        ISender sender, UpdateFarePolicyCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> GetFareAdjustments(
        ISender sender,
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetFareAdjustmentsQuery(fromDate, toDate), ct));

    private static async Task<IResult> GetEffectiveFareAdjustment(
        ISender sender,
        DateOnly date,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetEffectiveFareAdjustmentQuery(date), ct));

    private static async Task<IResult> UpsertWeekendFareAdjustment(
        ISender sender,
        UpsertWeekendFareAdjustmentCommand command,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpsertFareCalendarDay(
        ISender sender,
        UpsertFareCalendarDayCommand command,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> DeleteFareCalendarDay(
        ISender sender,
        DateOnly date,
        string scope,
        CancellationToken ct)
    {
        await sender.Send(new DeleteFareCalendarDayCommand(date, scope), ct);
        return Results.NoContent();
    }
}
