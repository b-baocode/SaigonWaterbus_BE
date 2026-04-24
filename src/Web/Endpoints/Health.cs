using SaigonWaterbus.Web.Infrastructure;

namespace SaigonWaterbus.Web.Endpoints;

public class Health : IEndpointGroup
{
    public static string RoutePrefix => "/api/health";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet("/", () => Results.Ok(new { status = "ok" }));
    }
}
