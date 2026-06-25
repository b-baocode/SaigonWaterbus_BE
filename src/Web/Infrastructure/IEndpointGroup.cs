namespace SaigonWaterbus.Web.Infrastructure;

///
public interface IEndpointGroup
{
    ///
    static virtual string? RoutePrefix => null;

    ///
    static virtual string? OpenApiTag => null;

    static abstract void Map(RouteGroupBuilder groupBuilder);
}
