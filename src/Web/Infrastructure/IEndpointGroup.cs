namespace SaigonWaterbus.Web.Infrastructure;

/// <summary>
/// Defines a group of related Minimal API endpoints.
/// Implementations are automatically discovered and registered as a route group.
/// By default the route prefix is <c>/api/{ClassName}</c>; override <see cref="RoutePrefix"/>
/// to use a custom path, including nested resource paths such as <c>/api/Management/Users</c>.
/// </summary>
public interface IEndpointGroup
{
    /// <summary>
    /// The route prefix for this endpoint group.
    /// Defaults to <c>/api/{ClassName}</c>. Override to specify a custom or nested path.
    /// </summary>
    static virtual string? RoutePrefix => null;

    /// <summary>
    /// The OpenAPI tag for this route group. Defaults to the class name.
    /// Return an empty string to skip the parent tag and let nested groups define tags.
    /// </summary>
    static virtual string? OpenApiTag => null;

    static abstract void Map(RouteGroupBuilder groupBuilder);
}
