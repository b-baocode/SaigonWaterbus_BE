using SaigonWaterbus.Application.TripSchedules;
using SaigonWaterbus.Domain.Enums;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class TripSchedules : IEndpointGroup
{
    public static string RoutePrefix => "/api/trip-schedules";

    private const string CreateExample =
        """
        {
          "routeCode": "R01-BD-TD",
          "vesselId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
          "departureTime": "07:00:00",
          "recurrenceType": "Weekly",
          "weeklyDays": "1,2,3,4,5",
          "monthlyDay": null,
          "horizonDays": 30,
          "validFrom": "2026-07-01",
          "validTo": null
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(ListSchedules, string.Empty)
            .RequireAuthorization(PolicyNames.AdminOnly)
            .WithSummary("Danh sach lich trinh (admin)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Query param: isActive (bool, optional)."));

        group.MapPost(CreateSchedule, string.Empty)
            .RequireAuthorization(PolicyNames.AdminOnly)
            .WithSummary("Tao lich trinh moi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                CreateExample,
                "recurrenceType: Daily | Weekly | Monthly.",
                "weeklyDays: chuoi DayOfWeek cach nhau dau phay, vd '1,2,3,4,5' = Thu 2-6 (0=CN, 1=T2...).",
                "monthlyDay: ngay trong thang (1-31), chi dung khi recurrenceType=Monthly.",
                "horizonDays: sinh trip truoc bao nhieu ngay (mac dinh 30, toi da 365)."));

        group.MapPut(UpdateSchedule, "{id:guid}")
            .RequireAuthorization(PolicyNames.AdminOnly)
            .WithSummary("Cap nhat lich trinh")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Chi cho phep sua horizonDays, validTo va isActive.",
                "De thay doi recurrence/vessel/route: xoa va tao lai."));

        group.MapPost(TriggerGeneration, "generate-now")
            .RequireAuthorization(PolicyNames.AdminOnly)
            .WithSummary("Kich hoat tao trip ngay lap tuc")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Chay GenerateScheduledTripsCommand ngay, khong can doi job 00:05.",
                "Tra ve so trip da tao va loi neu co."));
    }

    private static async Task<IResult> ListSchedules(
        ISender sender,
        bool? isActive,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new ListTripSchedulesQuery(isActive), ct));

    private static async Task<IResult> CreateSchedule(
        ISender sender,
        CreateTripScheduleCommand command,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateSchedule(
        ISender sender,
        Guid id,
        UpdateTripScheduleRequest req,
        CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateTripScheduleCommand(id, req.HorizonDays, req.ValidTo, req.IsActive), ct));

    private static async Task<IResult> TriggerGeneration(
        ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(new GenerateScheduledTripsCommand(), ct);
        return Results.Ok(result);
    }

    public sealed record UpdateTripScheduleRequest(
        int HorizonDays,
        DateOnly? ValidTo,
        bool IsActive);
}
