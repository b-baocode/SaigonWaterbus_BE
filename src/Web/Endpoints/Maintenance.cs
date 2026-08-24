using SaigonWaterbus.Application.Maintenance;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Maintenance : IEndpointGroup
{
    public static string RoutePrefix => "/api/maintenance";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet(GetDatabaseMigrations, "database/migrations")
            .RequireAuthorization()
            .WithSummary("Danh sach migration cua database dang ket noi")
            .WithDescription(OpenApiDescriptionBuilder.Build(
                "Admin",
                null,
                "Doi chieu migration bien dich trong code voi bang __EFMigrationsHistory cua DB dang ket noi.",
                "pendingCount > 0 nghia la schema that dang cu hon code — migration o du an nay chay tay nen rat de lech.",
                "migrations[].isApplied=false la migration chua chay tren DB nay.",
                "Migration da chay tren DB nhung khong con trong code cung duoc liet ke (isApplied=true).",
                "Chi doc, khong chay migration nao."));
    }

    private static async Task<IResult> GetDatabaseMigrations(ISender sender, CancellationToken ct) =>
        Results.Ok(await sender.Send(new GetDatabaseMigrationsQuery(), ct));
}
