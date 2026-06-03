using SaigonWaterbus.Application.Boats;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Boats : IEndpointGroup
{
    public static string RoutePrefix => "/api/boats";

    private const string CreateBoatExample =
        """
        {
          "boatCode": "WB-02",
          "boatName": "Waterbus 02",
          "capacity": 80,
          "description": "Tau tuyen 01 huong nguoc"
        }
        """;

    private const string UpdateBoatExample =
        """
        {
          "boatName": "Waterbus 02 (nang cap)",
          "capacity": 90,
          "boatStatus": "Active",
          "description": "Cap nhat mo ta"
        }
        """;

    private const string CreateSeatsExample =
        """
        {
          "rows": 10,
          "columns": 8,
          "seatClass": "Standard"
        }
        """;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetBoats, string.Empty)
            .RequireAuthorization()
            .WithSummary("Danh sach tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                null,
                "Tra ve tat ca tau (ca Active lan Inactive).",
                "Chi admin/staff moi co quyen xem."));

        group.MapPost(CreateBoat, string.Empty)
            .RequireAuthorization()
            .WithSummary("Tao tau moi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                CreateBoatExample,
                "BoatCode phai unique (tu dong uppercase).",
                "BoatStatus mac dinh la Active.",
                "Sau khi tao tau, them ghe bang POST /api/boats/{id}/seats/batch."));

        group.MapPut(UpdateBoat, "{id:guid}")
            .RequireAuthorization()
            .WithSummary("Cap nhat thong tin tau")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                UpdateBoatExample,
                "BoatStatus hop le: Active | Inactive | Maintenance.",
                "BoatCode khong doi duoc sau khi tao."));

        group.MapPost(CreateBoatSeats, "{id:guid}/seats/batch")
            .RequireAuthorization()
            .WithSummary("Tao ghe hang loat (luoi rows x columns)")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Bearer token",
                CreateSeatsExample,
                "Sinh seat_number kieu A1..A8, B1..B8, ..., J1..J8.",
                "rows=10, columns=8 → 80 ghe, ten A1-J8.",
                "seatClass: Window | Standard | VIP (optional).",
                "Khong tao ghe trung voi seat_number da ton tai trong cung tau."));
    }

    private static async Task<IResult> GetBoats(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetBoatListQuery(), ct));

    private static async Task<IResult> CreateBoat(ISender sender, CreateBoatCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));

    private static async Task<IResult> UpdateBoat(ISender sender, Guid id, UpdateBoatRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new UpdateBoatCommand(id, req.BoatName, req.Capacity, req.BoatStatus, req.Description), ct));

    private static async Task<IResult> CreateBoatSeats(ISender sender, Guid id, CreateSeatsRequest req, CancellationToken ct) =>
        Results.Ok(await sender.Send(new CreateSeatsCommand(id, req.Rows, req.Columns, req.SeatClass), ct));

    public sealed record UpdateBoatRequest(string BoatName, int Capacity, string BoatStatus, string? Description);
    public sealed record CreateSeatsRequest(int Rows, int Columns, string? SeatClass);
}
