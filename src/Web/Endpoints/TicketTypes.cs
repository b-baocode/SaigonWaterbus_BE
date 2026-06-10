using SaigonWaterbus.Application.TicketTypes;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class TicketTypes : IEndpointGroup
{
    public static string RoutePrefix => "/api/ticket-types";

    private const string CreateExample =
        """
        {
          "ticketTypeCode": "ADULT",
          "ticketTypeName": "Ve nguoi lon",
          "description": "Ve gia nguoi lon, ap dung cho hanh khach tu 18 tuoi tro len",
          "priceModifier": 1.00,
          "pointsEarnedRate": 10
        }
        """;

    private const string UpdateExample =
        """
        {
          "ticketTypeName": "Ve nguoi lon",
          "description": "Cap nhat mo ta",
          "priceModifier": 1.00,
          "pointsEarnedRate": 10,
          "isActive": true
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetTicketTypes, string.Empty)
            .AllowAnonymous()
            .WithSummary("Danh sach loai ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Anonymous",
                null,
                "Tra ve tat ca loai ve dang hoat dong (isActive = true).",
                "priceModifier: he so gia (1.0 = gia goc, 0.5 = giam 50%)."));

        group.MapPost(CreateTicketType, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao loai ve moi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                CreateExample,
                "TicketTypeCode phai unique (tu dong uppercase).",
                "priceModifier > 0. Vi du: Adult=1.0, Student=0.8, Child=0.5.",
                "pointsEarnedRate: so diem tich luy tren moi don vi gia (co the = 0)."));

        group.MapPut(UpdateTicketType, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat loai ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                UpdateExample,
                "isActive = false de an loai ve, khong xoa du lieu.",
                "TicketTypeCode khong doi duoc sau khi tao."));

        group.MapDelete(DeleteTicketType, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Vo hieu hoa loai ve")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Soft delete: dat isActive = false, khong xoa khoi DB.",
                "Tra ve 204 khi thanh cong."));
    }

    private static async Task<IResult> GetTicketTypes(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetTicketTypeListQuery(), ct));

    private static async Task<IResult> CreateTicketType(ISender sender, CreateTicketTypeCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateTicketType(ISender sender, Guid id, UpdateTicketTypeRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateTicketTypeCommand(
            id, req.TicketTypeName, req.Description, req.PriceModifier, req.PointsEarnedRate, req.IsActive), ct));

    private static async Task<IResult> DeleteTicketType(ISender sender, Guid id, CancellationToken ct)
    {
        await sender.Send(new DeleteTicketTypeCommand(id), ct);
        return Results.NoContent();
    }

    public sealed record UpdateTicketTypeRequest(
        string TicketTypeName, string? Description,
        decimal PriceModifier, int PointsEarnedRate, bool IsActive);
}
