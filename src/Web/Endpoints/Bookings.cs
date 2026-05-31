using SaigonWaterbus.Application.Bookings;
using SaigonWaterbus.Web.Infrastructure;

namespace SaigonWaterbus.Web.Endpoints;

public sealed class Bookings : IEndpointGroup
{
    public static string RoutePrefix => "/api/bookings";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost(CreateBooking, string.Empty).RequireAuthorization();
    }

    private static async Task<IResult> CreateBooking(
        ISender sender, CreateBookingCommand command, CancellationToken ct) =>
        Results.Ok(await sender.Send(command, ct));
}
