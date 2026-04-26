using SaigonWaterbus.Application.Auth.Commands.RegisterUser;

namespace SaigonWaterbus.Web.Endpoints;

public class Auth : IEndpointGroup
{
    public static string RoutePrefix => "/api/auth";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(Register, "register");
    }

    public static async Task<IResult> Register(ISender sender, RegisterUserCommand command)
    {
        var result = await sender.Send(command);
        return Results.Ok(result);
    }
}
