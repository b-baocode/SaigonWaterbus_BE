using System.Reflection;

namespace SaigonWaterbus.Web.Infrastructure;

public static class WebApplicationExtensions
{
    ///
    public static WebApplication MapEndpoints(this WebApplication app, Assembly assembly)
    {
        var endpointGroupTypes = assembly.GetExportedTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                     && t.IsAssignableTo(typeof(IEndpointGroup)));

        foreach (var type in endpointGroupTypes)
        {
            var groupName = type.Name;
            var routePrefix = type.GetProperty(nameof(IEndpointGroup.RoutePrefix))
                ?.GetValue(null) as string ?? $"/api/{groupName}";
            var openApiTag = type.GetProperty(nameof(IEndpointGroup.OpenApiTag))
                ?.GetValue(null) as string ?? groupName;
            var group = app.MapGroup(routePrefix);
            if (!string.IsNullOrWhiteSpace(openApiTag))
            {
                group.WithTags(openApiTag);
            }

            type.GetMethod(nameof(IEndpointGroup.Map))!.Invoke(null, [group]);
        }

        return app;
    }
}
